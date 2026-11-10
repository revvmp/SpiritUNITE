using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using SpiritUnite.Databin;

namespace SpiritUnite.Resource.Ifs;

public sealed record PreparedIfsPatch(IReadOnlyDictionary<string, byte[]> Files, byte[] FileList, byte[] VersionFile, string BundleVersionName);

public static partial class IfsPatcher
{
    public const string Dlc0Folder = "1.2.1/DLC_0/";
    public const string FileListPath = Dlc0Folder + "filelist.txt";
    public const string VersionFilePath = Dlc0Folder + "Version.txt";

    private const string ListFilePath = "(listfile)";
    private const uint FileFixKey = 0x00020000;
    private const byte ChunkPresent = 1;
    private const string BundleVersionNameKey = "BundleVersionName";
    private const string FileListMd5Key = "FileListMD5";

    public static PreparedIfsPatch Prepare(IfsArchive archive, IReadOnlyList<DatabinStringPatch> patches)
    {
        var fileList = Encoding.UTF8.GetString(archive.Extract(Require(archive, FileListPath)));
        var versionFile = Encoding.UTF8.GetString(archive.Extract(Require(archive, VersionFilePath)));
        var databins = patches.Select(patch => patch.DatabinName).Distinct(StringComparer.Ordinal).ToList();
        var zips = new Dictionary<string, StoredZip>(StringComparer.Ordinal);
        var homes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match line in FileListZipLine().Matches(fileList))
        {
            var path = line.Groups["path"].Value;
            if (archive.Find(path) is not { } entry)
            {
                continue;
            }

            var zip = StoredZip.Parse(archive.Extract(entry));
            foreach (var databin in databins.Where(databin => !homes.ContainsKey(databin) && zip.Contains(DatabinEnvelope.EntryName(databin))))
            {
                homes[databin] = path;
                zips[path] = zip;
            }
        }

        if (databins.FirstOrDefault(databin => !homes.ContainsKey(databin)) is { } missing)
        {
            throw new InvalidDataException($"No DLC_0 zip in the archive holds {missing}");
        }

        foreach (var databin in databins)
        {
            var zip = zips[homes[databin]];
            var entryName = DatabinEnvelope.EntryName(databin);
            var table = PbcTable.Parse(DatabinEnvelope.Decode(zip.Read(entryName), databin));
            foreach (var patch in patches.Where(patch => patch.DatabinName == databin))
            {
                var keys = patch.RowKey is { } key ? [key] : table.Keys.Distinct().ToList();
                foreach (var rowKey in keys)
                {
                    if (table.ReplaceString(rowKey, patch.FieldNumber, patch.Value) == 0)
                    {
                        throw new InvalidDataException($"{databin} row {rowKey} has no field {patch.FieldNumber}");
                    }
                }
            }

            zip.Replace(entryName, DatabinEnvelope.Encode(table.ToArray(), databin));
        }

