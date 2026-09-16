using System.Text.RegularExpressions;

namespace DiagFileMonitor.Core.Production;

/// <summary>
/// Works out which machine a set of production logs belongs to, from the folder they sit in.
/// <para>
/// ProdLogV2 files carry no machine identity - not in the file, not in the file name, not in the
/// content. The only thing that usually says which machine they came from is the folder somebody
/// filed them under, so that is what gets read.
/// </para>
/// </summary>
public static class ProductionSerial
{
    /// <summary>
    /// Serial shapes seen in the field: M20716, M21737, AOR1694, AOR1613, DGM20771, M21642-1.
    /// <para>
    /// At least four digits are required so a model name does not match - TornadoM450 and M500 are
    /// machine types, not serial numbers, and picking one of those up would file a machine's
    /// production under the wrong name.
    /// </para>
    /// </summary>
    private static readonly Regex SerialPattern = new(
        @"\b(?<serial>(?:DG)?(?:AOR|M)\d{4,6}(?:-\d+)?)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// The serial a path suggests, or null. Folders are read from the deepest outwards, so
    /// <c>D:\Production\M21737\Reports</c> finds M21737 rather than giving up on "Reports".
    /// </summary>
    public static string? FromPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        // Split on both separators whatever the platform. The paths this reads are typed on
        // Windows, and on Linux a backslash is an ordinary character - so relying on
        // Path.DirectorySeparatorChar leaves the whole path as one segment and the outermost
        // folder wins instead of the innermost.
        var segments = path.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);

        for (var i = segments.Length - 1; i >= 0; i--)
        {
            var match = SerialPattern.Match(segments[i]);
            if (match.Success) return Tidy(match.Groups["serial"].Value);
        }

        return null;
    }

    /// <summary>
    /// Folders under <paramref name="root"/> that hold production logs, each with the serial its
    /// own name suggests. Used where somebody keeps one folder per machine.
    /// </summary>
    public static IReadOnlyList<(string Folder, string? Serial)> MachineFolders(string root)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            return Array.Empty<(string, string?)>();

        return Directory.EnumerateDirectories(root)
            .Where(HoldsProductionLogs)
            .OrderBy(d => d, StringComparer.OrdinalIgnoreCase)
            .Select(d => (Folder: d, Serial: FromPath(d)))
            .ToList();
    }

    /// <summary>True where a folder, or anything under it, holds a ProdLogV2 file.</summary>
    public static bool HoldsProductionLogs(string folder)
    {
        try
        {
            return Directory.EnumerateFiles(folder, "*.log", SearchOption.AllDirectories)
                .Any(f => ProdLogParser.LooksLikeProdLog(Path.GetFileName(f)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Upper case, since that is how the machines write their own serials.</summary>
    private static string Tidy(string serial) => serial.ToUpperInvariant();
}
