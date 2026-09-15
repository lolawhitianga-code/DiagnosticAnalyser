namespace DiagFileMonitor.Core.Models;

/// <summary>
/// One ProdLogV2 weekly file that has been read in. Kept so the same week is not counted twice -
/// exports routinely carry a duplicate copy of recent weeks in a second folder.
/// </summary>
public class ProductionLogFile
{
    public int Id { get; set; }

    public string SerialNumber { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;

    /// <summary>ISO year and week from the file name, which is how a duplicate week is spotted.</summary>
    public int Year { get; set; }
    public int Week { get; set; }

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

    public double FastenerCount { get; set; }
    public int MembersAssembled { get; set; }
    public double Cube { get; set; }
    public double Lineal { get; set; }
    public double BuildMinutes { get; set; }
    public double IdleMinutes { get; set; }
    public double Junctions { get; set; }
    public bool BuildTimeImplausible { get; set; }
}