        var files = zips.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray(), StringComparer.Ordinal);
        foreach (var (path, data) in files)
        {
            fileList = ReplaceFileListLine(fileList, path, data);
        }

        var fileListBytes = Encoding.UTF8.GetBytes(fileList);
        var bundleVersionName = IniValue(versionFile, BundleVersionNameKey);
        versionFile = SetIniValue(versionFile, FileListMd5Key, Convert.ToHexStringLower(MD5.HashData(fileListBytes)));
        return new PreparedIfsPatch(files, fileListBytes, Encoding.UTF8.GetBytes(versionFile), bundleVersionName);
    }

    public static string PatchedVersionName(string bundleVersionName, int generation)
    {
        var parts = bundleVersionName.Split('.');
        if (parts.Length != 4 || !ushort.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out var last) || last + generation > ushort.MaxValue)
        {
            throw new InvalidDataException($"BundleVersionName {bundleVersionName} cannot take patch generation {generation}");
        }

        parts[3] = (last + generation).ToString(CultureInfo.InvariantCulture);
        return string.Join('.', parts);
    }

    public static PatchedIfsLayout Assemble(IfsArchive archive, PreparedIfsPatch prepared, string patchedVersionName)
    {
        var versionFile = Encoding.UTF8.GetBytes(SetIniValue(Encoding.UTF8.GetString(prepared.VersionFile), BundleVersionNameKey, patchedVersionName));
        var replacedFiles = prepared.Files
            .Append(new(FileListPath, prepared.FileList))
            .Append(new(VersionFilePath, versionFile))
            .ToList();

        var originalDataEnd = archive.Header.DataEnd;
        using var tail = new MemoryStream();
        var replacements = new List<IfsEntry>();
        foreach (var (path, data) in replacedFiles)
        {
            var original = Require(archive, path);
            if (original.Flags != (IfsArchive.FileExists | IfsArchive.FileCompress))
            {
                throw new InvalidDataException($"{path} uses flags {original.Flags:x}; only plain compressed entries are rewritten");
            }

            Pad(tail, originalDataEnd, IfsHeader.RawChunkSize);
            var compressed = archive.Compress(data);
            replacements.Add(original with
            {
                FilePos = originalDataEnd + tail.Position,
                FileSize = data.Length,
                CmpSize = compressed.Length,
                Md5 = MD5.HashData(data),
            });
            WriteWithChunkMd5s(tail, compressed);
        }

        var listFile = Require(archive, ListFilePath);
        if ((listFile.Flags & FileFixKey) != 0)
        {
            throw new InvalidDataException("The archive's (listfile) is keyed by its position and cannot move");
        }

        Pad(tail, originalDataEnd, IfsHeader.RawChunkSize);
        replacements.Add(listFile with { FilePos = originalDataEnd + tail.Position });
        tail.Write(archive.ReadAt(listFile.FilePos, checked((int)(listFile.CmpSize + ChunkMd5sLength(listFile.CmpSize)))));

        var hetPosition = originalDataEnd + tail.Position;
        var hetBlock = archive.ReadAt(archive.Header.HetPosition, checked((int)(archive.Header.BetPosition - archive.Header.HetPosition)));
        tail.Write(hetBlock);
        var betPosition = originalDataEnd + tail.Position;
        var betTable = archive.BetTableWith(replacements);
        WriteWithChunkMd5s(tail, betTable);
        Pad(tail, originalDataEnd, IfsHeader.RawChunkSize);
        var dataEnd = originalDataEnd + tail.Position;
        var header = archive.Header.Relocated(hetPosition, betPosition, dataEnd, archive.HetTable, betTable);

        var originalChunks = checked((int)(originalDataEnd / IfsHeader.RawChunkSize));
        var chunkCount = checked((int)(dataEnd / IfsHeader.RawChunkSize));
        var chunkMd5s = new byte[(chunkCount + 1) * MD5.HashSizeInBytes];
        archive.ReadAt(archive.Header.ChunkMd5TablePosition, originalChunks * MD5.HashSizeInBytes).CopyTo(chunkMd5s, 0);
        var firstChunk = archive.ReadAt(0, IfsHeader.RawChunkSize);
        header.CopyTo(firstChunk, 0);
        MD5.HashData(firstChunk, chunkMd5s.AsSpan(0, MD5.HashSizeInBytes));
        var tailBytes = tail.GetBuffer();
        for (var chunk = originalChunks; chunk < chunkCount; chunk++)
        {
            var start = (chunk - originalChunks) * IfsHeader.RawChunkSize;
            MD5.HashData(tailBytes.AsSpan(start, IfsHeader.RawChunkSize), chunkMd5s.AsSpan(chunk * MD5.HashSizeInBytes, MD5.HashSizeInBytes));
        }

        MD5.HashData(chunkMd5s.AsSpan(0, chunkCount * MD5.HashSizeInBytes), chunkMd5s.AsSpan(chunkCount * MD5.HashSizeInBytes));
        tail.Write(chunkMd5s);
        tail.Write(Enumerable.Repeat(ChunkPresent, chunkCount).ToArray());
        return new PatchedIfsLayout(archive.Path, header, originalDataEnd, tail.ToArray());
    }

    private static void WriteWithChunkMd5s(MemoryStream tail, ReadOnlySpan<byte> stored)
    {
        tail.Write(stored);
        Span<byte> md5 = stackalloc byte[MD5.HashSizeInBytes];
        for (var offset = 0; offset < stored.Length; offset += IfsHeader.RawChunkSize)
        {
            MD5.HashData(stored.Slice(offset, Math.Min(IfsHeader.RawChunkSize, stored.Length - offset)), md5);
            tail.Write(md5);
        }
    }

    private static long ChunkMd5sLength(long storedLength) =>
        (storedLength + IfsHeader.RawChunkSize - 1) / IfsHeader.RawChunkSize * MD5.HashSizeInBytes;

    private static IfsEntry Require(IfsArchive archive, string path) =>
        archive.Find(path) ?? throw new InvalidDataException($"The archive has no {path}");

    private static void Pad(MemoryStream tail, long tailBase, int alignment)
    {
        var remainder = (int)((tailBase + tail.Position) % alignment);
        if (remainder != 0)
        {
            tail.Write(new byte[alignment - remainder]);
        }
    }

    private static string ReplaceFileListLine(string fileList, string path, byte[] data)
    {
        var line = new Regex($@"(?m)^{Regex.Escape(path)}=(?<md5>[0-9a-f]{{32}}),(?<size>\d+),");
        var matches = line.Matches(fileList);
        if (matches.Count != 1)
        {
            throw new InvalidDataException($"filelist.txt lists {path} {matches.Count} times");
        }

        var match = matches[0];
        var replacement = $"{path}={Convert.ToHexStringLower(MD5.HashData(data))},{data.Length.ToString(CultureInfo.InvariantCulture)},";
        return string.Concat(fileList.AsSpan(0, match.Index), replacement, fileList.AsSpan(match.Index + match.Length));
    }

    private static string IniValue(string ini, string key)
    {
        var match = IniLine(key).Match(ini);
        return match.Success ? match.Groups["value"].Value : throw new InvalidDataException($"Version.txt has no {key}");
    }

    private static string SetIniValue(string ini, string key, string value)
    {
        var line = IniLine(key);
        if (line.Count(ini) != 1)
        {
            throw new InvalidDataException($"Version.txt does not hold exactly one {key}");
        }

        return line.Replace(ini, $"{key}={value}");
    }

    private static Regex IniLine(string key) => new($@"(?m)^{Regex.Escape(key)}=(?<value>[^\r\n]*)");

    [GeneratedRegex(@"(?m)^(?<path>1\.2\.1/DLC_0/[^/\r\n=]+\.zip)=", RegexOptions.CultureInvariant)]
    private static partial Regex FileListZipLine();
}
