using System.Globalization;
using System.Text;
using DiagFileMonitor.Core.Production;

namespace DiagFileMonitor.Core.Reports;

/// <summary>
/// The figures an interactive production report needs, as JSON to embed in the page.
/// <para>
/// Per-day totals are worked out here rather than in the browser, so availability is calculated
/// once, in C#, where it is tested. The page only ever adds days up into weeks and months. The
/// panel rows are carried too, but only for the hour view and the list of panels that were not
/// built - nothing is recomputed from them.
/// </para>
/// </summary>
public static class ProductionPayload
{
    /// <summary>Outcome codes, kept short because they repeat thousands of times.</summary>
    private static int Code(PanelOutcome outcome) => outcome switch
    {
        PanelOutcome.Completed => 0,
        PanelOutcome.SteppedPast => 1,
        PanelOutcome.StoppedByOperator => 2,
        PanelOutcome.RanButNailedNothing => 3,
        PanelOutcome.AbandonedPartWay => 4,
        _ => 5
    };

    public static string Build(ProductionSummary summary, IReadOnlyList<PanelRecord> panels,
        string machineName, DateTime preparedUtc)
    {
        var json = new StringBuilder();
        var first = summary.From ?? DateOnly.FromDateTime(preparedUtc);

        json.Append('{');
        json.Append($"\"serial\":{Text(summary.SerialNumber)},");
        json.Append($"\"name\":{Text(machineName)},");
        json.Append($"\"site\":{Text(summary.Site)},");
        json.Append($"\"prepared\":{Text(preparedUtc.ToString("d MMM yyyy", CultureInfo.InvariantCulture))},");
        json.Append($"\"day0\":{Text(first.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))},");

        json.Append("\"shift\":{");
        json.Append($"\"ignored\":{(summary.Shift.Ignored ? "true" : "false")},");
        json.Append($"\"name\":{Text(summary.Shift.Name)},");
        json.Append($"\"stopMin\":{N(summary.Shift.UnplannedStopMinutes)},");
        json.Append($"\"breaks\":[{string.Join(",", summary.Shift.Breaks.Select(b =>
            $"{{\"n\":{Text(b.Name)},\"f\":{Text($"{b.From:HH\\:mm}")},\"t\":{Text($"{b.To:HH\\:mm}")},\"d\":{(b.FoundInDataOnly ? "true" : "false")}}}"))}],");
        json.Append($"\"start\":{Text($"{summary.Shift.ShiftStart:HH\\:mm}")},");
        json.Append($"\"end\":{Text($"{summary.Shift.ShiftEnd:HH\\:mm}")}");
        json.Append("},");

        // One row per calendar day, including the empty ones - a day at zero while the site worked
        // is the most useful thing here and must not be dropped.
        json.Append("\"days\":[");
        json.Append(string.Join(",", summary.Days.Select(d => "["
            + $"{d.Day.DayNumber - first.DayNumber},"
            + $"{d.PanelsCompleted},{N(d.Cube)},{N(d.Lineal)},"
            + $"{d.SteppedPast},{d.Faults},"
            + $"{N(d.PlannedMinutes)},{N(d.RunMinutes)},{N(d.UnplannedStopMinutes)},"
            + $"{N(d.StartupMinutes)},{N(d.TailMinutes)},{d.UnplannedStops}]")));
        json.Append("],");

        // [day, minute of day, outcome, cube, lineal, build minutes, name, why]
        json.Append("\"panels\":[");
        json.Append(string.Join(",", panels
            .Where(p => p.Outcome != PanelOutcome.Superseded)
            .OrderBy(p => p.EndedAt)
            .Select(p => "["
                + $"{DateOnly.FromDateTime(p.EndedAt).DayNumber - first.DayNumber},"
                + $"{(int)p.EndedAt.TimeOfDay.TotalMinutes},"
                + $"{Code(p.Outcome)},{N(p.Cube)},{N(p.Lineal)},{N(p.BuildMinutes)},"
                + $"{Text(p.Name)},{Text(p.Outcome == PanelOutcome.Completed ? string.Empty : p.Explain())}]")));
        json.Append("],");

        json.Append($"\"superseded\":{panels.Count(p => p.Outcome == PanelOutcome.Superseded)},");
        json.Append($"\"counterOffDays\":{summary.DaysFastenerCounterOff}");
        json.Append('}');

        return json.ToString();
    }

    private static string N(double value) =>
        Math.Round(value, 3).ToString(CultureInfo.InvariantCulture);

    /// <summary>A JSON string. Escapes what has to be escaped and nothing else.</summary>
    private static string Text(string? value)
    {
        var sb = new StringBuilder("\"");

        foreach (var c in value ?? string.Empty)
        {
            sb.Append(c switch
            {
                '"' => "\\\"",
                '\\' => "\\\\",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                // </script> inside a string would end the block early.
                '<' => "\\u003c",
                '>' => "\\u003e",
                '&' => "\\u0026",
                _ => c < ' ' ? $"\\u{(int)c:x4}" : c.ToString()
            });
        }

        return sb.Append('"').ToString();
    }
}
