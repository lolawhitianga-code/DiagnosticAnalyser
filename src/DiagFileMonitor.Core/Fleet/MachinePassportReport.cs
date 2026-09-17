using DiagFileMonitor.Core.Reports;

namespace DiagFileMonitor.Core.Fleet;

/// <summary>
/// One page per machine: the thing you read before you phone the customer.
/// <para>
/// Support has a report per bundle and sales has nothing at all. Neither can answer the question
/// both of them get asked first - "what do we know about this machine?" - because the answer is
/// spread across a year of bundles nobody is going to open.
/// </para>
/// </summary>
public static class MachinePassportReport
{
    public static ReportModel Build(FleetSnapshot machine, DateTime preparedUtc)
    {
        var name = machine.MachineName.Length > 0 ? machine.MachineName : machine.Model;

        var report = new ReportModel
        {
            Title = $"{machine.SerialNumber} - machine passport",
            Subtitle = string.Join(" · ", new[] { name, machine.Customer, machine.Site }
                .Where(part => part.Length > 0)),
            InternalUseOnly = true,
            Footer = "Spida Machinery. Everything on this page is measured from what this machine "
                     + "has already sent us. Nothing was asked of the customer to produce it."
        };

        report.Sections.Add(WhatWeKnow(machine));
        report.Sections.Add(WhatItProduces(machine));
        if (machine.Timber.Any) report.Sections.Add(WhatTheyBuild(machine));
        report.Sections.Add(SupportHistory(machine, preparedUtc));

        return report;
    }

    private static ReportSection WhatWeKnow(FleetSnapshot machine)
    {
        var section = new ReportSection
        {
            Title = "The machine",
            Subtitle = machine.DaysKnown > 0
                ? $"We have been seeing bundles from it for {machine.DaysKnown} day(s)."
                : "We have seen it once."
        };

        section.Blocks.Add(new TableBlock
        {
            Columns = { new ReportColumn("", ColumnStyle.Text, 40), new ReportColumn("", ColumnStyle.Text, 60) },
            Rows =
            {
                Row("Serial", machine.SerialNumber),
                Row("Model", Or(machine.Model, "not reported")),
                Row("Name on the floor", Or(machine.MachineName, "not reported")),
                Row("Customer", Or(machine.Customer, "not reported")),
                Row("Site", Or(machine.Site, "not reported")),
                Row("Software", Or(machine.SoftwareVersion, "not reported")),
                Row("First bundle", machine.FirstSeenUtc is { } first ? $"{first:d MMM yyyy}" : "-"),
                Row("Last bundle", machine.LastSeenUtc is { } last
                    ? $"{last:d MMM yyyy} ({machine.DaysSinceLastBundle} day(s) ago)"
                    : "-")
            }
        });

        return section;
    }

    private static ReportSection WhatItProduces(FleetSnapshot machine)
    {
        var section = new ReportSection { Title = "What it produces" };

        if (!machine.Output.Any)
        {
            section.Blocks.Add(new NoteBlock
            {
                Text = "No production data has been stored for this machine. Either its logs have "
                       + "not been imported, or it is a kind of machine whose output we cannot yet "
                       + "measure. That is a gap in this tool, not a statement about the machine."
            });

            return section;
        }

        var output = machine.Output;

        section.Blocks.Add(new HeroBlock
        {
            Figure = $"{output.Total:N0}",
            Label = $"{output.Unit} across {output.DaysWithOutput} producing day(s)",
            Subline = output.From is { } from && output.To is { } to
                ? $"{from:d MMM yyyy} to {to:d MMM yyyy}"
                : string.Empty
        });

        section.Blocks.Add(new TableBlock
        {
            Columns =
            {
                new ReportColumn("Measured as", ColumnStyle.Text, 45),
                new ReportColumn("", ColumnStyle.Number, 55)
            },
            Rows =
            {
                Row("Cube (m3)", $"{output.CubicMetres:F1}"),
                Row("Lineal (m)", $"{output.LinealMetres:N0}"),
                Row($"{Capital(output.Unit)} an hour, on average", $"{output.AverageRatePerHour:F1}"),
                Row($"{Capital(output.Unit)} an hour, on its best day", $"{output.BestRatePerHour:F1}")
            }
        });

        if (output.ShareOfItsOwnBest is { } share)
        {
            var headroom = output.BestRatePerHour - output.AverageRatePerHour;

            section.Blocks.Add(new CalloutBlock
            {
                Lead = $"It is getting {share:P0} of what it has already shown it can do:",
                Text = $"On its best day this machine ran at {output.BestRatePerHour:F1} "
                       + $"{output.Unit} an hour. Across the whole window it averaged "
                       + $"{output.AverageRatePerHour:F1}. That gap is {headroom:F1} an hour, on this "
                       + "site, with this site's timber and this site's people - so it is not a "
                       + "brochure figure anybody can argue with. What it does not say is why. "
                       + "Short runs, a change of product, an operator or a fault all look the same "
                       + "from here."
            });
        }

        return section;
    }

    private static ReportSection WhatTheyBuild(FleetSnapshot machine)
    {
        var section = new ReportSection
        {
            Title = "What they build",
            Subtitle = "Read from the site's own stock list - the sizes they have switched on."
        };

        section.Blocks.Add(new BulletsBlock
        {
            Items =
            {
                $"Member sizes in use: {string.Join(", ", machine.Timber.Sizes)}",
                machine.Timber.Lengths.Count > 0
                    ? $"Stock lengths: {string.Join(", ", machine.Timber.Lengths)} mm"
                    : "No stock lengths switched on.",
                $"Deepest member stocked: {machine.Timber.DeepestMember} mm"
            }
        });

        if (machine.Timber.DeepestMember >= 240)
        {
            section.Blocks.Add(new NoteBlock
            {
                Text = "A site stocking members this deep is doing heavy floor or rafter work. "
                       + "Worth checking the machine on the floor is rated for what they are now "
                       + "buying timber for - stock lists change before machines do."
            });
        }

        return section;
    }

    private static ReportSection SupportHistory(FleetSnapshot machine, DateTime preparedUtc)
    {
        var section = new ReportSection { Title = "What support has cost" };

        section.Blocks.Add(new TableBlock
        {
            Columns = { new ReportColumn("", ColumnStyle.Text, 55), new ReportColumn("", ColumnStyle.Number, 45) },
            Rows =
            {
                Row("Bundles, ever", $"{machine.Bundles}"),
                Row("Bundles in the last 90 days", $"{machine.RecentBundles}"),
                Row("Sent within hours of another", $"{machine.RepeatSubmissions}")
            }
        });

        if (machine.RepeatSubmissions > 0)
        {
            section.Blocks.Add(new CalloutBlock
            {
                Lead = "Somebody sent the same problem twice:",
                Text = $"{machine.RepeatSubmissions} bundle(s) arrived within four hours of the one "
                       + "before. That is usually an operator who did not get an answer the first "
                       + "time, and it is the clearest measure of support pain we have."
            });
        }

        return section;
    }

    private static ReportRow Row(string label, string value) => new() { Cells = { label, value } };

    private static string Or(string value, string fallback) => value.Length > 0 ? value : fallback;

    private static string Capital(string value) =>
        value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value[1..];
}
