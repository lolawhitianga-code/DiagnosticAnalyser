using DiagFileMonitor.Core.Knowledge;
using DiagFileMonitor.Core.SpidaLogs;

namespace DiagFileMonitor.Core.Tests;

public class GuardStopTests
{
    private static GuardLedger Ledger(params string[] lines)
    {
        var entries = MachineLogFile.Parse(lines);
        return GuardStopLedger.Check(entries, FloatingHeadCheck.Check(entries));
    }

    private static GuardStop Guard(GuardLedger ledger, string part) =>
        ledger.Guards.Single(g => g.Name.Contains(part, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The prompt is answered by a THNTD press, so the wait is the operator's response time and
    /// nothing else. On both real V3 logs the median came out under three seconds.
    /// </summary>
    [Fact]
    public void TimesTheMovingPartsPromptToTheButtonPress()
    {
        var ledger = Ledger(
            "05:38:54.0000000,  Other, WallExtruderPLC,  **Ensure You Are Clear Of Any Moving Parts** Press Clamp/Fire Buttons To Continue, Machine Will Move",
            "05:38:57.0000000,  InputChange, THNTD,  Input (192.168.250.1-4.1) Changed to 1",
            "05:38:57.2000000,  InputChange, THNTD,  Input (192.168.250.1-4.1) Changed to 0",
            "06:00:00.0000000,  Other, WallExtruderPLC,  **Ensure You Are Clear Of Any Moving Parts** Press Clamp/Fire Buttons To Continue, Machine Will Move",
            "06:00:05.0000000,  InputChange, THNTD,  Input (192.168.250.1-4.1) Changed to 1");

        var guard = Guard(ledger, "moving parts");

        Assert.Equal(2, guard.Count);
        Assert.Equal(TimeSpan.FromSeconds(8), guard.Total);
        Assert.Equal(TimeSpan.FromSeconds(5), guard.Longest);
    }

    /// <summary>
    /// Timed off the bar input rather than the message: the input says when the bar was actually
    /// let go. Every servo drops out on a press, so the machine is not going anywhere until the
    /// sequencer moves again.
    /// </summary>
    [Fact]
    public void TimesTheSafetyBarFromPressToTheMachineMovingAgain()
    {
        var ledger = Ledger(
            "06:19:49.0000000,  InputChange, SafetyBarPressed,  Input (192.168.250.1-4.8) Changed to 1",
            "06:19:49.1000000,  Other, WallExtruderPLC,  Floating Side Safety Bar Pressed - Press Estop Reset to Continue",
            "06:19:49.1000000,  MotionEvent, Node0 Status,  Servo Disabled",
            "06:19:53.0000000,  InputChange, EstopResetButton,  Input (192.168.250.1-4.9) Changed to 1",
            "06:19:55.0000000,  InputChange, SafetyBarPressed,  Input (192.168.250.1-4.8) Changed to 0",
            "06:19:56.0000000,  Other, WallExtruderStep,  Step = 1000");

        var guard = Guard(ledger, "safety bar");

        Assert.Equal(1, guard.Count);
        Assert.Equal(TimeSpan.FromSeconds(7), guard.Total);
    }

    /// <summary>
    /// The whole point of the ledger. The loudest guard in the log is the cheapest one - 42
    /// prompts answered in three seconds each is less lost time than one floating head wait -
    /// so counting log lines ranks them exactly backwards.
    /// </summary>
    [Fact]
    public void RanksByTimeLostNotByHowOftenItAppears()
    {
        var lines = new List<string> { "08:00:00.0000000,  Other, FloatingSideHeight,  Move to : 3000" };

        for (var i = 0; i < 20; i++)
        {
            lines.Add($"09:{i:00}:00.0000000,  Other, WallExtruderPLC,  **Ensure You Are Clear Of Any Moving Parts** Press Clamp/Fire Buttons To Continue, Machine Will Move");
            lines.Add($"09:{i:00}:02.0000000,  InputChange, THNTD,  Input (192.168.250.1-4.1) Changed to 1");
        }

        lines.Add("10:00:00.0000000,  Other, WallExtruderStep,  Step = 321");
        lines.Add("10:00:00.1000000,  Other, REv3,  Unsafe to move Floating Head please clear Obstacle Then Press THNTD");
        lines.Add("10:03:00.0000000,  Other, WallExtruderStep,  Step = 330");
        lines.Add("10:03:01.0000000,  Other, FloatingSideHeight,  Move to : 2000");

        var ledger = Ledger(lines.ToArray());

        var prompt = Guard(ledger, "moving parts");
        var head = Guard(ledger, "floating head");

        Assert.Equal(20, prompt.Count);
        Assert.Equal(1, head.Count);
        Assert.True(head.Total > prompt.Total);
        Assert.Equal(head.Name, ledger.Used.OrderByDescending(g => g.Total).First().Name);
    }

    [Fact]
    public void AddsUpTheWholeCostAndItsShareOfTheShift()
    {
        var ledger = Ledger(
            "08:00:00.0000000,  Other, WallExtruderStep,  Step = 10",
            "09:00:00.0000000,  Other, WallExtruderPLC,  **Ensure You Are Clear Of Any Moving Parts** Press Clamp/Fire Buttons To Continue, Machine Will Move",
            "09:00:36.0000000,  InputChange, THNTD,  Input (192.168.250.1-4.1) Changed to 1",
            "10:00:00.0000000,  Other, WallExtruderStep,  Step = 20");

        Assert.Equal(1, ledger.Count);
        Assert.Equal(TimeSpan.FromSeconds(36), ledger.Total);
        Assert.Equal(0.005, ledger.ShareOfShift!.Value, 5);
    }

    /// <summary>
    /// A guard is not a fault and must not be in the fault list, or the forty times the machine
    /// checked it was safe to move bury the four things that went wrong.
    /// </summary>
    [Fact]
    public void GuardsAreKeptOutOfTheFaultList()
    {
        var log = MachineLogFile.Parse(new[]
        {
            "09:00:00.0000000,  Other, WallExtruderStep,  Step = 10",
            "09:00:01.0000000,  Other, WallExtruderPLC,  **Ensure You Are Clear Of Any Moving Parts** Press Clamp/Fire Buttons To Continue, Machine Will Move",
            "09:00:03.0000000,  InputChange, THNTD,  Input (192.168.250.1-4.1) Changed to 1",
            "09:00:04.0000000,  Other, WallExtruderPLC,  Floating Side Safety Bar Pressed - Press Estop Reset to Continue",
            "09:00:05.0000000,  Other, REv3,  Unsafe to move Floating Head please clear Obstacle Then Press THNTD",
            "09:00:06.0000000,  Other, WallExtruderPLC,  Lost product, revert and try again. Fixed Product: False Floating: True"
        });

        var analysis = new SpidaLogAnalyser().Analyse(
            log, Array.Empty<ErrLogEntry>(), Array.Empty<ChangeLogEntry>(), DateTime.UtcNow);

        var faults = analysis.Cycles.SelectMany(c => c.Faults).Select(f => f.Text).ToList();

        Assert.DoesNotContain(faults, f => f.Contains("Moving Parts", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(faults, f => f.Contains("Safety Bar", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(faults, f => f.Contains("Unsafe to move Floating", StringComparison.OrdinalIgnoreCase));

        // The real fault in the same log is still there.
        Assert.Contains(faults, f => f.Contains("Lost product", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AMachineThatNeverTrippedAGuardReportsNothing()
    {
        Assert.False(Ledger("09:00:00.0000000,  Other, WallExtruderStep,  Step = 10").Any);
    }
}
