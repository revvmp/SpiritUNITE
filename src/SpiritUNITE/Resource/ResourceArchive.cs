namespace SpiritUNITE.Resource;

public sealed record ResourceArchive(int ProductId, long Version, string Md5, string Url, string SecureUrl, string FilePath, long Length);
