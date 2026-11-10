using System.Text.Json;

namespace SpiritUnite.Resource;

public sealed record ResourceArchivePatchState(string OriginalMd5, string PatchDigest, int Generation, string Md5, long Length, ulong Version = 0)
{
    public const string SidecarExtension = ".patch.json";
    public const string ProductStateFileName = "patch-state.json";

    public static string SidecarPath(string archivePath) => archivePath + SidecarExtension;

    public bool Describes(string originalMd5, string patchDigest) =>
        OriginalMd5 == originalMd5 && PatchDigest == patchDigest && Generation > 0;

    public static async Task<ResourceArchivePatchState?> ReadLatestAsync(string archivePath, int productId, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(archivePath)!;
        var latest = await ReadFileAsync(Path.Combine(directory, ProductStateFileName), null, cancellationToken);
        foreach (var path in Directory.EnumerateFiles(directory, "*" + ResourceArchiveFileName.Extension + SidecarExtension))
        {
            var name = Path.GetFileName(path)[..^SidecarExtension.Length];
            if (!ResourceArchiveFileName.TryParse(name, out var savedProductId, out var originalVersion) || savedProductId != productId)
            {
                throw new InvalidDataException($"Patch state {path} does not name an archive for product {productId}");
            }

            var state = await ReadFileAsync(path, unchecked((ulong)originalVersion), cancellationToken);
            if (state is not null && (latest is null || state.Version > latest.Version))
            {
                latest = state;
            }
            else if (state is not null && latest is not null && state.Version == latest.Version && state.Md5 != latest.Md5)
            {
                throw new InvalidDataException($"Patch state {path} conflicts with another archive at the same served version");
            }
        }

        return latest;
    }

    public async Task WriteAsync(string archivePath, CancellationToken cancellationToken)
    {
        await WriteFileAsync(SidecarPath(archivePath), cancellationToken);
        await WriteFileAsync(Path.Combine(Path.GetDirectoryName(archivePath)!, ProductStateFileName), cancellationToken);
    }

    private static async Task<ResourceArchivePatchState?> ReadFileAsync(string path, ulong? originalVersion, CancellationToken cancellationToken)
    {
        try
        {
            await using var sidecar = File.OpenRead(path);
            var state = await JsonSerializer.DeserializeAsync<ResourceArchivePatchState>(sidecar, cancellationToken: cancellationToken);
            if (state is null || state.Generation is <= 0 or > ushort.MaxValue || state.Length <= 0
                || !IsHex(state.OriginalMd5, 32) || !IsHex(state.Md5, 32) || !IsHex(state.PatchDigest, 64))
            {
                throw new InvalidDataException($"Patch state {path} has invalid fields");
            }

            if (state.Version == 0 && originalVersion is { } original && (original & ushort.MaxValue) + (ulong)state.Generation <= ushort.MaxValue)
            {
                state = state with { Version = original + (ulong)state.Generation };
            }

            if (state.Version == 0)
            {
                throw new InvalidDataException($"Patch state {path} has no valid served version");
            }

            if (originalVersion is { } baseVersion && ((baseVersion & ushort.MaxValue) + (ulong)state.Generation > ushort.MaxValue
                || state.Version != baseVersion + (ulong)state.Generation))
            {
                throw new InvalidDataException($"Patch state {path} has a served version that does not match its archive and generation");
            }

            return state;
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"Patch state {path} is not valid JSON; restore it before serving updates", exception);
        }
    }

    private async Task WriteFileAsync(string path, CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(this);
        try
        {
            if ((await File.ReadAllBytesAsync(path, cancellationToken)).AsSpan().SequenceEqual(bytes))
            {
                return;
            }
        }
        catch (FileNotFoundException)
        {
        }

        var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var sidecar = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await sidecar.WriteAsync(bytes, cancellationToken);
                await sidecar.FlushAsync(cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    private static bool IsHex(string? value, int length) => value is not null && value.Length == length && value.All(char.IsAsciiHexDigit);
}
