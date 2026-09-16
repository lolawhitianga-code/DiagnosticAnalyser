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
        ProductionSummary summary, string machineName = "", bool internalUse = true,
        IReadOnlyList<PanelRecord>? panels = null)
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
        if (!summary.Shift.Ignored) report.Sections.Add(WhereTheShiftWent(summary));
        report.Sections.Add(NotBuilt(summary, panels));
        report.Sections.Add(Daily(summary));
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

        // Output stands on its own three ways, so a machine building fewer but bigger panels is
        // not read as a slower one.
        section.Blocks.Add(new TableBlock
        {
            Columns =
            {
                new ReportColumn("Measured as", ColumnStyle.Text, 40),
                new ReportColumn("Total", ColumnStyle.Number, 30),
                new ReportColumn("A day", ColumnStyle.Number, 30)
            },
            Rows =
            {
                new ReportRow { Cells = { "Panels", $"{s.PanelsCompleted:N0}", $"{s.PanelsPerProductionDay:F1}" } },
                new ReportRow { Cells = { "Cube (m3)", $"{s.Cube:F1}",
                    s.DaysWithOutput > 0 ? $"{s.Cube / s.DaysWithOutput:F2}" : "-" } },
                new ReportRow { Cells = { "Lineal (m)", $"{s.Lineal:N0}",
                    s.DaysWithOutput > 0 ? $"{s.Lineal / s.DaysWithOutput:N0}" : "-" } }
            }
        });

        var table = new TableBlock
        {
            Columns = { new ReportColumn("", ColumnStyle.Text, 55), new ReportColumn("", ColumnStyle.Number, 45) },
            Rows =
            {
                new ReportRow { Cells = { "Production days", $"{s.DaysWithOutput} of {s.CalendarDays}" } },
                new ReportRow { Cells = { "Stepped past (routine HMI advance, not a fault)", $"{s.SteppedPast:N0}" } },
                new ReportRow { Cells = { "Panels that went wrong", $"{s.Faults:N0}" } },
                new ReportRow { Cells = { "Fault rate", s.FaultRate is { } r ? $"{r:P2}" : "-" } },
                new ReportRow
                {
                    Muted = s.Shift.Ignored,
                    Cells =
                    {
                        "Availability",
                        s.Availability is { } a ? $"{a:P1}" : "not reported - no shift model"
                    }
                },
                new ReportRow
                {
                    Muted = s.Shift.Ignored,
                    Cells =
                    {
                        "Panels an hour, while running / across the shift",
                        s.RateWhileRunning is { } running && s.RateAcrossShift is { } across
                            ? $"{running:F1} / {across:F1}"
                            : "-"
                    }
                }
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

    /// <summary>
    /// Where the rostered time actually went. This is the section that tells a factory manager
    /// whether a slow week was the machine or the way it was fed.
    /// </summary>
    private static ReportSection WhereTheShiftWent(ProductionSummary s)
    {
        var section = new ReportSection
        {
            Title = "Where the shift went",
            Subtitle = "Rostered time, less breaks, split into running and waiting."
        };

        var planned = s.PlannedMinutes;
        string Share(double minutes) => planned > 0 ? $"{minutes / planned:P1}" : "-";
        string Hours(double minutes) => $"{minutes / 60:N1} hr";

        section.Blocks.Add(new TableBlock
        {
            Columns =
            {
                new ReportColumn("", ColumnStyle.Text, 46),
                new ReportColumn("Time", ColumnStyle.Number, 27),
                new ReportColumn("Share", ColumnStyle.Number, 27)
            },
            Rows =
            {
                new ReportRow { Cells = { "Running", Hours(s.RunMinutes), Share(s.RunMinutes) } },
                new ReportRow { Cells = { $"Unplanned stops ({s.UnplannedStops:N0})",
                    Hours(s.StopMinutes), Share(s.StopMinutes) } },
                new ReportRow { Cells = { "Start-up and tail",
                    Hours(s.StartupAndTailMinutes), Share(s.StartupAndTailMinutes) } },
                new ReportRow { Cells = { "Rostered, after breaks", Hours(planned), "100%" } }
            }
        });

        section.Blocks.Add(new CalloutBlock
        {
            Lead = "Reading:",
            Text = "Start-up and tail is rostered time either side of the day's work - the machine "
                   + "was on shift and nothing had been sent to it yet, or nothing was left. It is "
                   + "counted separately from stops because it is usually a scheduling matter "
                   + "rather than a machine one."
        });

        return section;
    }

    /// <summary>
    /// Panels the machine was asked for and did not make, each with a plain sentence saying why.
    /// Stepped past is listed separately and loudly, because it is much the larger number and is
    /// not a fault at all.
    /// </summary>
    private static ReportSection NotBuilt(ProductionSummary s, IReadOnlyList<PanelRecord>? panels)
    {
        var section = new ReportSection
        {
            Title = "Panels the machine did not build",
            Subtitle = $"{s.SteppedPast:N0} stepped past, {s.Faults:N0} went wrong."
        };

        section.Blocks.Add(new CalloutBlock
        {
            Lead = "Stepped past is not a fault:",
            Text = $"{s.SteppedPast:N0} panel(s) were advanced on the HMI without the machine being "
                   + "asked to build them - no time on the clock and nothing fired. That is how the "
                   + "job list is worked through. Counting it as a fault would drown out the "
                   + $"{s.Faults:N0} that really went wrong."
        });

        var faults = panels?.Where(p => p.IsFault).OrderByDescending(p => p.EndedAt).ToList()
                     ?? new List<PanelRecord>();

        if (faults.Count == 0)
        {
            section.Blocks.Add(new NoteBlock
            {
                Text = s.Faults == 0
                    ? "Nothing went wrong in this period."
                    : "The panel-by-panel list is not available for this report."
            });

            return section;
        }

        var table = new TableBlock
        {
            Scroll = faults.Count > 12,
            Columns =
            {
                new ReportColumn("When", ColumnStyle.Timestamp, 14),
                new ReportColumn("Panel", ColumnStyle.Data, 14),
                new ReportColumn("What happened", ColumnStyle.Text, 22),
                new ReportColumn("Why it is recorded that way", ColumnStyle.Text, 50)
            }
        };

        foreach (var panel in faults.Take(300))
        {
            table.Rows.Add(new ReportRow
            {
                Cells =
                {
                    $"{panel.EndedAt:yyyy-MM-dd}\n{panel.EndedAt:HH:mm:ss}",
                    panel.Name,
                    Describe(panel.Outcome),
                    panel.Explain()
                }
            });
        }

        section.Blocks.Add(table);

        if (faults.Count > 300)
            section.Blocks.Add(new NoteBlock { Text = $"Showing the most recent 300 of {faults.Count:N0}." });

        return section;
    }

    private static string Describe(PanelOutcome outcome) => outcome switch
    {
        PanelOutcome.StoppedByOperator => "Stopped by the operator",
        PanelOutcome.RanButNailedNothing => "Ran but fired nothing",
        PanelOutcome.AbandonedPartWay => "Abandoned part way",
        PanelOutcome.SteppedPast => "Stepped past",
        PanelOutcome.Superseded => "Superseded",
        _ => "Built"
    };

    /// <summary>Every day in the window, including the ones with nothing on them.</summary>
    private static ReportSection Daily(ProductionSummary s)
    {
        var section = new ReportSection
        {
            Title = "Output by day",
            Subtitle = "Every calendar day in the window. A day at zero while the site was working "
                       + "is downtime, not a missing record, so it is kept."
        };

        var table = new TableBlock
        {
            Scroll = s.Days.Count > 14,
            EmptyText = "No days in this window.",
            Columns =
            {
                new ReportColumn("Day", ColumnStyle.Timestamp, 16),
                new ReportColumn("Panels", ColumnStyle.Number, 12),
                new ReportColumn("Cube (m3)", ColumnStyle.Number, 14),
                new ReportColumn("Lineal (m)", ColumnStyle.Number, 14),
                new ReportColumn("Stepped past", ColumnStyle.Number, 14),
                new ReportColumn("Went wrong", ColumnStyle.Number, 12),
                new ReportColumn("Availability", ColumnStyle.Number, 18)
            }
        };

        foreach (var day in s.Days)
        {
            table.Rows.Add(new ReportRow
            {
                Muted = !day.HadOutput,
                Cells =
                {
                    day.Day.ToString("yyyy-MM-dd"),
                    day.HadOutput ? $"{day.PanelsCompleted:N0}" : "nothing",
                    day.HadOutput ? $"{day.Cube:F1}" : "-",
                    day.HadOutput ? $"{day.Lineal:N0}" : "-",
                    $"{day.SteppedPast:N0}",
                    $"{day.Faults:N0}",
                    day.Availability is { } a ? $"{a:P0}" : "-"
                }
            });
        }

        section.Blocks.Add(table);
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
