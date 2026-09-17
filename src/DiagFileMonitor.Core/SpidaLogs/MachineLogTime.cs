using System.Globalization;

namespace DiagFileMonitor.Core.SpidaLogs;

/// <summary>
/// Reads a time somebody has typed, the way the machine log writes them.
/// <para>
/// Not <see cref="TimeSpan.TryParse(string, out TimeSpan)"/>, which reads a bare number as a
/// count of days: "8" for eight o'clock becomes eight days, and "981" - a line number typed in
/// the wrong box - becomes 981 days. Both parse happily and land past the end of any log, which
/// is worse than refusing them, because nothing tells the user their time was not understood.
/// </para>
/// </summary>
public static class MachineLogTime
{
    private static readonly string[] Formats =
    {
        @"h\:mm", @"hh\:mm",
        @"h\:mm\:ss", @"hh\:mm\:ss",
        @"h\:mm\:ss\.FFFFFFF", @"hh\:mm\:ss\.FFFFFFF"
    };

    public static bool TryParse(string? text, out TimeSpan time)
    {
        time = default;

        var value = (text ?? string.Empty).Trim();

        // A time has a colon in it. Without this a bare number is read as days.
        if (!value.Contains(':')) return false;

        return TimeSpan.TryParseExact(value, Formats, CultureInfo.InvariantCulture, out time)
               && time >= TimeSpan.Zero
               && time < TimeSpan.FromDays(1);
    }
}
