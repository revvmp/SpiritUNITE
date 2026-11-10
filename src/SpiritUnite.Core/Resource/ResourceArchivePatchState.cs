using System.Text.Json;

namespace SpiritUnite.Resource;

public sealed record ResourceArchivePatchState(string OriginalMd5, string PatchDigest, int Generation, string Md5, long Length)
{
    public const string SidecarExtension = ".patch.json";

    public static string SidecarPath(string archivePath) => archivePath + SidecarExtension;

    public bool Describes(string originalMd5, string patchDigest) =>
        OriginalMd5 == originalMd5 && PatchDigest == patchDigest && Generation > 0;

    public static async Task<ResourceArchivePatchState?> ReadAsync(string archivePath, CancellationToken cancellationToken)
    {
        try
        {
            await using var sidecar = File.OpenRead(SidecarPath(archivePath));
            return await JsonSerializer.DeserializeAsync<ResourceArchivePatchState>(sidecar, cancellationToken: cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    public async Task WriteAsync(string archivePath, CancellationToken cancellationToken)
    {
        await using var sidecar = File.Create(SidecarPath(archivePath));
        await JsonSerializer.SerializeAsync(sidecar, this, cancellationToken: cancellationToken);
    }
}
