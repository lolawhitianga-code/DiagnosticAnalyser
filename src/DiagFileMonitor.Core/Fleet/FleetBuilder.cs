using DiagFileMonitor.Core.Data;
using DiagFileMonitor.Core.Models;
using DiagFileMonitor.Core.SpidaLogs;
using Microsoft.EntityFrameworkCore;

namespace DiagFileMonitor.Core.Fleet;

/// <summary>
/// Turns everything the app has stored into one row per machine.
/// <para>
/// The bundles are already on disk and the production data is already in the database. Nothing
/// here asks the customer for anything or adds a step to anybody's day - it reads what a year of
/// support has quietly accumulated and makes it answerable.
/// </para>
/// </summary>
public class FleetBuilder
{
    /// <summary>Only the panel fields the fleet view needs, so a year of panels stays cheap to read.</summary>
    private record PanelRow(string SerialNumber, DateTime EndedAt, string Outcome, double Cube, double Lineal);

    /// <summary>A bundle older than this says nothing about how a machine is running now.</summary>
    private static readonly TimeSpan Recent = TimeSpan.FromDays(90);

    private readonly Func<DiagDbContext> _contextFactory;

    public FleetBuilder(Func<DiagDbContext> contextFactory) => _contextFactory = contextFactory;

    public async Task<IReadOnlyList<FleetSnapshot>> BuildAsync(CancellationToken token = default)
    {
        await using var context = _contextFactory();

        var bundles = await context.DiagnosticFiles
            .AsNoTracking()
            .Where(f => f.SerialNumber != null && f.SerialNumber != "")
            .ToListAsync(token);

        var panels = await context.ProductionPanels
            .AsNoTracking()
            .Select(p => new PanelRow(p.SerialNumber, p.EndedAt, p.Outcome, p.Cube, p.Lineal))
            .ToListAsync(token);

        var panelsBySerial = panels
            .GroupBy(p => p.SerialNumber, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var now = DateTime.UtcNow;

        return bundles
            .GroupBy(b => b.SerialNumber!, StringComparer.OrdinalIgnoreCase)
            .Select(machine => Build(machine.Key, machine.OrderBy(b => b.ArrivedAtUtc).ToList(),
                panelsBySerial.GetValueOrDefault(machine.Key), now))
            .OrderBy(s => s.Customer)
            .ThenBy(s => s.SerialNumber)
            .ToList();
    }

    private static FleetSnapshot Build(
        string serial, List<DiagnosticFile> bundles, List<PanelRow>? panels, DateTime now)
    {
        var newest = bundles[^1];

        return new FleetSnapshot
        {
            SerialNumber = serial,

            // The newest bundle wins on identity: a machine that has been renamed or moved should
            // read as where it is now, not where it was first seen.
            Model = Best(bundles, b => b.MachineType),
            MachineName = Best(bundles, b => b.MachineName),
            Customer = Best(bundles, b => b.Customer),
            Site = Best(bundles, b => b.SiteLocation),
            SoftwareVersion = Best(bundles, b => b.Version),

            FirstSeenUtc = bundles[0].ArrivedAtUtc,
            LastSeenUtc = newest.ArrivedAtUtc,
            Bundles = bundles.Count,
            RecentBundles = bundles.Count(b => now - b.ArrivedAtUtc <= Recent),
            RepeatSubmissions = CountRepeats(bundles),

            // Panels where the production import has stored some; otherwise read the saw's own
            // report out of the newest bundle. Half the installed base is saws, and until now
            // they produced nothing this tool could measure.
            Output = PanelOutput(panels) is { Any: true } fromPanels ? fromPanels : SawOutput(newest),
            Timber = ReadTimber(newest),
            Changes = Array.Empty<(DateTime, string, string)>()
        };
    }

    /// <summary>
    /// Bundles sent within four hours of the one before. That is somebody sending the same problem
    /// again because the first one did not get them an answer, and it is the clearest measure of
    /// support pain a manufacturer has.
    /// </summary>
    private static int CountRepeats(List<DiagnosticFile> bundles) => bundles
        .Zip(bundles.Skip(1), (first, second) => second.ArrivedAtUtc - first.ArrivedAtUtc)
        .Count(gap => gap <= TimeSpan.FromHours(4));

    /// <summary>The newest non-empty value, so a blank field in a later bundle does not erase what we know.</summary>
    private static string Best(List<DiagnosticFile> bundles, Func<DiagnosticFile, string?> pick) => bundles
        .Select(pick)
        .LastOrDefault(v => !string.IsNullOrWhiteSpace(v) && v != DiagnosticFileSummary.Unknown)
        ?? string.Empty;

    /// <summary>
    /// Boards, from a saw's own <c>Reports/LatestReport.txt</c>.
    /// <para>
    /// The kind of machine is decided by what the report contains rather than by what Machine.xml
    /// calls it. On all three sample bundles the model field came back empty, and a report
    /// beginning PanelStarted is an extruder whatever the XML says.
    /// </para>
    /// </summary>
    private static OutputRecord SawOutput(DiagnosticFile bundle)
    {
        var report = FindInBundle(bundle, "LatestReport.txt");
        if (report is null) return OutputRecord.Nothing;

        var saw = SawProduction.ReadFile(report, bundle.SerialNumber ?? string.Empty);
        if (saw.BoardsCompleted == 0) return OutputRecord.Nothing;

        return new OutputRecord(
            "boards",
            saw.BoardsCompleted,
            CubicMetres: 0,           // a saw's report carries length, never volume
            saw.LinealMetres,
            saw.Days.Count,
            saw.BoardsPerWorkingHour,
            saw.BestDayRate,
            saw.From is { } from ? DateOnly.FromDateTime(from) : null,
            saw.To is { } to ? DateOnly.FromDateTime(to) : null);
    }

    private static string? FindInBundle(DiagnosticFile bundle, string fileName)
    {
        if (bundle.ExtractedPath is not { Length: > 0 } root || !Directory.Exists(root)) return null;

        try
        {
            return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .FirstOrDefault(p => Path.GetFileName(p).Equals(fileName, StringComparison.OrdinalIgnoreCase));
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static TimberProfile ReadTimber(DiagnosticFile bundle)
    {
        if (bundle.ExtractedPath is not { Length: > 0 } root || !Directory.Exists(root))
            return TimberProfile.Unknown;

        try
        {
            var stock = FindInBundle(bundle, "StockList.xml");
            return stock is null ? TimberProfile.Unknown : TimberProfileReader.Read(stock);
        }
        catch (IOException)
        {
            return TimberProfile.Unknown;
        }
    }

    /// <summary>
    /// Panel output for an extruder, from what the production import has already stored.
    /// <para>
    /// The rate is measured across the span the machine was actually producing on each day, first
    /// panel to last, rather than against a roster. No site has confirmed a roster, and a rate
    /// built on a guessed one is a number that looks measured and is not.
    /// </para>
    /// </summary>
    private static OutputRecord PanelOutput(List<PanelRow>? panels)
    {
        if (panels is null || panels.Count == 0) return OutputRecord.Nothing;

        var done = panels.Where(p => p.Outcome == "Completed").ToList();
        if (done.Count == 0) return OutputRecord.Nothing;

        var byDay = done
            .GroupBy(p => DateOnly.FromDateTime(p.EndedAt))
            .Select(g => new
            {
                Day = g.Key,
                Count = g.Count(),
                Span = g.Max(p => p.EndedAt) - g.Min(p => p.EndedAt)
            })
            .ToList();

        var working = byDay.Aggregate(TimeSpan.Zero, (total, day) => total + day.Span);

        var best = byDay
            .Where(d => d.Count >= 20 && d.Span > TimeSpan.FromMinutes(30))
            .Select(d => d.Count / d.Span.TotalHours)
            .DefaultIfEmpty(0)
            .Max();

        return new OutputRecord(
            "panels",
            done.Count,
            done.Sum(p => p.Cube),
            done.Sum(p => p.Lineal),
            byDay.Count,
            working > TimeSpan.Zero ? done.Count / working.TotalHours : 0,
            best,
            byDay.Min(d => d.Day),
            byDay.Max(d => d.Day));
    }
}
