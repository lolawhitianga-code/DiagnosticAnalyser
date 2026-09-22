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
    /// One machine runs more than one controller and they reuse node numbers. FixedSide's plate
    /// support down and the infeed's bay 1 prox are both node 1 address 1 - on different ports.
    /// Matching on node and address alone lays one on top of the other.
    /// </summary>
    [Fact]
    public void KeepsPointsOnDifferentPortsApart()
    {
        var config = Read();

        var plate = config.Signals.Single(s => s.Name == "PlateSupportDown");
        var bay = config.Signals.Single(s => s.Name == "Bay1Prox");

        Assert.Equal("192.168.250.1-1.1", plate.Address);
        Assert.Equal("TCP192.168.50.2:2-1.1", bay.Address);
        Assert.NotEqual(plate.Address, bay.Address);
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
