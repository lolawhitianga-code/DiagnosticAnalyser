using DiagFileMonitor.Core.Production;

namespace DiagFileMonitor.Core.Reports;

/// <summary>
/// Report 3 - the historic production rollup, built from ProdLogV2 weekly logs.
/// <para>
/// Every figure here is measured from the logs except availability, which rests on a shift model
/// nobody has measured. The model is printed in full, and the report says plainly that two sites
/// on different models are not comparable.
/// </para>
/// </summary>
public static class ProductionReport
{
    public static ReportModel Build(
        ProductionSummary summary, string machineName = "", bool internalUse = true)
    {
        var title = string.IsNullOrWhiteSpace(machineName)
            ? "Production Report"
            : $"{machineName} - Production Report";

        var subtitle = string.Join(" · ", new[] { summary.Site, summary.SerialNumber }
            .Where(s => !string.IsNullOrWhiteSpace(s)));

        var report = new ReportModel
        {
            Title = title,
            Subtitle = subtitle,
            InternalUseOnly = internalUse,
            Period = summary.From is { } from && summary.To is { } to
                ? new ReportPeriod(from.ToDateTime(TimeOnly.MinValue),
                    to.ToDateTime(TimeOnly.MinValue).AddDays(1), "Production period")
                : null,
            Footer = "Spida Machinery. Panel counts, cube and lineal metres are measured from the "
                     + "machine's production log. Availability rests on the shift model printed above."
        };

        report.Sections.Add(Overview(summary));
        report.Sections.Add(Monthly(summary));
        report.Sections.Add(ShiftSection(summary));
        report.Sections.Add(DataQuality(summary));

        return report;
    }

    private static ReportSection Overview(ProductionSummary s)
    {
        var section = new ReportSection
        {
            Title = "Overview",
            Subtitle = $"{s.DaysWithOutput} day(s) produced something, out of {s.CalendarDays} "
                       + "calendar day(s) in the window."
        };

        section.Blocks.Add(new HeroBlock
        {
            Figure = $"{s.PanelsCompleted:N0}",
            Label = "panels completed",
            Subline = $"{s.PanelsPerProductionDay:F1} a day on the days this machine produced anything."
        });

        var table = new TableBlock
        {
            Columns = { new ReportColumn("Measure", ColumnStyle.Text, 55), new ReportColumn("Figure", ColumnStyle.Number, 45) },
            Rows =
            {
                new ReportRow { Cells = { "Panels completed", $"{s.PanelsCompleted:N0}" } },
                new ReportRow { Cells = { "Cube", $"{s.Cube:F1} m3" } },
                new ReportRow { Cells = { "Lineal metres", $"{s.Lineal:N0} m" } },
                new ReportRow { Cells = { "Production days", $"{s.DaysWithOutput} of {s.CalendarDays}" } },
                new ReportRow { Cells = { "Stepped past (routine HMI advance, not a fault)", $"{s.SteppedPast:N0}" } },
                new ReportRow { Cells = { "Stopped by the operator", $"{s.StoppedByOperator:N0}" } },
                new ReportRow { Cells = { "Fault rate", s.FaultRate is { } r ? $"{r:P2}" : "-" } },
                new ReportRow { Cells = { "Availability (see shift model)", s.Availability is { } a ? $"{a:P1}" : "-" } }
            }
        };

        section.Blocks.Add(table);

        // The superseded count is large and looks alarming until someone knows what it is.
        if (s.Superseded > 0)
        {
            section.Blocks.Add(new CalloutBlock
            {
                Lead = "Not counted as faults:",
                Text = $"{s.Superseded:N0} panel(s) were left open when a different panel name started. "
                       + "Panel names on these machines are reused labels rather than unique numbers, so "
                       + "this is the operator moving around the HMI, not abandoned work. Counting them "
                       + "as faults would put the fault rate above 30%."
            });
        }

        return section;
    }

