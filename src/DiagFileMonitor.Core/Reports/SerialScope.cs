namespace DiagFileMonitor.Core.Reports;

/// <summary>
/// Turns a set of machines into the scope of a report, and back into the text a person types.
/// <para>
/// The rule that matters: a machine appears once. Several bundles from one machine is one machine,
/// and a report listing the same serial three times would count it three times in every chart.
/// </para>
/// </summary>
public static class SerialScope
{
    private static readonly char[] Separators = { ',', ';', '\n', '\r', '\t' };

    /// <summary>The distinct machines in a set of rows, in order, ignoring blanks.</summary>
    public static IReadOnlyList<string> FromSerials(IEnumerable<string?> serials) =>
        serials
            .Select(s => (s ?? string.Empty).Trim())
            .Where(s => s.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>What a person typed into a scope box, as a list.</summary>
    public static IReadOnlyList<string> Parse(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? Array.Empty<string>()
            : FromSerials(text.Split(Separators, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>The same list as text, ready to put in the box.</summary>
    public static string Describe(IEnumerable<string?> serials) => string.Join(", ", FromSerials(serials));
}
