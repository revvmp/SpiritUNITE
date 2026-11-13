using System.Collections.Frozen;
using System.Globalization;
using System.Security.Cryptography;
using Google.Protobuf;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Resource;

public sealed record ServerSyncResource(string ResName, string ResMD5, ByteString Bytes)
{
    private const int DatabinResType = 1;

    public pbCOMDT_RES_INFO ToResInfo(bool withResType) => new()
    {
        ResName = ResName,
        ResMD5 = ResMD5,
        ResType = withResType ? DatabinResType : 0,
    };
}

public sealed class ServerSyncResources
{
    public static readonly string DirectoryName = Path.Combine("Databin", "ServerSync");

    public const int OfficialSvrResVersion = 1198717;

    private const string FileExtension = ".bytes";

    private readonly FrozenDictionary<string, ServerSyncResource> resourcesByName;

    private ServerSyncResources(IReadOnlyList<ServerSyncResource> resources)
    {
        Resources = resources;
        resourcesByName = resources.ToFrozenDictionary(resource => resource.ResName, StringComparer.Ordinal);
    }

    public IReadOnlyList<ServerSyncResource> Resources { get; }

    public static ServerSyncResources Load(string directory)
    {
        var resources = Directory.EnumerateFiles(directory, "*" + FileExtension)
            .Select(ReadResource)
            .OrderBy(resource => resource.ResName.Length)
            .ThenBy(resource => resource.ResName, StringComparer.Ordinal)
            .ToArray();
        return new ServerSyncResources(resources);
    }

    public ServerSyncResource? Find(string resName) => resourcesByName.GetValueOrDefault(resName);

    private static ServerSyncResource ReadResource(string path)
    {
        var resName = Path.GetFileNameWithoutExtension(path);
        if (!uint.TryParse(resName, NumberStyles.None, CultureInfo.InvariantCulture, out _))
        {
            throw new InvalidDataException($"Server sync resource {path} is not named after its databin name hash");
        }

        var bytes = File.ReadAllBytes(path);
        return new ServerSyncResource(resName, Convert.ToHexStringLower(MD5.HashData(bytes)), ByteString.CopyFrom(bytes));
    }
}
