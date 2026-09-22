using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using DiagFileMonitor.Core.SpidaLogs;

namespace DiagFileMonitor.Core.Knowledge;

/// <summary>One tag read out of a PLC export.</summary>
/// <param name="Name">The tag name, as the PLC has it.</param>
/// <param name="Kind">Output where the name carries the IO- prefix these machines use, else input.</param>
/// <param name="Point">The module.bit it is assigned to, where the export says. Often blank.</param>
/// <param name="Comment">The engineer's description, where there is one. Worth keeping.</param>
public record ImportedTag(string Name, SignalKind Kind, string Point, string Comment)
{
    /// <summary>The name with the IO- prefix off, which is what groups the two sides together.</summary>
    public string BareName =>
        Name.StartsWith("IO-", StringComparison.OrdinalIgnoreCase) ? Name[3..] : Name;
}

public record TagImportResult(
    IReadOnlyList<ImportedTag> Tags,
    string Format,
    int RowsRead,
    IReadOnlyList<string> Notes)
{
    public int Outputs => Tags.Count(t => t.Kind == SignalKind.Output);
    public int Inputs => Tags.Count(t => t.Kind == SignalKind.Input);
}

/// <summary>
/// Reads a PLC tag export and turns it into the named points a model has.
/// <para>
/// This is the cheap way to seed a model. A log only shows what moved, so the last few points of
/// a Raked Wall Extruder V3 took a full shift to appear and neither of the first two machines
/// had all of them. A tag export has the lot on the first try, including the ones that only
/// speak when something is wrong - which are the ones worth having.
/// </para>
/// <para>
/// It is deliberately tolerant about layout. Rockwell and Sysmac both export CSV with their own
/// column names and a preamble, and neither is worth hand-editing before sending. It looks for a
/// row that names a column something like "name" and works from there.
/// </para>
/// </summary>
public static class PlcTagImport
{
    /// <summary>Column headings that mean "the tag's name", across both vendors' exports.</summary>
    private static readonly string[] NameColumns =
        { "name", "tagname", "tag name", "variable", "variable name", "symbol", "symbolname" };

    /// <summary>Where an export says which physical point a tag sits on.</summary>
    private static readonly string[] PointColumns =
        { "at", "alias", "aliasfor", "alias for", "specifier", "address", "assignment", "iolocation" };

    private static readonly string[] CommentColumns =
        { "comment", "description", "desc" };

    /// <summary>
    /// Rockwell writes an alias as Local:2:O.Data.3 - slot 2, output, bit 3 - so the two numbers
    /// that matter are not next to each other.
    /// </summary>
    private static readonly Regex RockwellAlias = new(
        @"^\w*?:?(?<module>\d+):[IO]\w*\.(?:Data\.)?(?<bit>\d+)\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>Everything else ends with the module and the bit together.</summary>
    private static readonly Regex PointPattern = new(
        @"(?<module>\d+)\s*[.:]\s*(?<bit>\d+)\s*$", RegexOptions.Compiled);

    public static TagImportResult Read(string path)
    {
        var text = File.ReadAllText(path, Encoding.UTF8);

        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".l5x" or ".xml" => ReadL5X(text),
            _ => ReadDelimited(text)
        };
    }

    /// <summary>
    /// Studio 5000 / RSLogix native export. Tags are elements, and the useful ones are usually
    /// aliases onto a module point rather than base tags.
    /// </summary>
    public static TagImportResult ReadL5X(string xml)
    {
        var notes = new List<string>();
        var tags = new List<ImportedTag>();
        var rows = 0;

        XDocument document;
        try
        {
            document = XDocument.Parse(xml);
        }
        catch (System.Xml.XmlException e)
        {
            return new TagImportResult(tags, "L5X", 0, new[] { $"Not readable as XML: {e.Message}" });
        }

        foreach (var tag in document.Descendants().Where(e => e.Name.LocalName == "Tag"))
        {
            rows++;
            var name = Attribute(tag, "Name");
            if (name.Length == 0) continue;

            var alias = Attribute(tag, "AliasFor");
            var comment = tag.Descendants()
                .FirstOrDefault(e => e.Name.LocalName is "Description" or "Comment")?.Value.Trim() ?? string.Empty;

            tags.Add(Build(name, alias, comment));
        }

        if (tags.Count == 0) notes.Add("No <Tag> elements found. Is this a full controller export?");

        return new TagImportResult(Tidy(tags, notes), "L5X", rows, notes);
    }

    /// <summary>
    /// A CSV or tab-separated export from either vendor. Both put a preamble above the real
    /// header, so the header is found rather than assumed to be the first line.
    /// </summary>
    public static TagImportResult ReadDelimited(string text)
    {
        var notes = new List<string>();
        var tags = new List<ImportedTag>();
        var lines = text.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Trim().Length > 0).ToList();

