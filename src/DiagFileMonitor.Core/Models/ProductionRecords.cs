namespace DiagFileMonitor.Core.Models;

/// <summary>Where a set of production panels was read from.</summary>
public static class ProductionSources
{
    /// <summary>A ProdLogV2&lt;year&gt;W&lt;week&gt;.log weekly export.</summary>
    public const string WeeklyLog = "WeeklyLog";

    /// <summary>Reports/LatestReport.txt inside a diagnostic .szip.</summary>
    public const string SupportBundle = "SupportBundle";
}

/// <summary>
/// One ProdLogV2 weekly file that has been read in. Kept so the same week is not counted twice -
/// exports routinely carry a duplicate copy of recent weeks in a second folder.
/// </summary>
public class ProductionLogFile
{
    public int Id { get; set; }

    public string SerialNumber { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;

    /// <summary>ISO year and week, from the file name where it has one, otherwise from the events.</summary>
    public int Year { get; set; }
    public int Week { get; set; }

    /// <summary>
    /// Where this came from: a weekly ProdLogV2 file, or the production report carried inside a
    /// diagnostic support bundle. A bundle's report covers only part of a week, so both can exist
    /// for the same week and the panels are de-duplicated rather than the files.
    /// </summary>
    public string Source { get; set; } = ProductionSources.WeeklyLog;

    /// <summary>The window the events actually cover, which for a bundle is a few days at most.</summary>
    public DateTime? CoversFromUtc { get; set; }
    public DateTime? CoversToUtc { get; set; }

    /// <summary>Panels already held from another source covering the same moment.</summary>
    public int PanelsSkippedAsDuplicate { get; set; }

    public DateTime ImportedAtUtc { get; set; }

    public int LinesRead { get; set; }
    public int ConsecutiveDuplicates { get; set; }
    public int MalformedLines { get; set; }
    public int PanelsStored { get; set; }

    /// <summary>Anything odd about the file, kept so a later report can say what it is built on.</summary>
    public string? Notes { get; set; }

    public List<ProductionPanel> Panels { get; set; } = new();
}

/// <summary>One panel, as classified out of the production log.</summary>
public class ProductionPanel
{
    public int Id { get; set; }

    public int ProductionLogFileId { get; set; }
    public ProductionLogFile? LogFile { get; set; }

    public string SerialNumber { get; set; } = string.Empty;

    /// <summary>Panel label from the log. Reused across jobs - not a unique identifier.</summary>
    public string Name { get; set; } = string.Empty;

    public DateTime EndedAt { get; set; }
    public DateTime? StartedAt { get; set; }

    /// <summary>Stored as text so the database stays readable by eye.</summary>
    public string Outcome { get; set; } = string.Empty;

    /// <summary>Panel, or Component for a Component Nailer. Rows stored before this are panels.</summary>
    public string Kind { get; set; } = "Panel";

    public double FastenerCount { get; set; }
    public int MembersAssembled { get; set; }
    public double Cube { get; set; }
    public double Lineal { get; set; }
    public double BuildMinutes { get; set; }
    public double IdleMinutes { get; set; }
    public double Junctions { get; set; }
    public bool BuildTimeImplausible { get; set; }
}
