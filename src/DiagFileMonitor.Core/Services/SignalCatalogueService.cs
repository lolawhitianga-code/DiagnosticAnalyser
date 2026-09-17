using DiagFileMonitor.Core.Data;
using DiagFileMonitor.Core.Models;
using DiagFileMonitor.Core.SpidaLogs;
using Microsoft.EntityFrameworkCore;

namespace DiagFileMonitor.Core.Services;

/// <summary>What one machine log taught the catalogue.</summary>
public record CatalogueResult(int PointsSeen, int PointsNew, int ChangesCounted)
{
    public string Summary => PointsSeen == 0
        ? "No I/O changes in that log."
        : $"{PointsSeen} point(s) seen, {PointsNew} of them new to this machine.";
}

/// <summary>
/// Keeps the list of I/O points every machine is known to have, learned from its logs.
/// <para>
/// Nothing else carries this list - Machine.xml has no I/O map - so the catalogue is built by
/// watching. It is what lets a reading say "this machine has 66 known points and only 12 of them
/// move in this log", which is a far more useful thing to know than the 12 on their own.
/// </para>
/// </summary>
public class SignalCatalogueService
{
    private readonly Func<DiagDbContext> _contextFactory;

    public SignalCatalogueService(Func<DiagDbContext> contextFactory) => _contextFactory = contextFactory;

    /// <summary>
    /// Folds one log's points into what is already known about the machine. Idempotent on the
    /// identity, so re-processing a bundle updates counts rather than doubling the list.
    /// </summary>
    public async Task<CatalogueResult> RecordAsync(
        string serialNumber, string machineType, IoTimeline timeline, CancellationToken token = default)
    {
        var serial = serialNumber.Trim();
        if (serial.Length == 0 || timeline.Signals.Count == 0)
            return new CatalogueResult(0, 0, 0);

        await using var context = _contextFactory();

        var held = await context.MachineSignals
            .Where(s => s.SerialNumber == serial)
            .ToListAsync(token);

        var index = held.ToDictionary(
            s => (s.Kind, s.Name, s.Address),
            s => s,
            new KeyComparer());

        var now = DateTime.UtcNow;
        var added = 0;
        var changes = 0;

        foreach (var id in timeline.Signals)
        {
            var moves = timeline.HistoryOf(id).Count;
            changes += moves;

            var key = (id.Kind.ToString(), id.Name, id.Address);

            if (index.TryGetValue(key, out var existing))
            {
                existing.LastSeenUtc = now;
                existing.BundlesSeenIn += 1;
                existing.TotalChanges += moves;
                if (machineType.Length > 0) existing.MachineType = machineType;
                continue;
            }

            added++;

            context.MachineSignals.Add(new MachineSignal
            {
                SerialNumber = serial,
                MachineType = machineType,
                Kind = id.Kind.ToString(),
                Name = id.Name,
                Address = id.Address,
                FirstSeenUtc = now,
                LastSeenUtc = now,
                BundlesSeenIn = 1,
                TotalChanges = moves
            });
        }

        await context.SaveChangesAsync(token);

        return new CatalogueResult(timeline.Signals.Count, added, changes);
    }

    /// <summary>Every point known for one machine, outputs first, then by name.</summary>
    public async Task<IReadOnlyList<MachineSignal>> ForSerialAsync(
        string serialNumber, CancellationToken token = default)
    {
        var serial = serialNumber.Trim();
        if (serial.Length == 0) return Array.Empty<MachineSignal>();

        await using var context = _contextFactory();

        return await context.MachineSignals
            .Where(s => s.SerialNumber == serial)
            .OrderBy(s => s.Kind)
            .ThenBy(s => s.Name)
            .ThenBy(s => s.Address)
            .ToListAsync(token);
    }

    /// <summary>
    /// Points known for the machine that this log never moves.
    /// <para>
    /// This is the half a single log cannot tell you. A clamp that moves in every other bundle and
    /// sits still in this one is worth a look, and so is a sensor that has never once been seen to
    /// change on any machine of the type.
    /// </para>
    /// </summary>
    public async Task<IReadOnlyList<MachineSignal>> NotInThisLogAsync(
        string serialNumber, IoTimeline timeline, CancellationToken token = default)
    {
        var known = await ForSerialAsync(serialNumber, token);

        var here = timeline.Signals
            .Select(s => (s.Kind.ToString(), s.Name, s.Address))
            .ToHashSet(new KeyComparer());

        return known
            .Where(s => !here.Contains((s.Kind, s.Name, s.Address)))
            .ToList();
    }

    /// <summary>Machines the catalogue holds anything for, with how much.</summary>
    public async Task<IReadOnlyList<(string SerialNumber, string MachineType, int Points)>> MachinesAsync(
        CancellationToken token = default)
    {
        await using var context = _contextFactory();

        var rows = await context.MachineSignals
            .GroupBy(s => new { s.SerialNumber, s.MachineType })
            .Select(g => new { g.Key.SerialNumber, g.Key.MachineType, Points = g.Count() })
            .OrderBy(r => r.SerialNumber)
            .ToListAsync(token);

        return rows.Select(r => (r.SerialNumber, r.MachineType, r.Points)).ToList();
    }

    /// <summary>Names and addresses are compared the way the logs write them: case-insensitively.</summary>
    private sealed class KeyComparer : IEqualityComparer<(string Kind, string Name, string Address)>
    {
        public bool Equals((string Kind, string Name, string Address) a, (string Kind, string Name, string Address) b) =>
            string.Equals(a.Kind, b.Kind, StringComparison.OrdinalIgnoreCase)
            && string.Equals(a.Name, b.Name, StringComparison.OrdinalIgnoreCase)
            && string.Equals(a.Address, b.Address, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string Kind, string Name, string Address) key) => HashCode.Combine(
            key.Kind.ToLowerInvariant(), key.Name.ToLowerInvariant(), key.Address.ToLowerInvariant());
    }
}
