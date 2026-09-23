using System.Globalization;
using System.Text.RegularExpressions;

namespace DiagFileMonitor.Core.Production;

/// <summary>
/// Reads a ProdLogV2 weekly production log.
/// <para>
/// Checked against ten real weeks from M21737 (213,665 lines). The one rule that matters here is
/// consecutive de-duplication: the controller writes some events twice in a row, and on that
/// sample it was 49.6% of MemberAssembled lines and 29.9% of MachineStopped. Every other event
/// type had none at all, so this is not a blanket "everything is doubled" - it is suppressing a
/// line that is byte-identical to the one before it, which is safe either way.
/// </para>
/// </summary>
public static class ProdLogParser
{
    /// <summary>File names look like ProdLogV22026W37.log - ProdLogV2, year, W, ISO week.</summary>
    private static readonly Regex FileNamePattern =
        new(@"ProdLogV2(?<year>\d{4})W(?<week>\d{1,2})\.log$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// The same naming without the V2 - ProdLog2020W07.log. Older exports carry these alongside
    /// the V2 files. They are recognised so they can be reported rather than silently lumped in
    /// with the ShiftLogs, but they are not read: nobody has supplied one with data in it, and
    /// guessing at a file format is how this project has gone wrong before.
    /// </summary>
    private static readonly Regex OlderNamePattern =
        new(@"ProdLog(?<year>\d{4})W(?<week>\d{1,2})\.log$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Dictionary<string, ProdLogEventKind> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["PanelStarted"] = ProdLogEventKind.PanelStarted,
        ["PanelAssembled"] = ProdLogEventKind.PanelAssembled,
        ["PanelStopped"] = ProdLogEventKind.PanelStopped,
        ["MemberAssembled"] = ProdLogEventKind.MemberAssembled,
        ["MemberCut"] = ProdLogEventKind.MemberCut,
        ["MachineStarted"] = ProdLogEventKind.MachineStarted,
        ["MachineStopped"] = ProdLogEventKind.MachineStopped,
        ["MachineIdleStart"] = ProdLogEventKind.MachineIdleStart,
        ["MachineIdleStop"] = ProdLogEventKind.MachineIdleStop,
        ["UserLogin"] = ProdLogEventKind.UserLogin,
        ["UserLogout"] = ProdLogEventKind.UserLogout,
        ["MembersSubAssembled"] = ProdLogEventKind.MembersSubAssembled
    };

    public static ProdLogParseResult ParseFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return new ProdLogParseResult { FileName = Path.GetFileName(path ?? string.Empty) };

        return Parse(File.ReadAllText(path), Path.GetFileName(path));
    }

    public static ProdLogParseResult Parse(string text, string fileName = "")
    {
        // Some exports carry stray NUL padding. Strip it before anything tries to read a field.
        var hadNulls = text.Contains('\0');
        if (hadNulls) text = text.Replace("\0", string.Empty);

        var events = new List<ProdLogEvent>();
        var unknown = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        int linesRead = 0, duplicates = 0, malformed = 0, lineNumber = 0;

        string? previous = null;

        foreach (var rawLine in text.Split('\n'))
        {
            lineNumber++;
            var line = rawLine.Trim('\r', ' ', '\t');
            if (line.Length == 0) continue;

            linesRead++;

            if (line == previous)
            {
                duplicates++;
                continue;
            }

            previous = line;

            var fields = line.Split(',');
            if (fields.Length < 2)
            {
                malformed++;
                continue;
            }

            var name = fields[0].Trim();
            if (!DateTime.TryParseExact(fields[1].Trim(), "yyyyMMdd HH:mm:ss",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var timestamp))
            {
                malformed++;
                continue;
            }

            if (!Names.TryGetValue(name, out var kind))
            {
                kind = ProdLogEventKind.Unknown;
                unknown[name] = unknown.GetValueOrDefault(name) + 1;
            }

            events.Add(new ProdLogEvent
            {
                Kind = kind,
                Name = name,
                Timestamp = timestamp,
                Fields = fields.Skip(2).Select(f => f.Trim()).ToArray(),
                SourceFile = fileName,
                LineNumber = lineNumber
            });
        }

        return new ProdLogParseResult
        {
            Events = events,
            FileName = fileName,
            LinesRead = linesRead,
            ConsecutiveDuplicates = duplicates,
            MalformedLines = malformed,
            UnknownEventNames = unknown,
            HadNullPadding = hadNulls
        };
    }

    /// <summary>
    /// The year and ISO week a file name claims. Used to skip a file already stored, and to spot
    /// the duplicate copy of a recent week that some exports carry in a second folder.
    /// </summary>
    public static (int Year, int Week)? WeekFromFileName(string fileName)
    {
        var match = FileNamePattern.Match(fileName ?? string.Empty);
        if (!match.Success) return null;

        return (int.Parse(match.Groups["year"].Value), int.Parse(match.Groups["week"].Value));
    }

    public static bool LooksLikeProdLog(string fileName) => WeekFromFileName(fileName) is not null;

    /// <summary>A production log named the older way, without the V2. Recognised, not read.</summary>
    public static bool LooksLikeOlderProdLog(string fileName) =>
        !LooksLikeProdLog(fileName ?? string.Empty)
        && OlderNamePattern.IsMatch(fileName ?? string.Empty);

    /// <summary>
    /// Whether a file holds production events, judged on what is in it rather than what it is
    /// called.
    /// <para>
    /// Needed because a support bundle carries its own production data as
    /// <c>Reports\LatestReport.txt</c> - same format, nothing in the name to say so. Only the
    /// first few lines are read, so this is cheap enough to run over a folder.
    /// </para>
    /// </summary>
    public static bool LooksLikeProductionContent(string path)
    {
        try
        {
            using var reader = new StreamReader(path);

            for (var read = 0; read < 20; read++)
            {
                var line = reader.ReadLine();
                if (line is null) break;

                line = line.Replace("\0", string.Empty).Trim();
                if (line.Length == 0) continue;

                var fields = line.Split(',');
                if (fields.Length < 2) continue;

                if (Names.ContainsKey(fields[0].Trim())
                    && DateTime.TryParseExact(fields[1].Trim(), "yyyyMMdd HH:mm:ss",
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                {
                    return true;
                }
            }

            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
