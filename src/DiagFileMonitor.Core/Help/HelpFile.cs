using System.Text;
using System.Text.RegularExpressions;

namespace DiagFileMonitor.Core.Help;

/// <summary>
/// Reads HELP.md into topics a control can look up.
/// <para>
/// The format is deliberately dull: an anchor comment carrying an id, a <c>###</c> heading, then
/// the body until the next anchor. No Markdown library, no front matter, no build step - the file
/// has to stay something a support person can edit in Notepad without breaking it.
/// </para>
/// </summary>
public class HelpFile
{
    private static readonly Regex AnchorPattern =
        new(@"<!--\s*help:(?<id>[a-z0-9.]+)\s*-->", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly Dictionary<string, HelpTopic> _topics;

    private HelpFile(Dictionary<string, HelpTopic> topics) => _topics = topics;

    public IReadOnlyCollection<HelpTopic> Topics => _topics.Values;

    public int Count => _topics.Count;

    /// <summary>The topic with this id, or null. An unknown id is never an error - the caller
    /// shows nothing rather than an apology.</summary>
    public HelpTopic? Find(string? id) =>
        string.IsNullOrWhiteSpace(id) ? null
        : _topics.TryGetValue(id.Trim(), out var topic) ? topic : null;

    public bool Has(string? id) => Find(id) is not null;

    /// <summary>An empty help file, for when none could be loaded.</summary>
    public static HelpFile Empty { get; } = new(new Dictionary<string, HelpTopic>(StringComparer.OrdinalIgnoreCase));

    public static HelpFile ParseFile(string path)
    {
        try
        {
            return File.Exists(path) ? Parse(File.ReadAllText(path)) : Empty;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Empty;
        }
    }

    public static HelpFile Parse(string text)
    {
        var topics = new Dictionary<string, HelpTopic>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(text)) return new HelpFile(topics);

        var matches = AnchorPattern.Matches(text);

        for (var i = 0; i < matches.Count; i++)
        {
            var id = matches[i].Groups["id"].Value.ToLowerInvariant();

            var from = matches[i].Index + matches[i].Length;
            var to = i + 1 < matches.Count ? matches[i + 1].Index : text.Length;

            var (title, body) = SplitHeading(text[from..to]);
            if (title.Length == 0 && body.Length == 0) continue;

            // First one wins. A repeated id is a mistake in the file, and quietly replacing the
            // real topic with a later stray would be worse than ignoring the stray.
            topics.TryAdd(id, new HelpTopic { Id = id, Title = title, Body = body });
        }

        return new HelpFile(topics);
    }

    /// <summary>
    /// Pulls the heading off the front and tidies what is left. A horizontal rule ends a topic, so
    /// the section break before the next window does not get swept into the last topic's body.
    /// </summary>
    private static (string Title, string Body) SplitHeading(string block)
    {
        var lines = block.Replace("\r\n", "\n").Split('\n');

        var title = string.Empty;
        var body = new StringBuilder();
        var started = false;

        foreach (var line in lines)
        {
            var trimmed = line.TrimEnd();

            if (!started)
            {
                if (trimmed.Length == 0) continue;

                if (trimmed.StartsWith('#'))
                {
                    title = trimmed.TrimStart('#').Trim();
                    started = true;
                    continue;
                }

                started = true;
            }

            if (trimmed == "---") break;

            body.AppendLine(trimmed);
        }

        return (title, body.ToString().Trim());
    }
}
