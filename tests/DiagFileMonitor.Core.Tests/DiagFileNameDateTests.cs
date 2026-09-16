using System.IO.Compression;
using DiagFileMonitor.Core.Services;

namespace DiagFileMonitor.Core.Tests;

public class DiagFileNameDateTests
{
    [Theory]
    // Real names taken from the support folder.
    [InlineData("_7_27_2026 9-53-10 PM . M21461SupportFile.szip", 2026, 7, 27, 21, 53, 10)]
    [InlineData("_7_27_2026 10-15-10 PM . M20716SupportFile.szip", 2026, 7, 27, 22, 15, 10)]
    [InlineData("_10_6_2025 11-01-44 PM . M21036SupportFile.szip", 2025, 10, 6, 23, 1, 44)]
    [InlineData("_1_28_2026 1-00-58 AM . AOR00000SupportFile.szip", 2026, 1, 28, 1, 0, 58)]
    [InlineData("_12_18_2025 11-56-05 AM . AORSupportFile.szip", 2025, 12, 18, 11, 56, 5)]
    [InlineData("_3_13_2025 7-44-35 PM . 20114-6SupportFile.szip", 2025, 3, 13, 19, 44, 35)]
    public void ReadsTheTimestampFromTheName(string name, int year, int month, int day, int hour, int minute, int second)
    {
        var parsed = DiagFileNameDate.TryParseUtc(name, fileNameTimesAreUtc: true);

        Assert.Equal(new DateTime(year, month, day, hour, minute, second, DateTimeKind.Utc), parsed);
    }

    [Fact]
    public void TreatsTheDateAsMonthDayYear()
    {
        // 24 cannot be a month, so these names prove the order.
        var parsed = DiagFileNameDate.TryParseUtc("_10_24_2025 7-13-53 PM . AOR2728SupportFile.szip", true);

        Assert.Equal(10, parsed!.Value.Month);
        Assert.Equal(24, parsed.Value.Day);
    }

    [Theory]
    [InlineData("_1_1_2026 12-00-00 AM . x.szip", 0)]   // midnight
    [InlineData("_1_1_2026 12-30-00 PM . x.szip", 12)]  // noon
    [InlineData("_1_1_2026 11-59-59 PM . x.szip", 23)]
    public void HandlesMiddayAndMidnightCorrectly(string name, int expectedHour)
    {
        Assert.Equal(expectedHour, DiagFileNameDate.TryParseUtc(name, true)!.Value.Hour);
    }

