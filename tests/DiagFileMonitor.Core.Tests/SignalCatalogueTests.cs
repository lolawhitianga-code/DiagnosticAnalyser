using DiagFileMonitor.Core.Services;
using DiagFileMonitor.Core.SpidaLogs;

namespace DiagFileMonitor.Core.Tests;

public class SignalCatalogueTests : IDisposable
{
    private readonly TestEnvironment _env = new();

    public void Dispose() => _env.Dispose();

    private static IoTimeline Timeline(params string[] lines) =>
        IoTimeline.Build(MachineLogFile.Parse(lines));

    private const string GunOn = "07:53:30.1000000,  OutputChange, LowerGunFire,  Output (COM7-6.5) Set On";
    private const string GunOff = "07:53:30.4000000,  OutputChange, LowerGunFire,  Output (COM7-6.5) Set Off";
    private const string ClampOn = "07:53:34.8000000,  OutputChange, TopStudClamp,  Output (COM7-6.7) Set On";
    private const string PinDown = "07:53:31.3000000,  InputChange, StudPinDown,  Input (COM7-2.3) Changed to 1";

    [Fact]
    public async Task LearnsEveryPointALogMoves()
    {
        var result = await _env.Signals.RecordAsync("M21737", "RakedExtruder",
            Timeline(GunOn, GunOff, ClampOn, PinDown));

        Assert.Equal(3, result.PointsSeen);
        Assert.Equal(3, result.PointsNew);

        var known = await _env.Signals.ForSerialAsync("M21737");

        Assert.Equal(3, known.Count);
        Assert.Equal(2, known.Count(s => s.Kind == "Output"));
        Assert.Equal("RakedExtruder", known[0].MachineType);
    }

    [Fact]
    public async Task SeeingTheSameBundleAgainUpdatesCountsRatherThanDoublingTheList()
    {
        await _env.Signals.RecordAsync("M21737", "RakedExtruder", Timeline(GunOn, GunOff));
        await _env.Signals.RecordAsync("M21737", "RakedExtruder", Timeline(GunOn, GunOff));

        var gun = Assert.Single(await _env.Signals.ForSerialAsync("M21737"));

        Assert.Equal(2, gun.BundlesSeenIn);
        Assert.Equal(4, gun.TotalChanges);
    }

    [Fact]
    public async Task OneNameOnTwoAddressesIsTwoEntries()
    {
        // The two gun coils, one per side. Collapsing them would hide one of them for good.
        await _env.Signals.RecordAsync("M21737", "RakedExtruder", Timeline(
            GunOn,
            "07:53:30.1000000,  OutputChange, LowerGunFire,  Output (COM7-6.2) Set On"));

        var known = await _env.Signals.ForSerialAsync("M21737");

        Assert.Equal(2, known.Count);
        Assert.Equal(new[] { "COM7-6.2", "COM7-6.5" }, known.Select(s => s.Address).OrderBy(a => a));
    }

    [Fact]
    public async Task EachMachineKeepsItsOwnList()
    {
        await _env.Signals.RecordAsync("M21737", "RakedExtruder", Timeline(GunOn));
        await _env.Signals.RecordAsync("M20716", "Saw", Timeline(ClampOn));

        Assert.Equal("LowerGunFire", Assert.Single(await _env.Signals.ForSerialAsync("M21737")).Name);
        Assert.Equal("TopStudClamp", Assert.Single(await _env.Signals.ForSerialAsync("M20716")).Name);
    }

    [Fact]
    public async Task SaysWhichKnownPointsALogNeverTouches()
    {
        // The half a single log cannot tell you: a point that moves in every other bundle and
        // sits still in this one.
        await _env.Signals.RecordAsync("M21737", "RakedExtruder", Timeline(GunOn, GunOff, ClampOn, PinDown));

        var quiet = await _env.Signals.NotInThisLogAsync("M21737", Timeline(ClampOn));

        Assert.Equal(2, quiet.Count);
        Assert.Contains(quiet, s => s.Name == "LowerGunFire");
        Assert.Contains(quiet, s => s.Name == "StudPinDown");
    }

    [Fact]
    public async Task ALogWithNoIoInItTeachesNothingAndDoesNotThrow()
    {
        var result = await _env.Signals.RecordAsync("M21737", "RakedExtruder",
            Timeline("07:53:38.9085106,  Other, WallExtruderStep,  Step = 0"));

        Assert.Equal(0, result.PointsSeen);
        Assert.Empty(await _env.Signals.ForSerialAsync("M21737"));
    }

    [Fact]
    public async Task ABundleWithNoSerialIsNotFiledUnderNothing()
    {
        Assert.Equal(0, (await _env.Signals.RecordAsync("", "", Timeline(GunOn))).PointsSeen);
        Assert.Empty(await _env.Signals.MachinesAsync());
    }

    [Fact]
    public async Task ProcessingABundleLearnsItsIo()
    {
        // End to end: the catalogue fills itself as bundles come in, without anybody asking.
        var zip = _env.CreateZip("M21737SupportFile.zip", new Dictionary<string, string>
        {
            ["Machine.xml"] = "<Machine><SerialNumber>M21737</SerialNumber>"
                              + "<MachineType>RakedExtruder</MachineType></Machine>",
            ["Logs/MachineLog.txt"] = string.Join(Environment.NewLine, GunOn, GunOff, ClampOn, PinDown)
        });

        await _env.Processor.ProcessAsync(zip);

        var known = await _env.Signals.ForSerialAsync("M21737");

        Assert.Equal(3, known.Count);
        Assert.Contains(known, s => s is { Kind: "Output", Name: "TopStudClamp", Address: "COM7-6.7" });
        Assert.Contains(known, s => s is { Kind: "Input", Name: "StudPinDown" });
    }

    [Fact]
    public async Task ABundleWithNoMachineLogIsNotAFailure()
    {
        // Production figures and I/O are both a bonus. Neither may cost the user the import.
        var zip = _env.CreateZip("M21737SupportFile.zip", new Dictionary<string, string>
        {
            ["Machine.xml"] = "<Machine><SerialNumber>M21737</SerialNumber></Machine>"
        });

        var stored = await _env.Processor.ProcessAsync(zip);

        Assert.NotNull(stored);
        Assert.Empty(await _env.Signals.ForSerialAsync("M21737"));
    }
}
