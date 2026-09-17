using System.Text;
using System.Text.RegularExpressions;

namespace DiagFileMonitor.Core.Fleet;

/// <summary>
/// Reads the timber a site actually runs, out of <c>StockList.xml</c>.
/// <para>
/// This file has been sitting in every bundle since the beginning and nothing has ever opened it.
/// It is the closest thing to a statement of what a customer builds: the member sizes they stock,
/// the lengths they buy, and how high they stack. A site running 45x290 is doing heavy floor and
/// rafter work and needs different machine capacity from one running 35x90 all day.
/// </para>
/// <para>
/// It is written UTF-16 with a byte order mark, which is why a naive read of it comes back as
/// gibberish with spaces between every letter.
/// </para>
/// </summary>
public static class TimberProfileReader
{
    private static readonly Regex Item = new(@"<StockItem>(?<body>.*?)</StockItem>",
        RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex Lengths = new(@"<StockLengths>.*?</StockLengths>",
        RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex InUseLength = new(
        @"<StockData>\s*<InUse>true</InUse>\s*<Length>(?<len>\d+)</Length>",
        RegexOptions.Singleline | RegexOptions.Compiled);

    public static TimberProfile Read(string path)
    {
        if (!File.Exists(path)) return TimberProfile.Unknown;

        try
        {
            // Detects the UTF-16 mark and falls back to UTF-8 for a file written the other way.
            using var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return Parse(reader.ReadToEnd());
        }
        catch (IOException)
        {
            return TimberProfile.Unknown;
        }
    }

    public static TimberProfile Parse(string xml)
    {
        var sizes = new List<string>();
        var lengths = new List<int>();

        foreach (Match item in Item.Matches(xml))
        {
            var body = item.Groups["body"].Value;

            // Each stock length carries its own <InUse> as well, and those come first in the file.
            // Reading the nested one as the item's own marks every size as in use and overstates
            // what the customer builds - which is exactly the kind of quiet wrong answer that ends
            // up in a proposal.
            var own = Lengths.Replace(body, string.Empty);

            if (!Tag(own, "InUse").Equals("true", StringComparison.OrdinalIgnoreCase)) continue;

            var height = Tag(own, "Height");
            var width = Tag(own, "Width");

            if (height.Length > 0 && width.Length > 0) sizes.Add($"{height}x{width}");

            foreach (Match length in InUseLength.Matches(body))
                if (int.TryParse(length.Groups["len"].Value, out var value)) lengths.Add(value);
        }

        return new TimberProfile(
            sizes.Distinct().ToList(),
            lengths.Distinct().OrderBy(l => l).ToList());
    }

    /// <summary>The first direct child with that name - deliberately not the nested ones.</summary>
    private static string Tag(string body, string name)
    {
        var match = Regex.Match(body, $@"<{name}>(?<v>[^<]*)</{name}>");
        return match.Success ? match.Groups["v"].Value.Trim() : string.Empty;
    }
}
