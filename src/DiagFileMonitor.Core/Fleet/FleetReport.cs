using DiagFileMonitor.Core.Reports;

namespace DiagFileMonitor.Core.Fleet;

/// <summary>
/// The installed base on one page, and the list of people to ring.
/// <para>
/// A manufacturer who sells machines and then supports them accumulates something rare: measured
/// performance for every machine it has ever sold, on real sites, with real timber. Almost nobody
/// uses it. This report turns it into three things a business can act on - what each model really
/// does, which machines are heading for trouble, and who is worth a call this week.
/// </para>
/// </summary>
public static class FleetReport
{
    /// <summary>Fewer machines than this and a model average is one machine wearing a hat.</summary>
    private const int EnoughToAverage = 3;

    public static ReportModel Build(
        IReadOnlyList<FleetSnapshot> fleet, DateTime preparedUtc, string preparedFor = "")
    {
        var report = new ReportModel
        {
            Title = "The installed base",
            Subtitle = $"{fleet.Count} machine(s) across "
                       + $"{fleet.Select(m => m.Customer).Where(c => c.Length > 0).Distinct().Count()} customer(s)"
                       + (preparedFor.Length > 0 ? $" · prepared for {preparedFor}" : string.Empty),
            InternalUseOnly = true,
            Footer = "Spida Machinery. Built from support bundles already on this PC. Every figure "
                     + "is measured from a machine's own logs; none of it was asked of a customer."
        };

        report.Sections.Add(Overview(fleet));
        report.Sections.Add(WhoToRing(fleet, preparedUtc));
        report.Sections.Add(ByModel(fleet));
        report.Sections.Add(EveryMachine(fleet));
        report.Sections.Add(WhatThisCannotTellYou(fleet));

        return report;
    }

    private static ReportSection Overview(IReadOnlyList<FleetSnapshot> fleet)
    {
        var producing = fleet.Where(m => m.Output.Any).ToList();

        var section = new ReportSection
        {
            Title = "Where the fleet is",
            Subtitle = producing.Count < fleet.Count
                ? $"{producing.Count} of {fleet.Count} machine(s) have production data stored. The "
                  + "rest have sent bundles but no output we can measure yet."
                : "Every machine has production data stored."
        };

        section.Blocks.Add(new HeroBlock
        {
            Figure = $"{fleet.Count}",
            Label = "machines we hear from",
            Subline = $"{fleet.Count(m => m.RecentBundles > 0)} of them in the last ninety days."
        });

        section.Blocks.Add(new TableBlock
        {
            Columns = { new ReportColumn("", ColumnStyle.Text, 60), new ReportColumn("", ColumnStyle.Number, 40) },
            Rows =
            {
                Row("Customers", $"{fleet.Select(m => m.Customer).Where(c => c.Length > 0).Distinct().Count()}"),
                Row("Models", $"{fleet.Select(m => m.Model).Where(c => c.Length > 0).Distinct().Count()}"),
                Row("Bundles received, all time", $"{fleet.Sum(m => m.Bundles):N0}"),
                Row("Bundles in the last 90 days", $"{fleet.Sum(m => m.RecentBundles):N0}"),
                Row("Machines silent for 180+ days", $"{fleet.Count(m => m.Bundles >= 2 && m.DaysSinceLastBundle is > 180 and < int.MaxValue)}")
            }
        });

        return section;
    }

    private static ReportSection WhoToRing(IReadOnlyList<FleetSnapshot> fleet, DateTime preparedUtc)
    {
        var radar = OpportunityRadar.Scan(fleet, preparedUtc);

        var section = new ReportSection
        {
            Title = "Who to ring",
            Subtitle = "Ranked by how much the data is saying, not by how big the customer is. "
                       + "Each line carries the evidence that raised it."
        };

        if (radar.Count == 0)
        {
            section.Blocks.Add(new NoteBlock
            {
                Text = "Nothing in the fleet is asking for a call. That is the answer a quiet week "
                       + "should give - a radar that lights up every week gets switched off."
            });

            return section;
        }

        foreach (var entry in radar.Take(12))
        {
            var machine = entry.Machine;

            section.Blocks.Add(new TextBlock
            {
                Text = $"{machine.Customer} - {machine.SerialNumber} "
                       + $"({(machine.Model.Length > 0 ? machine.Model : "model not reported")}) - for {entry.Who}"
            });

            section.Blocks.Add(new BulletsBlock
            {
                Items = entry.Signals.Select(s => s.Describe()).ToList()
            });
        }

        return section;
    }

