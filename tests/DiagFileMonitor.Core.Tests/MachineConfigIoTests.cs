using System.Text;
using DiagFileMonitor.Core.Knowledge;
using DiagFileMonitor.Core.SpidaLogs;

namespace DiagFileMonitor.Core.Tests;

public class MachineConfigIoTests
{
    /// <summary>The real shape, trimmed: two ports reusing node numbers, and axes alongside I/O.</summary>
    private const string Config = """
        <?xml version="1.0" encoding="utf-16"?>
        <WallExtruder>
          <StickGunsFitted>true</StickGunsFitted>
          <MajorSubFitted>false</MajorSubFitted>
          <ControlBox2Fitted>true</ControlBox2Fitted>
          <CommonIO>
            <Inputs>
              <RackLockOff>
                <InUse>true</InUse><Simulate>false</Simulate><Port>192.168.250.1</Port>
                <NodeNum>2</NodeNum><Address>9</Address><Inverted>false</Inverted>
              </RackLockOff>
              <ClampClearProx>
                <InUse>false</InUse><Simulate>false</Simulate><Port></Port>
                <NodeNum>0</NodeNum><Address>0</Address><Inverted>false</Inverted>
              </ClampClearProx>
            </Inputs>
            <Outputs>
              <SideClamp>
                <InUse>true</InUse><Simulate>false</Simulate><Port>192.168.250.1</Port>
                <NodeNum>1</NodeNum><Address>5</Address><Inverted>true</Inverted>
              </SideClamp>
            </Outputs>
          </CommonIO>
          <FixedSide>
            <Inputs>
              <PlateSupportDown>
                <InUse>true</InUse><Simulate>false</Simulate><Port>192.168.250.1</Port>
                <NodeNum>1</NodeNum><Address>1</Address><Inverted>false</Inverted>
              </PlateSupportDown>
            </Inputs>
            <Trolley><Axis>
              <NodeNum>0</NodeNum><Port>192.168.250.1</Port><Velocity>1000</Velocity>
              <Accel>300</Accel><Scale>420.431</Scale>
            </Axis></Trolley>
            <ServoGuns><ServoGun><Axis>
              <NodeNum>4</NodeNum><Port>TCP192.168.50.2</Port><Velocity>8000000</Velocity>
              <Accel>1000000</Accel><Scale>1</Scale>
            </Axis></ServoGun></ServoGuns>
          </FixedSide>
          <MajorSubInfeed>
            <Inputs>
              <Bay1Prox>
                <InUse>true</InUse><Simulate>false</Simulate><Port>TCP192.168.50.2:2</Port>
                <NodeNum>1</NodeNum><Address>1</Address><Inverted>false</Inverted>
              </Bay1Prox>
            </Inputs>
          </MajorSubInfeed>
          <TrolleyHeight><Axis>
            <NodeNum>4</NodeNum><Port>192.168.250.1</Port><Velocity>175</Velocity>
            <Accel>100</Accel><Scale>1</Scale>
          </Axis></TrolleyHeight>
        </WallExtruder>
        """;

    private static MachineConfig Read() => MachineConfigIo.Parse(Config);

    [Fact]
    public void ReadsTheNameSideAndAddress()
    {
        var rack = Read().Signals.Single(s => s.Name == "RackLockOff");

        Assert.Equal(SignalKind.Input, rack.Kind);
        Assert.Equal(MachineSide.Shared, rack.Side);
        Assert.Equal("192.168.250.1-2.9", rack.Address);
        Assert.True(rack.Fitted);
    }

    /// <summary>
    /// The thing no log can give. A log records changes, so a sensor that never came on and one
    /// that is not fitted look identical - the gap that cost us the M21737 case.
    /// </summary>
    [Fact]
    public void SaysWhatIsNotFittedAtAll()
    {
        var config = Read();

        Assert.False(config.Signals.Single(s => s.Name == "ClampClearProx").Fitted);
        Assert.DoesNotContain(config.Fitted, s => s.Name == "ClampClearProx");
    }

    [Fact]
    public void ReadsTheSideOffThePath()
    {
        var config = Read();

        Assert.Equal(MachineSide.FixedSide, config.Signals.Single(s => s.Name == "PlateSupportDown").Side);
        Assert.Equal(MachineSide.Shared, config.Signals.Single(s => s.Name == "SideClamp").Side);
    }

    [Fact]
    public void KeepsTheInvertedFlag() =>
        Assert.True(Read().Signals.Single(s => s.Name == "SideClamp").Inverted);

    /// <summary>
    /// Definitions reuse node numbers across controllers. FixedSide's plate support down and the
    /// infeed's bay 1 prox are both node 1 address 1, on different ports. Matching on node and
    /// address alone lays one on top of the other.
    /// </summary>
    [Fact]
    public void KeepsPointsOnDifferentPortsApart()
    {
        var config = Read();

        var plate = config.Signals.Single(s => s.Name == "PlateSupportDown");
        var bay = config.Signals.Single(s => s.Name == "Bay1Prox");

        Assert.Equal("192.168.250.1-1.1", plate.Address);
        Assert.Equal("TCP192.168.50.2:2-1.1", bay.Address);
    }