    private static ReportSection Monthly(ProductionSummary s)
    {
        var section = new ReportSection
        {
            Title = "Output by month",
            Subtitle = "Counts are for the period in the header, not all time."
        };

        if (s.Months.Count > 1)
        {
            var chart = new BarChartBlock { ValueSuffix = string.Empty };
            var best = s.Months.Max(m => m.PanelsCompleted);

            foreach (var month in s.Months)
            {
                chart.Bars.Add(new BarChartBar(
                    month.Label,
                    $"{month.DaysWithOutput} days",
                    month.PanelsCompleted,
                    month.PanelsCompleted == 0 ? BarTone.Absent
                    : month.PanelsCompleted >= best ? BarTone.Normal
                    : BarTone.Warning));
            }

            section.Blocks.Add(chart);
        }

        var table = new TableBlock
        {
            EmptyText = "No production recorded.",
            Columns =
            {
                new ReportColumn("Month", ColumnStyle.Data, 18),
                new ReportColumn("Panels", ColumnStyle.Number, 14),
                new ReportColumn("Cube (m3)", ColumnStyle.Number, 17),
                new ReportColumn("Lineal (m)", ColumnStyle.Number, 17),
                new ReportColumn("Days", ColumnStyle.Number, 12),
                new ReportColumn("Availability", ColumnStyle.Number, 22)
            }
        };

        foreach (var month in s.Months)
        {
            table.Rows.Add(new ReportRow
            {
                Muted = month.PanelsCompleted == 0,
                Cells =
                {
                    month.Label,
                    $"{month.PanelsCompleted:N0}",
                    $"{month.Cube:F1}",
                    $"{month.Lineal:N0}",
                    month.DaysWithOutput.ToString(),
                    month.Availability is { } a ? $"{a:P1}" : "-"
                }
            });
        }

        section.Blocks.Add(table);
        return section;
    }

    private static ReportSection ShiftSection(ProductionSummary s)
    {
        var section = new ReportSection
        {
            Title = "Assumed shift and break model",
            Subtitle = s.Shift.Name
        };

        section.Blocks.Add(new CalloutBlock
        {
            Tone = CalloutTone.Caution,
            Lead = "This is an assumption:",
            Text = "Availability is not measured. It is planned shift time, less breaks, less any "
                   + "gap between panels longer than "
                   + $"{s.Shift.UnplannedStopMinutes:F0} minutes that did not fall in a break. Two "
                   + "machines are only comparable on this figure if they are on the same model. "
                   + "Change the model and every availability number here changes with it."
        });

        var table = new TableBlock
        {
            EmptyText = "No breaks in this model - the whole shift counts as planned time.",
            Columns =
            {
                new ReportColumn("Break", ColumnStyle.Text, 34),
                new ReportColumn("Window", ColumnStyle.Data, 26),
                new ReportColumn("Note", ColumnStyle.Text, 40)
            }
        };

        foreach (var window in s.Shift.Breaks)
        {
            table.Rows.Add(new ReportRow
            {
                Cells =
                {
                    window.Name,
                    $"{window.From:HH\\:mm} - {window.To:HH\\:mm}",
                    window.FoundInDataOnly
                        ? "found in the timestamps, not on an official break sheet - worth confirming"
                        : string.Empty
                }
            });
        }

        section.Blocks.Add(table);
        section.Blocks.Add(new NoteBlock
        {
            Text = $"Shift {s.Shift.ShiftStart:HH\\:mm} to {s.Shift.ShiftEnd:HH\\:mm}, "
                   + $"{s.Shift.PlannedMinutesPerDay / 60:F1} planned hours a day after breaks."
        });

        return section;
    }

    private static ReportSection DataQuality(ProductionSummary s)
    {
        var section = new ReportSection
        {
            Title = "About this data",
            Subtitle = "What the figures above do and do not cover."
        };

        var bullets = new List<string>
        {
            "Panel counts come from PanelAssembled rows in the machine's own production log. "
            + "Build time is taken as the log states it and never recalculated from the gap "
            + "between events.",
            "Consecutive duplicate lines are dropped before anything is counted. On real exports "
            + "roughly half of all MemberAssembled lines and a third of MachineStopped lines are "
            + "written twice in a row."
        };

        if (s.ImplausibleBuildTimes > 0)
        {
            bullets.Add($"{s.ImplausibleBuildTimes} panel(s) logged a build time past the plausible "
                        + "ceiling, which means a missing stop event rather than a panel that really "
                        + "took that long. They are counted as panels but left out of time averages.");
        }

        if (s.ZeroBuildTimes > 0)
        {
            bullets.Add($"{s.ZeroBuildTimes} completed panel(s) logged a build time of exactly zero. "
                        + "They are counted as panels; what a zero means here is not yet confirmed.");
        }

        bullets.Add("Days with no output are kept in the daily figures rather than dropped. A day "
                    + "where this machine sat at zero while the rest of the site worked is real "
                    + "downtime, and skipping it would hide that.");

        section.Blocks.Add(new BulletsBlock { Items = bullets });

        section.Blocks.Add(new CalloutBlock
        {
            Tone = CalloutTone.Caution,
            Lead = "Not yet verified:",
            Text = "The fastener count column in the log has not been confirmed against a physical "
                   + "count on this machine family, so it is reported raw and nothing is derived from "
                   + "it. See docs/production-reports.md for the full list of open questions."
        });

        return section;
    }
}
