using DiagFileMonitor.Core.Knowledge;
using DiagFileMonitor.Core.SpidaLogs;

namespace DiagFileMonitor.Core.Tests;

public class PlcTagImportTests
{
    /// <summary>
    /// The shape a Studio 5000 controller export has: tags as elements, the useful ones aliased
    /// onto a module point, descriptions in a child element.
    /// </summary>
    private const string L5X = """
        <?xml version="1.0" encoding="UTF-8"?>
        <RSLogix5000Content SchemaRevision="1.0">
          <Controller Name="WallExtruderDG">
            <Tags>
              <Tag Name="IO-PlateClampFixedSide" TagType="Alias" AliasFor="Local:2:O.Data.3">
                <Description>Plate clamp, fixed side</Description>
              </Tag>
              <Tag Name="IO-PlateClampFloatingSide" TagType="Alias" AliasFor="Local:2:O.Data.4">
                <Description>Plate clamp, floating side</Description>
              </Tag>
              <Tag Name="PlateClampUpFixedSide" TagType="Alias" AliasFor="Local:1:I.Data.10" />
              <Tag Name="PlateClampUpFloatingSide" TagType="Alias" AliasFor="Local:1:I.Data.11" />
              <Tag Name="IO-RackLock" TagType="Alias" AliasFor="Local:2:O.Data.1" />
              <Tag Name="_scratch" TagType="Base" />
            </Tags>
          </Controller>
        </RSLogix5000Content>
        """;

    [Fact]
    public void ReadsAStudio5000Export()
    {
        var result = PlcTagImport.ReadL5X(L5X);

        Assert.Equal("L5X", result.Format);
        Assert.Equal(3, result.Outputs);
        Assert.Equal(2, result.Inputs);
        Assert.DoesNotContain(result.Tags, t => t.Name.StartsWith("_"));

        var clamp = result.Tags.Single(t => t.Name == "IO-PlateClampFixedSide");
        Assert.Equal(SignalKind.Output, clamp.Kind);
        Assert.Equal("2.3", clamp.Point);
        Assert.Equal("Plate clamp, fixed side", clamp.Comment);
    }

    /// <summary>
    /// A Sysmac variable export: a preamble above the real header, the physical point in an "AT"
    /// column, and commas inside quoted comments.
    /// </summary>
    private const string SysmacCsv = """
        Sysmac Studio Variable List
        Project,WallExtruderDG

        Name,Data Type,Initial Value,AT,Retain,Comment
        IO-StudPinUpFixed,BOOL,FALSE,IOBus://rack#0/slot#2/Out_Bit_00.13,FALSE,"Stud pin up, fixed"
        IO-StudPinUpFloating,BOOL,FALSE,IOBus://rack#0/slot#2/Out_Bit_01.8,FALSE,"Stud pin up, floating"
        StudPinDownFixed,BOOL,FALSE,IOBus://rack#0/slot#1/In_Bit_00.16,FALSE,
        StudPinDownFloating,BOOL,FALSE,IOBus://rack#0/slot#1/In_Bit_01.14,FALSE,
        CycleCount,DINT,0,,TRUE,"Not an I/O point, has no AT"
        """;

    [Fact]
    public void ReadsASysmacExportWithItsPreambleAndQuotedCommas()
    {
        var result = PlcTagImport.ReadDelimited(SysmacCsv);

        Assert.Equal(2, result.Outputs);
        Assert.Equal(3, result.Inputs);

        var pin = result.Tags.Single(t => t.Name == "IO-StudPinUpFixed");
        Assert.Equal("0.13", pin.Point);
        Assert.Equal("Stud pin up, fixed", pin.Comment);
    }

    /// <summary>Tab-separated exports are common too, and the header is not always line one.</summary>
    [Fact]
    public void FindsTheHeaderWhereverItIs()
    {
        var result = PlcTagImport.ReadDelimited(
            "Exported 2026-09-22\n\nTag Name\tDescription\nIO-SideClamp\tShared side clamp\n");

        var tag = Assert.Single(result.Tags);
        Assert.Equal("IO-SideClamp", tag.Name);
        Assert.Equal("Shared side clamp", tag.Comment);
    }

