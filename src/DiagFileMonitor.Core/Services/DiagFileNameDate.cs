using System.Globalization;
using System.IO.Compression;
using System.Text.RegularExpressions;

namespace DiagFileMonitor.Core.Services;

/// <summary>
/// Works out when a diagnostic bundle was really made.
/// <para>
/// The best source is the timestamp Spida puts at the front of a support file name, e.g.
/// <c>_7_27_2026 9-53-10 PM . M21461SupportFile.szip</c>. The date is month_day_year (confirmed by
/// names like <c>_10_24_2025</c>, where 24 cannot be a month), and neither the month, day nor hour
/// is zero padded.
/// </para>
/// <para>
/// Plenty of bundles arrive without a date in the name at all - <c>AOR1613SupportFiles.szip</c> -
/// so the next best source is the newest file inside the zip, which is the moment the machine
/// wrote the export. The file's own creation time on disk is the worst source and is no longer
/// used: Windows keeps the original creation time when a file of the same name is replaced in the
/// same folder (file system tunnelling), so saving the same attachment repeatedly leaves every
/// copy stamped with the date of the first one. That is not a cosmetic problem - a bundle stamped
/// months old is dropped by the "only files newer than" filter without ever being read.
/// </para>
/// </summary>
public static class DiagFileNameDate
{
    private static readonly Regex Pattern = new(
        @"(?<month>\d{1,2})_(?<day>\d{1,2})_(?<year>\d{4})[ _]+(?<hour>\d{1,2})-(?<minute>\d{2})-(?<second>\d{2})\s*(?<meridiem>AM|PM)?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Returns the timestamp in UTC, or null when the name carries no usable date.
    /// <paramref name="fileNameTimesAreUtc"/> says whether the machine wrote the time in UTC
    /// or in its own local time.
    /// </summary>
    public static DateTime? TryParseUtc(string fileName, bool fileNameTimesAreUtc)
    {
        var match = Pattern.Match(Path.GetFileName(fileName));
        if (!match.Success) return null;

        var hour = int.Parse(match.Groups["hour"].Value, CultureInfo.InvariantCulture);
        var meridiem = match.Groups["meridiem"].Value;

        if (meridiem.Length > 0)
        {
            if (hour is < 1 or > 12) return null;

            var isPm = meridiem.Equals("PM", StringComparison.OrdinalIgnoreCase);
            hour = hour switch
            {
                12 when !isPm => 0,     // 12:xx AM is midnight
                12 => 12,               // 12:xx PM is noon
                _ when isPm => hour + 12,
                _ => hour
            };
        }

        try
        {
            var local = new DateTime(
                int.Parse(match.Groups["year"].Value, CultureInfo.InvariantCulture),
                int.Parse(match.Groups["month"].Value, CultureInfo.InvariantCulture),
                int.Parse(match.Groups["day"].Value, CultureInfo.InvariantCulture),
                hour,
                int.Parse(match.Groups["minute"].Value, CultureInfo.InvariantCulture),
                int.Parse(match.Groups["second"].Value, CultureInfo.InvariantCulture),
                fileNameTimesAreUtc ? DateTimeKind.Utc : DateTimeKind.Local);

            return fileNameTimesAreUtc ? local : local.ToUniversalTime();
        }
        catch (ArgumentOutOfRangeException)
        {
            // Month 13, day 32 and similar: the name only looked like a date.
            return null;
        }
    }

    /// <summary>
    /// When the bundle was made: the name's timestamp, then the newest file inside the zip, then
    /// the file's last write time, then now.
    /// </summary>
    public static DateTime ArrivedUtc(string path, bool fileNameTimesAreUtc)
    {
        if (TryParseUtc(path, fileNameTimesAreUtc) is { } fromName) return fromName;
        if (NewestEntryUtc(path, fileNameTimesAreUtc) is { } fromZip) return fromZip;

        try
        {
            // Last write, never creation time - see the note on this class.
            return new FileInfo(path).LastWriteTimeUtc;
        }
        catch (IOException)
        {
            return DateTime.UtcNow;
        }
    }

    /// <summary>
    /// The newest timestamp among the files inside the bundle, which is when the machine wrote the
    /// export. A bundle carries plenty of old files - machine configuration from years back - so it
    /// is the newest that dates the export, not the oldest.
    /// </summary>
    public static DateTime? NewestEntryUtc(string path, bool timesAreUtc)
    {
        try
        {
            using var archive = ZipFile.OpenRead(path);

            DateTime? newest = null;
            var ceiling = DateTime.UtcNow.AddDays(1);

            foreach (var entry in archive.Entries)
            {
                var written = entry.LastWriteTime.DateTime;

                // A zip stores no time zone, so the stamp is the machine's own clock - the same
                // assumption the file name gets.
                var utc = timesAreUtc
                    ? DateTime.SpecifyKind(written, DateTimeKind.Utc)
                    : DateTime.SpecifyKind(written, DateTimeKind.Local).ToUniversalTime();

                // 1980-01-01 is the zero value of a DOS timestamp, and anything in the future is a
                // machine with its clock wrong. Neither dates the export.
                if (utc.Year <= 1980 || utc > ceiling) continue;

                if (newest is null || utc > newest) newest = utc;
            }

            return newest;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            // Not readable as a zip, or gone. The caller has another fallback.
            return null;
        }
    }
}