    /// <summary>
    /// The file defines every option whether the machine has it or not, so InUse on its own is
    /// not "this machine has one". M21844 carries 21 MajorSubInfeed points all marked InUse and
    /// has no infeed - MajorSubFitted is false. Reading InUse alone invents a whole subsystem,
    /// and with it a second controller the machine does not have.
    /// </summary>
    [Fact]
    public void AnOptionThatIsNotFittedIsNotOnTheMachine()
    {
        var config = Read();

        var bay = config.Signals.Single(s => s.Name == "Bay1Prox");

        Assert.True(bay.Fitted);
        Assert.False(bay.SubsystemFitted);
        Assert.False(bay.OnThisMachine);
        Assert.DoesNotContain(config.Fitted, s => s.Name == "Bay1Prox");
        Assert.Equal(1, config.DefinedButNotOnThisMachine);
    }

    [Fact]
    public void ReadsTheOptionFlags()
    {
        var config = Read();

        Assert.False(config.Subsystems["MajorSub"]);
        Assert.True(config.Subsystems["ControlBox2"]);
        Assert.True(config.Subsystems["StickGuns"]);
    }

    /// <summary>Most of the machine has no option flag, and must not be gated away.</summary>
    [Fact]
    public void AnythingWithNoOptionFlagStaysOnTheMachine()
    {
        var config = Read();

        Assert.Contains(config.Fitted, s => s.Name == "PlateSupportDown");
        Assert.Contains(config.Fitted, s => s.Name == "RackLockOff");
    }

    /// <summary>
    /// Node 4 is TrolleyHeight on the Omron main and the fixed side servo guns on a CLX. The
    /// log's NodeN Status lines come from the main, so that is what a bare node number means.
    /// </summary>
    [Fact]
    public void ResolvesANodeAgainstTheRightController()
    {
        var config = Read();

        Assert.Equal("192.168.250.1", config.MainPort);
        Assert.Equal("TrolleyHeight", config.AxisOnNode(4));
        Assert.Equal("FixedSide ServoGuns", config.AxisOnNode(4, "TCP192.168.50.2"));
        Assert.Equal("FixedSide Trolley", config.AxisOnNode(0));
    }

    /// <summary>These files are UTF-16. Read as UTF-8 they parse as an empty document.</summary>
    [Fact]
    public void ReadsAUtf16FileFromDisk()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".xml");
        File.WriteAllText(path, Config, new UnicodeEncoding(false, true));

        try
        {
            Assert.True(MachineConfigIo.Read(path).Any);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void AMissingFileIsNotAnError() =>
        Assert.False(MachineConfigIo.Read(Path.Combine(Path.GetTempPath(), "nope.xml")).Any);
}

public class ConfigPlatformTests
{
    private static string Machine(string body) =>
        $"<?xml version=\"1.0\" encoding=\"utf-16\"?><WallExtruder>{body}</WallExtruder>";

    private static string Point(string section, string name, string port, string node, string addr) =>
        $"<{section}><Inputs><{name}><InUse>true</InUse><Port>{port}</Port>"
        + $"<NodeNum>{node}</NodeNum><Address>{addr}</Address></{name}></Inputs></{section}>";

    /// <summary>Every real machine here sits on one platform, and that is the expected shape.</summary>
    [Fact]
    public void OnePlatformIsNormal()
    {
        var config = MachineConfigIo.Parse(Machine(
            Point("CommonIO", "RackLockOff", "192.168.250.1", "2", "9")
            + Point("FixedSide", "PlateSupportDown", "192.168.250.1", "1", "1")));

        Assert.Single(config.PlatformsUsed);
        Assert.False(config.PlatformLooksWrong);
    }

    /// <summary>
    /// The one mix Spida allow: a CLX printer on an Omron machine. Expected, so it must not read
    /// as a problem.
    /// </summary>
    [Fact]
    public void AClxPrinterOnAnOmronMachineIsExpected()
    {
        var config = MachineConfigIo.Parse(Machine(
            Point("CommonIO", "RackLockOff", "192.168.250.1", "2", "9")
            + Point("FixedSide", "PlateSupportDown", "192.168.250.1", "1", "1")
            + Point("TimPrinter", "PrinterReady", "TCP192.168.50.9", "0", "1")));

        Assert.Equal(2, config.PlatformsUsed.Count);
        Assert.Empty(config.OffPlatformNonPrinters);
        Assert.False(config.PlatformLooksWrong);
    }

    /// <summary>
    /// Anything else on a second platform means the reading is wrong. Ignoring the option flags
    /// made an M21844 look like this - an Omron machine apparently running CLX subsystems it does
    /// not have.
    /// </summary>
    [Fact]
    public void AnythingElseOnASecondPlatformIsFlagged()
    {
        var config = MachineConfigIo.Parse(Machine(
            Point("CommonIO", "RackLockOff", "192.168.250.1", "2", "9")
            + Point("FixedSide", "PlateSupportDown", "192.168.250.1", "1", "1")
            + Point("MajorSubInfeed", "Bay1Prox", "TCP192.168.50.2:2", "1", "1")));

        Assert.True(config.PlatformLooksWrong);
        Assert.Single(config.OffPlatformNonPrinters);
    }
}