    private static ReportSection ByModel(IReadOnlyList<FleetSnapshot> fleet)
    {
        var section = new ReportSection
        {
            Title = "What each model actually does",
            Subtitle = "Measured across the installed base rather than from a specification. This "
                       + "is the number to put in a proposal, because it is the number a customer "
                       + "will get."
        };

        var models = fleet
            .Where(m => m.Model.Length > 0 && m.Output.Any)
            .GroupBy(m => m.Model, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .ToList();

        if (models.Count == 0)
        {
            section.Blocks.Add(new PendingBlock
            {
                Text = "No model has production data stored against it yet. Import some production "
                       + "logs and this section fills itself in."
            });

            return section;
        }

        var table = new TableBlock
        {
            Columns =
            {
                new ReportColumn("Model", ColumnStyle.Text, 30),
                new ReportColumn("Machines", ColumnStyle.Number, 14),
                new ReportColumn("Typical rate", ColumnStyle.Number, 20),
                new ReportColumn("Best seen", ColumnStyle.Number, 18),
                new ReportColumn("Unit", ColumnStyle.Text, 18)
            }
        };

        foreach (var model in models)
        {
            var rates = model.Select(m => m.Output.AverageRatePerHour).Where(r => r > 0).OrderBy(r => r).ToList();
            var best = model.Max(m => m.Output.BestRatePerHour);

            table.Rows.Add(new ReportRow
            {
                // Say plainly when a "model average" is one machine.
                Muted = model.Count() < EnoughToAverage,
                Cells =
                {
                    model.Key,
                    $"{model.Count()}",
                    rates.Count > 0 ? $"{rates[rates.Count / 2]:F1}" : "-",
                    $"{best:F1}",
                    model.First().Output.Unit
                }
            });
        }

        section.Blocks.Add(table);

        if (models.Any(m => m.Count() < EnoughToAverage))
        {
            section.Blocks.Add(new NoteBlock
            {
                Text = $"Greyed rows are models we have fewer than {EnoughToAverage} machines' data "
                       + "for. One machine's habits are not a model's performance, and quoting them "
                       + "as such is how a proposal becomes a complaint."
            });
        }

        return section;
    }

    private static ReportSection EveryMachine(IReadOnlyList<FleetSnapshot> fleet)
    {
        var section = new ReportSection { Title = "Every machine" };

        var table = new TableBlock
        {
            Columns =
            {
                new ReportColumn("Serial", ColumnStyle.Data, 12),
                new ReportColumn("Customer", ColumnStyle.Text, 24),
                new ReportColumn("Model", ColumnStyle.Text, 18),
                new ReportColumn("Bundles", ColumnStyle.Number, 10),
                new ReportColumn("Last heard", ColumnStyle.Timestamp, 14),
                new ReportColumn("Output", ColumnStyle.Number, 22)
            }
        };

        foreach (var machine in fleet.OrderBy(m => m.Customer).ThenBy(m => m.SerialNumber))
        {
            table.Rows.Add(new ReportRow
            {
                Cells =
                {
                    machine.SerialNumber,
                    Or(machine.Customer, "-"),
                    Or(machine.Model, "-"),
                    $"{machine.Bundles}",
                    machine.LastSeenUtc is { } last ? $"{last:d MMM yy}" : "-",
                    machine.Output.Any
                        ? $"{machine.Output.Total:N0} {machine.Output.Unit}"
                        : "none stored"
                }
            });
        }

        section.Blocks.Add(table);
        return section;
    }

    /// <summary>
    /// The limits, said out loud. A fleet report that does not carry its own caveats will be
    /// quoted to a customer inside a week.
    /// </summary>
    private static ReportSection WhatThisCannotTellYou(IReadOnlyList<FleetSnapshot> fleet)
    {
        var section = new ReportSection { Title = "What this cannot tell you" };

        var items = new List<string>
        {
            "It only knows machines that have sent a support bundle. A machine running perfectly "
            + "and never sending one is invisible here, and those are usually the best ones.",

            "Rates are measured from first output to last output on each producing day. No site has "
            + "confirmed a roster, so this deliberately does not claim to be a shift figure.",

            "It has no price, no margin and no service cost in it, so it cannot tell you what any "
            + "of this is worth. It can tell you where the hours are going.",

            "\"Best day\" is the best this machine has been seen to do, not the best it could do. "
            + "A machine that has never had a good day will not show its headroom."
        };

        var noOutput = fleet.Count(m => !m.Output.Any);

        if (noOutput > 0)
        {
            items.Add($"{noOutput} of {fleet.Count} machine(s) have no production data stored, so "
                      + "they appear in the support figures and in none of the output ones.");
        }

        section.Blocks.Add(new BulletsBlock { Items = items });
        return section;
    }

    private static ReportRow Row(string label, string value) => new() { Cells = { label, value } };

    private static string Or(string value, string fallback) => value.Length > 0 ? value : fallback;
}