    /// <summary>
    /// An export with no header naming a tag column cannot be read, and says so rather than
    /// inventing a column.
    /// </summary>
    [Fact]
    public void SaysSoWhenItCannotFindAHeader()
    {
        var result = PlcTagImport.ReadDelimited("1,2,3\n4,5,6\n");

        Assert.Empty(result.Tags);
        Assert.Contains(result.Notes, n => n.Contains("header", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// No point column is fine. The names are the deliverable; the numbers come off each
    /// machine's own log, because machines of one model are not numbered alike.
    /// </summary>
    [Fact]
    public void AnExportWithNoAddressesStillGivesTheNames()
    {
        var result = PlcTagImport.ReadDelimited("Name,Data Type\nIO-TopStudClamp,BOOL\nTopStudClampUp,BOOL\n");

        Assert.Equal(2, result.Tags.Count);
        Assert.All(result.Tags, t => Assert.Equal(string.Empty, t.Point));
        Assert.Contains(result.Notes, n => n.Contains("read off each machine", StringComparison.OrdinalIgnoreCase));
    }
}

public class ModelListBuilderTests
{
    private static IReadOnlyList<KnownSignal> Build(string csv) =>
        ModelListBuilder.Build(PlcTagImport.ReadDelimited(csv).Tags);

    /// <summary>
    /// The PLC names the two sides separately and the log writes both as one name, so they fold
    /// back into one point fitted twice - and the side the PLC states comes with them, which is
    /// exactly what took a duty-time fingerprint to work out from logs alone.
    /// </summary>
    [Fact]
    public void FoldsTheTwoSidesBackIntoOneNamedPoint()
    {
        var built = Build("""
            Name,AT
            IO-PlateClampFixedSide,Out_Bit_04.4
            IO-PlateClampFloatingSide,Out_Bit_04.5
            """);

        var clamp = Assert.Single(built);

        Assert.Equal("IO-PlateClamp", clamp.Name);
        Assert.Equal(SignalKind.Output, clamp.Instances == 2 ? SignalKind.Output : SignalKind.Input);
        Assert.Equal(2, clamp.Instances);
        Assert.Equal(new[] { "4.4", "4.5" }, clamp.PointsSeen);
        Assert.Equal(MachineSide.FixedSide, clamp.SideAtPoint("4.4"));
        Assert.Equal(MachineSide.FloatingSide, clamp.SideAtPoint("4.5"));
    }

    [Fact]
    public void KeepsASharedPointAsOne()
    {
        var built = Build("Name,AT\nIO-RackLockCommonIO,Out_Bit_00.1\n");

        var rack = Assert.Single(built);
        Assert.Equal(1, rack.Instances);
        Assert.Equal(MachineSide.Shared, rack.SideAtPoint("0.1"));
    }

    /// <summary>
    /// An output keeps the IO- prefix the logs use, so a name from the PLC and a name from a log
    /// match without anyone having to remember which is which.
    /// </summary>
    [Fact]
    public void OutputsComeOutNamedTheWayTheLogWritesThem()
    {
        var built = Build("Name\nIO-TopStudClampFixed\nIO-TopStudClampFloating\nTopStudClampUpFixed\n");

        Assert.Contains(built, p => p.Kind == SignalKind.Output && p.Name == "IO-TopStudClamp");
        Assert.Contains(built, p => p.Kind == SignalKind.Input && p.Name == "TopStudClampUp");
    }

    /// <summary>
    /// StudPinUp2 is a second pin, not the second side of StudPinUp, and the Raked Wall Extruder
    /// V3 has both. A trailing number is therefore never treated as a side. If a PLC numbers its
    /// sides rather than naming them this under-folds, which somebody will spot - the other way
    /// round loses a point silently.
    /// </summary>
    [Fact]
    public void DoesNotFoldAGenuinelySecondMechanism()
    {
        var built = Build("Name\nIO-StudPinUpFixed\nIO-StudPinUpFloating\nIO-StudPinUp2Fixed\nIO-StudPinUp2Floating\n");

        Assert.Equal(2, built.Count);
        Assert.Contains(built, p => p.Name == "IO-StudPinUp" && p.Instances == 2);
        Assert.Contains(built, p => p.Name == "IO-StudPinUp2" && p.Instances == 2);
    }

    [Fact]
    public void WritesTheListAsPastableCSharp()
    {
        var csharp = ModelListBuilder.ToCSharp(Build("Name,AT\nIO-RackLockCommonIO,Out_Bit_00.1\n"));

        Assert.Contains("new(SignalKind.Output, \"IO-RackLock\", 1, new[] { \"0.1\" }", csharp);
        Assert.Contains("MachineSide.Shared", csharp);
    }
}

public class WallSheatherMapTests
{
    /// <summary>
    /// The Wall Sheather is why the list had to be names rather than addresses. Three bridge gun
    /// gantries and two saws mean most things are fitted three or five times, not twice, so 24
    /// named points cover 67 numbers.
    /// </summary>
    [Fact]
    public void KnowsTheWallSheatherByName()
    {
        var points = MachineIoMap.For("WallSheather");

        Assert.Equal(24, points.Count);
        Assert.Equal(67, points.Sum(p => p.PointsSeen.Count));

        var fire = MachineIoMap.Find("WallSheather", SignalKind.Output, "IO-GunFire");
        Assert.Equal(5, fire!.Instances);

        var gunUp = MachineIoMap.Find("WallSheather", SignalKind.Input, "GunUp");
        Assert.Equal(5, gunUp!.Instances);
    }

    [Fact]
    public void TheWallSheatherAndTheExtruderAreSeparateLists()
    {
        Assert.NotEqual(
            MachineIoMap.For("WallSheather").Count,
            MachineIoMap.For("RakingWallExtruderV3DG").Count);

        Assert.Null(MachineIoMap.Find("WallSheather", SignalKind.Output, "IO-PlateClamp"));
    }
}
