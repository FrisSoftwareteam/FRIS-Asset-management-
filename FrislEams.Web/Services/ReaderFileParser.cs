using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace FrislEams.Web.Services;

/// <summary>
/// Extracts RFID/tag codes from whatever file a handheld reader app exports: plain text, CSV,
/// Excel .xlsx (a zip of XML) or legacy .xls (binary, strings stored as Latin-1 or UTF-16).
/// A code is a run of 8 to 32 hex characters (EPCs, TIDs, FAMAS 12-digit codes) or a dashed
/// FAMAS tag code such as 033-004-000001. Header words and RSSI/count numbers are ignored.
/// </summary>
public static partial class ReaderFileParser
{
    public static List<string> ExtractCodes(byte[] bytes)
    {
        var texts = new List<string>();
        if (bytes.Length > 4 && bytes[0] == 'P' && bytes[1] == 'K')
        {
            texts.AddRange(ReadXlsx(bytes));
        }
        else
        {
            texts.Add(Encoding.UTF8.GetString(bytes));
            if (LooksBinary(bytes))
            {
                texts.Add(Encoding.Latin1.GetString(bytes));
                texts.Add(Encoding.Unicode.GetString(bytes));
                if (bytes.Length > 1) texts.Add(Encoding.Unicode.GetString(bytes, 1, bytes.Length - 1));
            }
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var codes = new List<string>();
        foreach (var text in texts)
        {
            foreach (Match m in CodePattern().Matches(text))
            {
                var code = m.Value.ToUpperInvariant();
                // Skip plain small numbers (RSSI, counts) and things that are only a header word.
                if (!code.Contains('-') && code.All(char.IsDigit) && code.Length < 9) continue;
                // Excel stores 004014000041 as the number 4014000041; put the zeros back.
                if (!code.Contains('-') && code.All(char.IsDigit) && code.Length < 12) code = code.PadLeft(12, '0');
                if (seen.Add(code)) codes.Add(code);
            }
        }

        return codes;
    }

    private static IEnumerable<string> ReadXlsx(byte[] bytes)
    {
        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        foreach (var entry in zip.Entries)
        {
            if (!entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) continue;
            if (!(entry.FullName.Contains("sharedStrings") || entry.FullName.Contains("worksheets/"))) continue;
            using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
            var xml = reader.ReadToEnd();
            // Keep only cell text/values so XML attributes (sizes, style ids) are not mistaken for codes.
            var values = XmlValue().Matches(xml).Select(m => m.Groups[1].Value);
            yield return string.Join("\n", values);
        }
    }

    private static bool LooksBinary(byte[] bytes)
    {
        var sample = Math.Min(bytes.Length, 4096);
        var zeros = 0;
        for (var i = 0; i < sample; i++) if (bytes[i] == 0) zeros++;
        return zeros > sample / 50;
    }

    [GeneratedRegex(@"(?<![0-9A-Za-z])(?:[0-9]{3}-[0-9]{3}-[0-9]{6}|[0-9A-Fa-f]{8,32})(?![0-9A-Za-z])")]
    private static partial Regex CodePattern();

    [GeneratedRegex(@"<(?:t|v)(?:\s[^>]*)?>([^<]*)</(?:t|v)>")]
    private static partial Regex XmlValue();
}
