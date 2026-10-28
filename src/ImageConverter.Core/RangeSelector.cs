using System.Globalization;
using System.Text.RegularExpressions;

namespace ImageConverter.Core;

/// <summary>
/// Parses selection text such as <c>5915-5920, 5925</c> (numbers in file names),
/// <c>#3-#10</c> (1-based list positions) and <c>IMG_*.jpg</c> (wildcards), separated by commas or semicolons.
/// Same syntax as Image Renamer.
/// </summary>
public static partial class RangeSelector
{
    [GeneratedRegex(@"^#(\d+)(?:\s*-\s*#?(\d+))?$")]
    private static partial Regex PositionRegex();

    [GeneratedRegex(@"^(\d+)(?:\s*-\s*(\d+))?$")]
    private static partial Regex NumberRangeRegex();

    [GeneratedRegex(@"\d+")]
    private static partial Regex DigitsRegex();

    public static List<ConvertItem> Select(string text, IReadOnlyList<ConvertItem> items)
    {
        var selected = new HashSet<ConvertItem>();
        foreach (var part in Split(text))
            foreach (var item in Match(part, items))
                selected.Add(item);
        // Preserve list order.
        return items.Where(selected.Contains).ToList();
    }

    private static IEnumerable<string> Split(string text) =>
        (text ?? "").Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static IEnumerable<ConvertItem> Match(string part, IReadOnlyList<ConvertItem> items)
    {
        var m = PositionRegex().Match(part);
        if (m.Success)
        {
            var (a, b) = Bounds(m);
            for (long i = Math.Max(a, 1); i <= Math.Min(b, items.Count); i++)
                yield return items[(int)i - 1];
            yield break;
        }

        m = NumberRangeRegex().Match(part);
        if (m.Success)
        {
            var (a, b) = Bounds(m);
            foreach (var item in items)
            {
                // Any number in the name may match, so "DSC_5915" and "2024_05_01_5915" both work.
                foreach (Match d in DigitsRegex().Matches(item.Stem))
                {
                    if (long.TryParse(d.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var v) && v >= a && v <= b)
                    {
                        yield return item;
                        break;
                    }
                }
            }
            yield break;
        }

        // Wildcard against the file name; without an extension in the pattern, against the stem.
        var re = new Regex("^" + Regex.Escape(part).Replace(@"\*", ".*").Replace(@"\?", ".") + "$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        foreach (var item in items)
            if (re.IsMatch(item.FileName) || re.IsMatch(item.Stem))
                yield return item;
    }

    private static (long A, long B) Bounds(Match m)
    {
        long a = long.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        long b = m.Groups[2].Success ? long.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) : a;
        return a <= b ? (a, b) : (b, a);
    }
}
