using SpiritUnite.Packets.Messages;
using SpiritUnite.Servers.Http;

namespace SpiritUnite.Resource;

public sealed record DatabinStringPatch(string DatabinName, ulong? RowKey, int FieldNumber, string Value);

public static class ClientTablePatches
{
    public const string ActivityBaseDatabin = "databin_Activity_Base";
    public const string LanguagesChangeDatabin = "databin_Languages_Change";
    public const string GlobalStringDatabin = "databin_Global_String";
    public const string NoServerStopTime = "0";
    public const ulong GiftExchangeActivityID = 424;

    public static IReadOnlyList<DatabinStringPatch> For(Uri webOrigin)
    {
        var giftCodePage = PageUrl(webOrigin, GiftCodeEndpoints.PagePath);
        var aboutPage = PageUrl(webOrigin, AboutPage.Path);
        var galleryPage = PageUrl(webOrigin, GalleryEndpoints.PagePath);
        return
        [
            new(ActivityBaseDatabin, GiftExchangeActivityID, pbActivity_Base.SpJumpURLFieldNumber, giftCodePage),
            new(ActivityBaseDatabin, GiftExchangeActivityID, pbActivity_Base.NsJumpURLFieldNumber, giftCodePage),
            new(LanguagesChangeDatabin, null, pbLanguages_Change.AgreementUrlFieldNumber, aboutPage),
            new(LanguagesChangeDatabin, null, pbLanguages_Change.PrivacyUrlFieldNumber, aboutPage),
            new(LanguagesChangeDatabin, null, pbLanguages_Change.CodeConductUrlFieldNumber, aboutPage),
            new(GlobalStringDatabin, (ulong)PB_ENUM_RES_RESGLOBALSTR.PbResglobalstrOfflineLibraryStopServerTime, pbGlobal_String.ValueFieldNumber, NoServerStopTime),
            new(GlobalStringDatabin, (ulong)PB_ENUM_RES_RESGLOBALSTR.PbResgolbalstrUniteComicUrl, pbGlobal_String.ValueFieldNumber, galleryPage),
        ];
    }

    private static string PageUrl(Uri webOrigin, string path) => new Uri(webOrigin, path).AbsoluteUri;
}
