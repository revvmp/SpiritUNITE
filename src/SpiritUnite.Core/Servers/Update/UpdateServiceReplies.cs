using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SpiritUnite.Network.Gcp;
using SpiritUnite.Network.Thrift;
using SpiritUnite.Network.Tuap;
using SpiritUnite.Resource;
using SpiritUnite.Servers.Lobby;

namespace SpiritUnite.Servers.Update;

public sealed class UpdateServiceReplies(
    IOptions<UpdateServerOptions> updateServer,
    IOptions<LobbyAddressOptions> lobbyAddress,
    ResourceArchiveCatalog catalog,
    AppUpdatePackages appUpdatePackages,
    ILogger<UpdateServiceReplies> logger)
{
    public const string QueryAddrSvr = "QueryAddrSvr";
    public const string PufferUpdateService = "PufferUpdateService";
    public const string VersionUpdate = "VersionUpdate";

    public const int kUpdateSuccess = 0;
    public const int kUpdatePackageNotFound = 20002;
    public const int kUpdateTypeNot = 0;
    public const int kUpdateTypeOptional = 1;

    private const int QueryAddrMinCompressSize = 1024;
    private const int UpdateMinCompressSize = 512;
    private const string PufferAppId = "6.53.1";
    private const string VersionUpdateAppId = "2.51.1";
    private const int VersionUpdateRegionId = 7001;

    private static readonly IReadOnlyDictionary<string, TuapService> Services = new Dictionary<string, TuapService>(StringComparer.Ordinal)
    {
        [QueryAddrSvr] = new(QueryAddrSvr, new GcpCompression(GcpCompression.Lz4CompressMode, QueryAddrMinCompressSize)),
        [PufferUpdateService] = new(PufferUpdateService, new GcpCompression(GcpCompression.Lz4CompressMode, UpdateMinCompressSize)),
        [VersionUpdate] = new(VersionUpdate, new GcpCompression(GcpCompression.Lz4CompressMode, UpdateMinCompressSize)),
    };

    public static TuapService? FindService(string name) => Services.GetValueOrDefault(name);

    public ThriftStruct? Answer(TuapService service, ThriftMessage call, EndPoint? remoteEndPoint)
    {
        if (call.Type != ThriftMessageType.Call || call.Arguments is not { } arguments)
        {
            logger.LogWarning("Update request {Name} from {RemoteEndPoint} refused: not a call with arguments", call.Name, remoteEndPoint);
            return null;
        }

        return call.Name switch
        {
            $"{QueryAddrSvr}:QueryAddrInfo" when service.Name == QueryAddrSvr => QueryAddrInfo(arguments, remoteEndPoint),
            $"{PufferUpdateService}:GetUpdateInfo" when service.Name == PufferUpdateService => GetUpdateInfo(arguments, remoteEndPoint),
            $"{VersionUpdate}:ReqUpdateVersion" when service.Name == VersionUpdate => ReqUpdateVersion(arguments, remoteEndPoint),
            _ => Refuse(call.Name, service.Name, remoteEndPoint),
        };
    }

    private ThriftStruct? QueryAddrInfo(ThriftStruct arguments, EndPoint? remoteEndPoint)
    {
        if (arguments.FindInteger(QueryAddrReqField.type, ThriftType.I64) is not { } type)
        {
            logger.LogWarning("QueryAddrInfo from {RemoteEndPoint} refused: no i64 type", remoteEndPoint);
            return null;
        }

        var name = arguments.FindString(QueryAddrReqField.name) ?? string.Empty;
        var gameId = arguments.FindInteger(QueryAddrReqField.gameid, ThriftType.I64);
        var host = updateServer.Value.AdvertisedHost(lobbyAddress.Value);
        var port = updateServer.Value.Port;
        logger.LogInformation("QueryAddrInfo from {RemoteEndPoint} for game {GameId} type {Type} {Name}: {Host}:{Port}", remoteEndPoint, gameId, type, name.Length <= 32 ? name : name[..32], host, port);
        return AddrInfo(kUpdateSuccess, [new ThriftStruct([
            new ThriftField(AddrField.ip, ThriftBinary.String(host)),
            new ThriftField(AddrField.port, ThriftInteger.I32(port)),
        ])]);
    }

    private ThriftStruct? GetUpdateInfo(ThriftStruct arguments, EndPoint? remoteEndPoint)
    {
        if (arguments.FindStruct(PufferUpdateReqField.comm) is not { } comm || comm.FindInteger(UpdateCommInfoField.product_id, ThriftType.I32) is not { } productId)
        {
            logger.LogWarning("GetUpdateInfo from {RemoteEndPoint} refused: no comm.product_id", remoteEndPoint);
            return null;
        }

        if (!catalog.TryGet((int)productId, out var archive))
        {
            logger.LogInformation("GetUpdateInfo from {RemoteEndPoint} for product {ProductId}: no archive, kUpdatePackageNotFound", remoteEndPoint, productId);
            return new ThriftStruct([
                new ThriftField(PufferUpdateRspField.result, ThriftInteger.I32(kUpdatePackageNotFound)),
                new ThriftField(PufferUpdateRspField.comm, comm),
                new ThriftField(PufferUpdateRspField.app_id, ThriftBinary.String(PufferAppId)),
            ]);
        }

        var dolphinAppVersion = comm.FindInteger(UpdateCommInfoField.dolphin_app_version, ThriftType.I64) ?? 0;
        logger.LogInformation("GetUpdateInfo from {RemoteEndPoint} for product {ProductId} app version {AppVersion}: version {Version} md5 {Md5} at {Url}", remoteEndPoint, productId, AppUpdatePackages.VersionName(dolphinAppVersion), AppUpdatePackages.VersionName(archive.Version), archive.Md5, archive.Url);
        var updateInfo = new ThriftStruct([
            new ThriftField(UpdateInfoField.url, ThriftBinary.String(archive.Url)),
            new ThriftField(UpdateInfoField.md5, ThriftBinary.String(archive.Md5)),
            new ThriftField(UpdateInfoField.custom_str, ThriftBinary.String(string.Empty)),
            new ThriftField(UpdateInfoField.desc, ThriftBinary.String(string.Empty)),
            new ThriftField(UpdateInfoField.version, ThriftInteger.I64(archive.Version)),
            new ThriftField(UpdateInfoField.url2, ThriftBinary.String(archive.SecureUrl)),
        ]);
        return new ThriftStruct([
            new ThriftField(PufferUpdateRspField.result, ThriftInteger.I32(kUpdateSuccess)),
            new ThriftField(PufferUpdateRspField.comm, comm),
            new ThriftField(PufferUpdateRspField.update_info, updateInfo),
            new ThriftField(PufferUpdateRspField.app_id, ThriftBinary.String(PufferAppId)),
        ]);
    }

    private ThriftStruct? ReqUpdateVersion(ThriftStruct arguments, EndPoint? remoteEndPoint)
    {
        if (arguments.FindStruct(ReqVersionUpdateField.comm_info) is not { } commInfo || commInfo.FindInteger(VersionCommInfoField.apk_version, ThriftType.I64) is not { } apkVersion)
        {
            logger.LogWarning("ReqUpdateVersion from {RemoteEndPoint} refused: no comm_info.apk_version", remoteEndPoint);
            return null;
        }

        var dolphinProductId = commInfo.FindInteger(VersionCommInfoField.product_id, ThriftType.I32) ?? 0;
        if (!appUpdatePackages.TryGetNewest(dolphinProductId, out var package))
        {
            logger.LogInformation("ReqUpdateVersion from {RemoteEndPoint} for product {ProductId} apk version {ApkVersion}: no archive, kUpdateTypeNot", remoteEndPoint, dolphinProductId, AppUpdatePackages.VersionName(apkVersion));
            return VersionUpdateReply(commInfo, PackageInfo(apkVersion, []), kUpdateTypeNot);
        }

        var updateType = apkVersion < package.Version ? kUpdateTypeOptional : kUpdateTypeNot;
        logger.LogInformation("ReqUpdateVersion from {RemoteEndPoint} for product {ProductId} apk version {ApkVersion}: newest app version {NewestAppVersion}, update type {UpdateType}", remoteEndPoint, dolphinProductId, AppUpdatePackages.VersionName(apkVersion), AppUpdatePackages.VersionName(package.Version), updateType);
        return VersionUpdateReply(commInfo, PackageInfo(package.Version, [
            new ThriftField(VersionPackageInfoField.name, ThriftBinary.String(package.FileName)),
            new ThriftField(VersionPackageInfoField.http_url, ThriftBinary.String(package.Url)),
            new ThriftField(VersionPackageInfoField.md5sum, ThriftBinary.String(package.Md5)),
        ]), updateType);
    }

    private static ThriftStruct PackageInfo(long version, IReadOnlyList<ThriftField> packageFile)
    {
        var fields = new List<ThriftField>
        {
            new(VersionPackageInfoField.version, ThriftInteger.I64(version)),
            new(VersionPackageInfoField.description, ThriftBinary.String(string.Empty)),
            new(VersionPackageInfoField.custom_str, ThriftBinary.String(string.Empty)),
            new(VersionPackageInfoField.attr, NormalVersionAttr()),
        };
        fields.AddRange(packageFile);
        return new ThriftStruct([.. fields.OrderBy(field => field.Id)]);
    }

    private static ThriftStruct VersionUpdateReply(ThriftStruct commInfo, ThriftStruct packageInfo, int updateType)
    {
        var updateInfo = new ThriftStruct([
            new ThriftField(VersionUpdateInfoField.package_info, packageInfo),
            new ThriftField(VersionUpdateInfoField.update_type, ThriftInteger.I32(updateType)),
        ]);
        return new ThriftStruct([
            new ThriftField(ResVersionUpdateField.result, ThriftInteger.I32(kUpdateSuccess)),
            new ThriftField(ResVersionUpdateField.comm_info, commInfo.Only(id => id is > 0 and <= VersionCommInfoField.gameid)),
            new ThriftField(ResVersionUpdateField.update_info, updateInfo),
            new ThriftField(ResVersionUpdateField.app_id, ThriftBinary.String(VersionUpdateAppId)),
            new ThriftField(ResVersionUpdateField.region_id, ThriftInteger.I32(VersionUpdateRegionId)),
            new ThriftField(ResVersionUpdateField.src_attr, NormalVersionAttr()),
        ]);
    }

    private ThriftStruct? Refuse(string name, string service, EndPoint? remoteEndPoint)
    {
        logger.LogWarning("Update request {Name} on service {Service} from {RemoteEndPoint} refused: unknown method", name, service, remoteEndPoint);
        return null;
    }

    private static ThriftStruct AddrInfo(int result, IReadOnlyList<ThriftValue> addresses) => new([
        new ThriftField(QueryAddrRspField.result, ThriftInteger.I32(result)),
        new ThriftField(QueryAddrRspField.message, ThriftBinary.String(string.Empty)),
        new ThriftField(QueryAddrRspField.addrs, new ThriftList(ThriftType.Struct, addresses)),
    ]);

    private static ThriftStruct NormalVersionAttr() => new([
        new ThriftField(VersionAttrField.audit, ThriftInteger.Byte(0)),
        new ThriftField(VersionAttrField.gray, ThriftInteger.Byte(0)),
        new ThriftField(VersionAttrField.normal, ThriftInteger.Byte(1)),
    ]);

    private static class QueryAddrReqField
    {
        public const short gameid = 1;
        public const short type = 2;
        public const short name = 3;
    }

    private static class QueryAddrRspField
    {
        public const short result = 1;
        public const short message = 2;
        public const short addrs = 3;
    }

    private static class AddrField
    {
        public const short ip = 1;
        public const short port = 2;
    }

    private static class PufferUpdateReqField
    {
        public const short comm = 1;
    }

    private static class UpdateCommInfoField
    {
        public const short product_id = 3;
        public const short dolphin_app_version = 8;
    }

    private static class PufferUpdateRspField
    {
        public const short result = 1;
        public const short comm = 2;
        public const short update_info = 3;
        public const short app_id = 4;
    }

    private static class UpdateInfoField
    {
        public const short url = 1;
        public const short md5 = 2;
        public const short custom_str = 3;
        public const short desc = 4;
        public const short version = 5;
        public const short url2 = 6;
    }

    private static class ReqVersionUpdateField
    {
        public const short comm_info = 1;
    }

    private static class VersionCommInfoField
    {
        public const short product_id = 2;
        public const short apk_version = 5;
        public const short gameid = 8;
    }

    private static class ResVersionUpdateField
    {
        public const short result = 1;
        public const short comm_info = 2;
        public const short update_info = 3;
        public const short app_id = 4;
        public const short region_id = 5;
        public const short src_attr = 7;
    }

    private static class VersionUpdateInfoField
    {
        public const short package_info = 1;
        public const short update_type = 2;
    }

    private static class VersionPackageInfoField
    {
        public const short version = 1;
        public const short name = 2;
        public const short description = 3;
        public const short http_url = 4;
        public const short md5sum = 5;
        public const short custom_str = 6;
        public const short attr = 7;
    }

    private static class VersionAttrField
    {
        public const short audit = 1;
        public const short gray = 2;
        public const short normal = 3;
    }
}
