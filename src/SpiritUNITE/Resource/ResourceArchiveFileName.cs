using System.Globalization;
using System.Text.RegularExpressions;

namespace SpiritUNITE.Resource;

public static partial class ResourceArchiveFileName
{
    public const string Extension = ".ifs";
    private const int VersionPartBits = 16;

    public static bool TryParse(string fileName, out int productId, out long version)
    {
        productId = 0;
        version = 0;
        var match = OfficialPattern().Match(fileName);
        if (!match.Success || !int.TryParse(match.Groups["product"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out productId))
        {
            return false;
        }

        ulong packed = 0;
        foreach (Capture part in match.Groups["part"].Captures)
        {
            if (!ushort.TryParse(part.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
            {
                return false;
            }

            packed = (packed << VersionPartBits) | value;
        }

        version = unchecked((long)packed);
        return true;
    }

    public static bool TryParseProductFolder(string folderName, out int productId) =>
        int.TryParse(folderName, NumberStyles.None, CultureInfo.InvariantCulture, out productId) && productId > 0;

    [GeneratedRegex(@"^\d+_(?<product>\d+)_(?<part>\d+)\.(?<part>\d+)\.(?<part>\d+)\.(?<part>\d+)_.*\.ifs$", RegexOptions.CultureInvariant)]
    private static partial Regex OfficialPattern();
}
