using System.Globalization;
using System.Text;
using System.Text.Json;

namespace SpiritUnite.Game.BattleVoice;

public static class GVoiceReplies
{
    public const uint ReplySucc = 0;
    public const string ReplyOk = "ok";

    private const string ProtocolVersion = "0.1.1";
    private const string SmallRoomInfoType = "room_info";
    private const string ConfigInfoType = "config_info";
    private const string GCloudBusinessPrefix = "gcloud.";
    private const string WeixinAppId = "wxk158ztg8lli234jgpu";
    private const string WeixinSttUrl = "http://127.0.0.1/cgi-bin/wxvoicereco";
    private const int BitRate = 16000;
    private const int Fec = 0;
    private const string OfflineVoiceHost = "sg-offline.voice.gcloudcs.com";
    private const string SpeechTranslateUrl = "https://sg.api.translator.voice.gcloudsdk.com:443";
    private const string VoiceReportUrl = "https://report_complaint_pubgm.iegcom.com:443";
    private const int Svryys = 1;
    private const string AccessScheme = "udp://";

    public static string ReplyBusinessId(string requestBusinessId) => GCloudBusinessPrefix + requestBusinessId;

    public static string AccessUrl(string host, int port) => $"{AccessScheme}{host}:{port.ToString(CultureInfo.InvariantCulture)}";

    public static string RoomInfoJson(GVoiceRoom room, GVoiceMember member, string businessId, string accessUrl) =>
        Write(json =>
        {
            json.WriteString("version", ProtocolVersion);
            json.WriteString("type", SmallRoomInfoType);
            json.WriteStartObject("info");
            json.WriteString("room_id", room.RoomId.ToString(CultureInfo.InvariantCulture));
            json.WriteString("room_key", room.RoomKey.ToString(CultureInfo.InvariantCulture));
            json.WriteString("member_id", member.MemberId.ToString(CultureInfo.InvariantCulture));
            json.WriteStartArray("access_ip");
            json.WriteStringValue(accessUrl);
            json.WriteEndArray();
            json.WriteString("business_id", businessId);
            json.WriteString("open_id", member.OpenId);
            json.WriteEndObject();
        });

    public static string ConfigInfoJson(string requestBusinessId) =>
        Write(json =>
        {
            json.WriteString("version", ProtocolVersion);
            json.WriteString("type", ConfigInfoType);
            json.WriteString("appid", ReplyBusinessId(requestBusinessId));
            json.WriteString("weixin_appid", WeixinAppId);
            json.WriteString("weixin_stt_url", WeixinSttUrl);
            json.WriteNumber("bit_rate", BitRate);
            json.WriteNumber("fec", Fec);
            json.WriteString("gvofs", OfflineVoiceHost);
            json.WriteString("speech_translate_url", SpeechTranslateUrl);
            json.WriteString("voice_report_url", VoiceReportUrl);
            json.WriteNumber("svryys", Svryys);
        });

    private static string Write(Action<Utf8JsonWriter> writeMembers)
    {
        using var stream = new MemoryStream();
        using (var json = new Utf8JsonWriter(stream))
        {
            json.WriteStartObject();
            writeMembers(json);
            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
