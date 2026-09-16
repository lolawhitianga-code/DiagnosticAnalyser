namespace DiagFileMonitor.Core.Help;

/// <summary>A piece of a topic ready to draw: a paragraph or a bullet.</summary>
public record HelpBlock(string Text, bool IsBullet);

/// <summary>
/// Turns a topic's body into blocks to draw.
/// <para>
/// The help file is hard wrapped at 100 characters so it reads well in an editor; a popup is not,
/// so wrapped lines are joined back up. A blank line ends a paragraph and a bullet stands alone.
/// This is the only Markdown handling there is - the file uses paragraphs, bullets and bold, and
/// pulling in a library for three features would be a poor trade.
/// </para>
/// </summary>
public static class HelpText
{
    public static IReadOnlyList<HelpBlock> Blocks(string? body)
    {
        var blocks = new List<HelpBlock>();
        if (string.IsNullOrWhiteSpace(body)) return blocks;

        var current = new List<string>();

        void Flush()
        {
            if (current.Count == 0) return;

            var text = string.Join(' ', current);
            var bullet = text.StartsWith("- ", StringComparison.Ordinal);

            blocks.Add(new HelpBlock(bullet ? text[2..] : text, bullet));
            current.Clear();
        }

        foreach (var raw in body.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Trim();

            if (line.Length == 0)
            {
                Flush();
                continue;
            }

            // A bullet starts its own block, and the line before it ends there.
            if (line.StartsWith("- ", StringComparison.Ordinal)) Flush();

            current.Add(line);

            // A bullet that wraps keeps going, so only flush on the next bullet or blank line.
        }

        Flush();
        return blocks;
    }

    /// <summary>
    /// Splits a line on <c>**bold**</c> markers. The pieces alternate plain, bold, plain - so an
    /// odd index is bold.
    /// </summary>
    public static IReadOnlyList<string> BoldRuns(string text) => text.Split("**");
}