    [Fact]
    public void ConvertsFromLocalTimeWhenTheNameIsNotUtc()
    {
        var asUtc = DiagFileNameDate.TryParseUtc("_7_27_2026 9-53-10 PM . x.szip", fileNameTimesAreUtc: true);
        var asLocal = DiagFileNameDate.TryParseUtc("_7_27_2026 9-53-10 PM . x.szip", fileNameTimesAreUtc: false);

        Assert.Equal(DateTimeKind.Utc, asLocal!.Value.Kind);

        var expected = new DateTime(2026, 7, 27, 21, 53, 10, DateTimeKind.Local).ToUniversalTime();
        Assert.Equal(expected, asLocal);

        // Only identical where the machine runs on UTC.
        if (TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 7, 27)) != TimeSpan.Zero)
        {
            Assert.NotEqual(asUtc, asLocal);
        }
    }

    [Fact]
    public void WorksOnAFullPathNotJustAName()
    {
        var parsed = DiagFileNameDate.TryParseUtc(@"C:\drop\_7_27_2026 9-53-10 PM . M21461SupportFile.szip", true);

        Assert.Equal(new DateTime(2026, 7, 27, 21, 53, 10, DateTimeKind.Utc), parsed);
    }

    [Theory]
    [InlineData("SupportFile.szip")]
    [InlineData("no date here.szip")]
    [InlineData("_13_45_2026 9-53-10 PM . x.szip")]   // month 13, day 45
    [InlineData("_2_30_2026 9-53-10 AM . x.szip")]    // 30 February
    public void ReturnsNothingWhenThereIsNoUsableDate(string name)
    {
        Assert.Null(DiagFileNameDate.TryParseUtc(name, true));
    }

    [Fact]
    public void AcceptsATwentyFourHourNameWithNoAmPm()
    {
        var parsed = DiagFileNameDate.TryParseUtc("_7_27_2026 21-53-10 . x.szip", true);

        Assert.Equal(21, parsed!.Value.Hour);
    }

    [Fact]
    public void FallsBackToTheFileDateWhenTheNameHasNone()
    {
        var dir = Path.Combine(Path.GetTempPath(), "diagname", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        try
        {
            var path = Path.Combine(dir, "nodate.szip");
            File.WriteAllText(path, "x");

            var arrived = DiagFileNameDate.ArrivedUtc(path, true);

            Assert.True((DateTime.UtcNow - arrived).Duration() < TimeSpan.FromMinutes(5));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void PrefersTheNameOverTheFileDate()
    {
        var dir = Path.Combine(Path.GetTempPath(), "diagname", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        try
        {
            var path = Path.Combine(dir, "_7_27_2026 9-53-10 PM . M1SupportFile.szip");
            File.WriteAllText(path, "x");

            Assert.Equal(new DateTime(2026, 7, 27, 21, 53, 10, DateTimeKind.Utc),
                DiagFileNameDate.ArrivedUtc(path, true));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}

/// <summary>
/// Dating a bundle whose name carries no timestamp. Raised from the field: a brand new
/// AOR1613SupportFiles.szip showed an arrival date of 2025-11-03, which put it outside the
/// "only files newer than" window and would have had it dropped without being read.
/// </summary>
public class BundleArrivalDateTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "arrivaltests", Guid.NewGuid().ToString("N"));

    public BundleArrivalDateTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch { /* best effort */ }
    }

    private string MakeZip(string name, params (string Entry, DateTime Written)[] entries)
    {
        var path = Path.Combine(_folder, name);

        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            foreach (var (entryName, written) in entries)
            {
                var entry = archive.CreateEntry(entryName);
                entry.LastWriteTime = new DateTimeOffset(written, TimeSpan.Zero);
                using var stream = entry.Open();
                stream.WriteByte(1);
            }
        }

        return path;
    }

    [Fact]
    public void TakesTheNewestFileInsideTheBundleWhenTheNameHasNoDate()
    {
        // A bundle carries machine configuration from years back alongside logs written seconds
        // ago. It is the newest that says when the export was taken.
        var path = MakeZip("AOR1613SupportFiles.szip",
            ("FastFramer.xml", new DateTime(2017, 11, 3, 7, 37, 0)),
            ("Logs/Change.log", new DateTime(2024, 9, 5, 6, 56, 0)),
            ("Logs/MachineLog.txt", new DateTime(2026, 9, 16, 13, 9, 44)));

        Assert.Equal(new DateTime(2026, 9, 16, 13, 9, 44), DiagFileNameDate.ArrivedUtc(path, true));
    }

    [Fact]
    public void ANameWithADateStillWinsOverTheZipContents()
    {
        var path = MakeZip("_7_27_2026 10-15-10 PM . M20716SupportFile.szip",
            ("Logs/MachineLog.txt", new DateTime(2026, 7, 28, 10, 15, 10)));

        Assert.Equal(new DateTime(2026, 7, 27, 22, 15, 10), DiagFileNameDate.ArrivedUtc(path, true));
    }

    [Fact]
    public void TheCreationTimeOnDiskIsNeverUsed()
    {
        // Windows restores the original creation time when a file of the same name is replaced in
        // the same folder, so saving the same attachment twice leaves the second one stamped with
        // the date of the first. That is where 2025-11-03 came from.
        var path = MakeZip("AOR1613SupportFiles.szip",
            ("Logs/MachineLog.txt", new DateTime(2026, 9, 16, 13, 9, 44)));

        File.SetCreationTimeUtc(path, new DateTime(2025, 11, 3, 9, 0, 0));

        Assert.Equal(new DateTime(2026, 9, 16, 13, 9, 44), DiagFileNameDate.ArrivedUtc(path, true));
    }

    [Fact]
    public void FallsBackToTheLastWriteTimeWhenTheZipCannotBeRead()
    {
        var path = Path.Combine(_folder, "NotReallyAZip.szip");
        File.WriteAllText(path, "this is not a zip");
        File.SetLastWriteTimeUtc(path, new DateTime(2026, 9, 16, 1, 28, 35));

        Assert.Null(DiagFileNameDate.NewestEntryUtc(path, true));
        Assert.Equal(new DateTime(2026, 9, 16, 1, 28, 35), DiagFileNameDate.ArrivedUtc(path, true));
    }

    [Fact]
    public void ADosZeroTimestampIsNotTreatedAsADate()
    {
        // 1980-01-01 is the zero value of a DOS timestamp - it dates nothing.
        var path = MakeZip("AOR1613SupportFiles.szip",
            ("empty.txt", new DateTime(1980, 1, 1, 0, 0, 0)));

        Assert.Null(DiagFileNameDate.NewestEntryUtc(path, true));
    }

    [Fact]
    public void AnEntryFromTheFutureIsIgnored()
    {
        // A machine with its clock wrong must not stamp a bundle years ahead, or it outranks
        // every real bundle in the list forever.
        var path = MakeZip("AOR1613SupportFiles.szip",
            ("Logs/MachineLog.txt", new DateTime(2026, 9, 16, 13, 9, 44)),
            ("Logs/ErrLog.txt", DateTime.UtcNow.AddYears(5)));

        Assert.Equal(new DateTime(2026, 9, 16, 13, 9, 44), DiagFileNameDate.NewestEntryUtc(path, true));
    }

    [Fact]
    public void ABundleDatedFromInsideIsNotDroppedAsTooOld()
    {
        // The real consequence: with a 100 day window, a wrongly-aged bundle is skipped by the
        // folder monitor and never read at all.
        var path = MakeZip("AOR1613SupportFiles.szip",
            ("Logs/MachineLog.txt", DateTime.UtcNow.AddHours(-2)));

        File.SetCreationTimeUtc(path, DateTime.UtcNow.AddDays(-300));

        Assert.True(DiagFileNameDate.ArrivedUtc(path, true) > DateTime.UtcNow.AddDays(-100));
    }
}
