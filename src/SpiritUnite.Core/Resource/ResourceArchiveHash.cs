using System.Security.Cryptography;
using System.Text.Json;

namespace SpiritUnite.Resource;

public sealed record ResourceArchiveHash(string Md5, long Length, long LastWriteTimeUtcTicks)
{
    public const string SidecarExtension = ".md5";
    private const int HashBufferBytes = 4 * 1024 * 1024;

    public static string SidecarPath(string archivePath) => archivePath + SidecarExtension;

    public bool Describes(FileInfo archive) =>
        Length == archive.Length
        && LastWriteTimeUtcTicks == archive.LastWriteTimeUtc.Ticks
        && Md5.Length == MD5.HashSizeInBytes * 2
        && Md5.All(char.IsAsciiHexDigitLower);

    public static async Task<ResourceArchiveHash?> ReadSidecarAsync(string archivePath, CancellationToken cancellationToken)
    {
        try
        {
            await using var sidecar = File.OpenRead(SidecarPath(archivePath));
            return await JsonSerializer.DeserializeAsync<ResourceArchiveHash>(sidecar, cancellationToken: cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    public async Task WriteSidecarAsync(string archivePath, CancellationToken cancellationToken)
    {
        await using var sidecar = File.Create(SidecarPath(archivePath));
        await JsonSerializer.SerializeAsync(sidecar, this, cancellationToken: cancellationToken);
    }

    public static async Task<ResourceArchiveHash> ComputeAsync(FileInfo archive, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            archive.FullName,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            HashBufferBytes,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var digest = await MD5.HashDataAsync(stream, cancellationToken);
        return new ResourceArchiveHash(Convert.ToHexStringLower(digest), archive.Length, archive.LastWriteTimeUtc.Ticks);
    }
}