        var headerAt = -1;
        char separator = ',';
        string[] header = Array.Empty<string>();

        for (var i = 0; i < lines.Count && headerAt < 0; i++)
        {
            foreach (var candidate in new[] { ',', '\t', ';' })
            {
                // A one-column export is just a list of names, which is still the deliverable.
                var cells = Split(lines[i], candidate).Select(Normalise).ToArray();
                if (cells.Any(c => NameColumns.Contains(c)))
                {
                    headerAt = i;
                    separator = candidate;
                    header = cells;
                    break;
                }
            }
        }

        if (headerAt < 0)
            return new TagImportResult(tags, "delimited", lines.Count,
                new[] { "No header row naming a tag/variable/symbol column. Export with headings included." });

        var nameAt = IndexOfAny(header, NameColumns);
        var pointAt = IndexOfAny(header, PointColumns);
        var commentAt = IndexOfAny(header, CommentColumns);

        var rows = 0;
        for (var i = headerAt + 1; i < lines.Count; i++)
        {
            var cells = Split(lines[i], separator);
            if (cells.Length <= nameAt) continue;

            rows++;
            var name = cells[nameAt].Trim().Trim('"');
            if (name.Length == 0) continue;

            tags.Add(Build(
                name,
                pointAt >= 0 && pointAt < cells.Length ? cells[pointAt] : string.Empty,
                commentAt >= 0 && commentAt < cells.Length ? cells[commentAt].Trim().Trim('"') : string.Empty));
        }

        if (pointAt < 0)
            notes.Add("No column giving the physical point, so the names come through without numbers. "
                      + "That is fine - the numbers are read off each machine's own log.");

        return new TagImportResult(Tidy(tags, notes), $"delimited ('{separator}')", rows, notes);
    }

    /// <summary>
    /// Outputs carry the IO- prefix on these machines, on both control systems - the logs write
    /// IO-PlateClamp for an output and PlateClampUp for an input. Anything else is read as an
    /// input, which is the safer way round: an input listed by mistake reads as a sensor that
    /// never moved, and an output listed by mistake would claim the machine drives something it
    /// does not.
    /// </summary>
    private static ImportedTag Build(string name, string point, string comment)
    {
        var kind = name.StartsWith("IO-", StringComparison.OrdinalIgnoreCase)
            ? SignalKind.Output
            : SignalKind.Input;

        // Vendors zero-pad the module differently - Out_Bit_00.13 and Out_Bit_0.13 are the same
        // point - so the number is normalised rather than taken as written.
        var at = (point ?? string.Empty).Trim();
        var match = RockwellAlias.Match(at);
        if (!match.Success) match = PointPattern.Match(at);

        var where = match.Success
            ? $"{int.Parse(match.Groups["module"].Value)}.{int.Parse(match.Groups["bit"].Value)}"
            : string.Empty;

        return new ImportedTag(name.Trim(), kind, where, comment.Trim());
    }

    /// <summary>Drops the obvious non-I/O noise an export carries, and says how much it dropped.</summary>
    private static IReadOnlyList<ImportedTag> Tidy(List<ImportedTag> tags, List<string> notes)
    {
        var before = tags.Count;

        var kept = tags
            .Where(t => t.Name.Length > 1)
            .Where(t => !t.Name.StartsWith("_", StringComparison.Ordinal))
            .GroupBy(t => (t.Kind, MachineIoMap.Flatten(t.Name)))
            .Select(g => g.OrderByDescending(t => t.Point.Length).ThenByDescending(t => t.Comment.Length).First())
            .OrderBy(t => t.Kind)
            .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (before > kept.Count) notes.Add($"{before - kept.Count} row(s) dropped as duplicates or scratch tags.");

        return kept;
    }

    private static string Attribute(XElement element, string name) =>
        element.Attributes().FirstOrDefault(a => a.Name.LocalName == name)?.Value.Trim() ?? string.Empty;

    private static string Normalise(string cell) =>
        new(cell.Trim().Trim('"').Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static int IndexOfAny(string[] header, string[] wanted)
    {
        var flat = wanted.Select(w => new string(w.Where(char.IsLetterOrDigit).ToArray())).ToArray();
        for (var i = 0; i < header.Length; i++)
            if (flat.Contains(header[i]))
                return i;
        return -1;
    }

    /// <summary>Splits on the separator, respecting double quotes so a comma in a comment survives.</summary>
    private static string[] Split(string line, char separator)
    {
        var cells = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;

        foreach (var c in line)
        {
            if (c == '"') quoted = !quoted;
            else if (c == separator && !quoted) { cells.Add(cell.ToString()); cell.Clear(); }
            else cell.Append(c);
        }

        cells.Add(cell.ToString());
        return cells.ToArray();
    }
}
