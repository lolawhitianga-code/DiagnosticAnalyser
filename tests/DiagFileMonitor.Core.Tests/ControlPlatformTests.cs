using DiagFileMonitor.Core.Knowledge;
using DiagFileMonitor.Core.SpidaLogs;

namespace DiagFileMonitor.Core.Tests;

public class ControlPlatformTests
{
    private static PlatformFinding Detect(params string[] lines) =>
        ControlPlatformCheck.Detect(MachineLogFile.Parse(lines));

    /// <summary>The shape seen on M20716, M21737 and M21844.</summary>
    [Fact]
    public void ReadsTheNetworkAddressedPlatform()
    {
        var found = Detect(
            "05:14:58.0680108,  OutputChange, IO-PlatePresentBypass,  Output (192.168.250.1-0.0) Set On",
            "05:14:58.2537957,  InputChange, EStop,  Input (192.168.250.1-4.0) Changed to 0",
            "05:14:57.8569101,  MotionEvent, Node0 Status,  Needs to be Homed");

        Assert.Equal(ControlPlatform.NetworkNodes, found.Platform);
        Assert.Contains("node status", found.Evidence, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The shape seen on AOR1694: COM-port addresses, and axes reporting under their own names
    /// rather than as numbered nodes.
    /// </summary>
    [Fact]
    public void ReadsTheSerialPortPlatform()
    {
        var found = Detect(
            "11:19:29.6787630,  OutputChange, LowerGunFire,  Output (COM7-6.5) Set On",
            "11:19:29.7000000,  InputChange, StudPinDown,  Input (COM7-2.3) Changed to 1",
            "11:19:30.0000000,  MotionEvent, TrolleyHeight Status,  OK");

        Assert.Equal(ControlPlatform.SerialPort, found.Platform);
        Assert.True(found.Known);
    }

    /// <summary>
    /// A log with no I/O in it says nothing about the platform, and must say so rather than
    /// defaulting to whichever one we have mapped.
    /// </summary>
    [Fact]
    public void SaysSoWhenTheLogCannotTell()
    {
        var found = Detect(
            "09:00:00.0000000,  Other, WallExtruderStep,  Step = 10",
            "09:00:01.0000000,  Other, Eject,  Panel Complete");

        Assert.Equal(ControlPlatform.Unknown, found.Platform);
        Assert.False(found.Known);
        Assert.Contains("no I/O addresses", found.Evidence);
    }

    [Theory]
    [InlineData("192.168.250.1-4.2", ControlPlatform.NetworkNodes)]
    [InlineData("COM7-6.5", ControlPlatform.SerialPort)]
    [InlineData("com12-0.1", ControlPlatform.SerialPort)]
    [InlineData("something-else", ControlPlatform.Unknown)]
    public void ReadsThePlatformOffAnAddress(string address, ControlPlatform expected) =>
        Assert.Equal(expected, ControlPlatformCheck.OfAddress(address));
}

public class TcpPlatformTests
{
    /// <summary>
    /// M17311, a Tornado M500 that support named as the CLX build. Addresses carry a TCP prefix
    /// and axes report under their own names, which is neither of the other two shapes.
    /// </summary>
    [Fact]
    public void ReadsTheTcpAddressedPlatform()
    {
        var found = ControlPlatformCheck.Detect(MachineLogFile.Parse(new[]
        {
            "08:45:26.9052195,  OutputChange, IO-InfeedDriveSideClamp1Out,  Output (TCP192.168.50.2-3.17) Set On",
            "08:45:26.9052195,  InputChange, FollowerDown,  Input (TCP192.168.50.2-15.5) Changed to 1",
            "08:45:30.8522097,  MotionEvent, Axis-InfeedFollower,  Homing"
        }));

        Assert.Equal(ControlPlatform.TcpAddressed, found.Platform);
        Assert.Equal(ControlPlatform.TcpAddressed, ControlPlatformCheck.OfAddress("TCP192.168.50.2-3.17"));
    }

    /// <summary>
    /// A TCP address contains a bare IP, so it must not be mistaken for the network-addressed
    /// platform - which would hand a Tornado the Raked Extruder's map.
    /// </summary>
    [Fact]
    public void ATcpAddressIsNotReadAsABareNetworkAddress()
    {
        var found = ControlPlatformCheck.Detect(MachineLogFile.Parse(new[]
        {
            "08:45:26.0000000,  OutputChange, IO-Thing,  Output (TCP192.168.50.2-3.17) Set On",
            "08:45:27.0000000,  OutputChange, IO-Thing2,  Output (TCP192.168.50.2-3.18) Set On"
        }));

        Assert.NotEqual(ControlPlatform.NetworkNodes, found.Platform);
        Assert.Empty(MachineIoMap.For("RakingWallExtruderV3DG", ControlPlatform.TcpAddressed));
    }
}
