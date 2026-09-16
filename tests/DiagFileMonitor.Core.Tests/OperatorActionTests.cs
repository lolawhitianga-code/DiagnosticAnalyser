using DiagFileMonitor.Core.Knowledge;
using DiagFileMonitor.Core.SpidaLogs;

namespace DiagFileMonitor.Core.Tests;

/// <summary>
/// Built from a real AOR1694 export where an operator reported the floating side lower gun firing
/// on its own after they pressed stop. The report at the time said only that the last log line was
/// an axis disable, and a technician sent it back through the feedback loop saying what it should
/// have found instead.
/// </summary>
public class TwoHandControlTests
{
    private static IReadOnlyList<MachineLogEntry> Log(params string[] lines) => MachineLogFile.Parse(lines);

    private static string Press(string time, int value) =>
        $"{time},  InputChange, THNTD,  Input (COM7-2.12) Changed to {value}";

    [Fact]
    public void ReadsEachPressAndHowLongItWasHeld()
    {
        var findings = TwoHandControlCheck.Check(Log(
            Press("07:53:29.6121200", 1),
            Press("07:53:31.0026695", 0),
            Press("07:53:34.6431047", 1),
            Press("07:53:34.7837244", 0)));

        Assert.Equal("THNTD", findings.InputTag);
        Assert.Equal("COM7-2.12", findings.Address);
        Assert.Equal(2, findings.Presses.Count);
        Assert.Equal(1.39, findings.Presses[0].Held!.Value.TotalSeconds, 2);
        Assert.Equal(0.14, findings.LastPress!.Held!.Value.TotalSeconds, 2);
    }

    [Fact]
    public void APressStillDownWhenTheLogEndsIsReportedAsThat()
    {
        var findings = TwoHandControlCheck.Check(Log(Press("07:53:34.6431047", 1)));

        Assert.True(Assert.Single(findings.Presses).StillHeld);
        Assert.Null(findings.LastPress!.Held);
    }

    [Fact]
    public void GunOutputsFiredTogetherAreOneFiring()
    {
        // Four outputs go on at the same instant and off together a fraction later. That is one
        // firing, not eight events.
        var findings = TwoHandControlCheck.Check(Log(
            "07:53:30.1747290,  OutputChange, LowerGunFire,  Output (COM7-6.5) Set On",
            "07:53:30.1747290,  OutputChange, UpperGunFire,  Output (COM7-6.6) Set On",
            "07:53:30.1747290,  OutputChange, LowerGunFire,  Output (COM7-6.2) Set On",
            "07:53:30.1747290,  OutputChange, UpperGunFire,  Output (COM7-6.3) Set On",
            "07:53:30.4089480,  OutputChange, LowerGunFire,  Output (COM7-6.5) Set Off",
            "07:53:30.4089480,  OutputChange, UpperGunFire,  Output (COM7-6.6) Set Off"));

        var firing = Assert.Single(findings.Firings);
        Assert.Equal(4, firing.Outputs.Count);
        Assert.Equal(234, firing.HeldFor!.Value.TotalMilliseconds, 0);
    }

    [Fact]
    public void NoFiringAfterTheLastPressIsItselfTheFinding()
    {
        // The operator says a gun went off. Nothing commanded it. That points at the valve and the
        // air side rather than the program - and a gun firing with no output leaves no other trace.
        var findings = TwoHandControlCheck.Check(Log(
            "07:53:30.1747290,  OutputChange, LowerGunFire,  Output (COM7-6.5) Set On",
            "07:53:30.4089480,  OutputChange, LowerGunFire,  Output (COM7-6.5) Set Off",
            Press("07:53:34.6431047", 1),
            Press("07:53:34.7837244", 0),
            "07:53:38.9085106,  Other, WallExtruderStep,  Step = 0"));

        Assert.Single(findings.Firings);
        Assert.Empty(findings.FiringsAfterLastPress);
    }

    [Fact]
    public void AShortJabStandsOutAgainstTheMachinesOwnHabit()
    {
        var findings = TwoHandControlCheck.Check(Log(
            Press("07:31:50.3341329", 1), Press("07:31:51.7561204", 0),   // 1.42s
            Press("07:33:26.3914332", 1), Press("07:33:28.6256431", 0),   // 2.23s
            Press("07:34:08.3109740", 1), Press("07:34:09.6546550", 0),   // 1.34s
            Press("07:53:34.6431047", 1), Press("07:53:34.7837244", 0))); // 0.14s

        Assert.Equal(1.42, findings.MedianHold!.Value.TotalSeconds, 2);
        Assert.True(findings.LastPress!.Held < findings.MedianHold / 2);
    }

    [Fact]
    public void AMachineWithNoTwoHandControlReportsNothingRatherThanGuessing()
    {
        var findings = TwoHandControlCheck.Check(Log(
            "07:53:30.1747290,  InputChange, SawMotorConfirm,  Input (COM7-1.1) Changed to 1"));

        Assert.False(findings.Any);
        Assert.Empty(findings.InputTag);
    }
}

public class StepOutcomeTests
{
    private static IReadOnlyList<MachineLogEntry> Log(params string[] lines) => MachineLogFile.Parse(lines);

    private static string Step(string time, int value, string tag = "WallExtruderStep") =>
        $"{time},  Other, {tag},  Step = {value}";

    [Fact]
    public void CatchesTheOneTimeAStepDidNotDoWhatItUsuallyDoes()
    {
        // The whole point: this needs no idea what 1310 or 1400 mean. Five times it went one way,
        // once it did not.
        var story = StepOutcomeCheck.Check(Log(
            Step("07:32:45.7842340", 1310), Step("07:32:46.9842340", 1400),
            Step("07:33:27.2038460", 1310), Step("07:33:28.5138460", 1400),
            Step("07:34:07.7485030", 1310), Step("07:34:09.5185030", 1400),
            Step("07:48:11.4638800", 1310), Step("07:48:14.1938800", 1400),
            Step("07:53:29.3777500", 1310), Step("07:53:30.7877500", 1400),
            Step("07:53:35.5493074", 1310), Step("07:53:38.9085106", 0)));

        var step = story.LastWorkingStep!;
        Assert.Equal(1310, step.Step);
        Assert.Equal(6, step.Occurrences);
        Assert.Equal(1400, step.UsualNextStep);
        Assert.Equal(5, step.UsualNextCount);
        Assert.Equal(0, step.FinalNextStep);
        Assert.True(step.FinalDifferedFromUsual);
        Assert.Equal(3.36, step.FinalDwell!.Value.TotalSeconds, 2);
    }

    [Fact]
    public void AStepBehavingNormallyIsNotFlagged()
    {
        var story = StepOutcomeCheck.Check(Log(
            Step("07:32:45.0000000", 1310), Step("07:32:46.0000000", 1400),
            Step("07:33:27.0000000", 1310), Step("07:33:28.0000000", 1400),
            Step("07:34:07.0000000", 1310), Step("07:34:08.0000000", 1400),
            Step("07:34:09.0000000", 1310), Step("07:34:10.0000000", 1400)));

        Assert.False(story.LastWorkingStep!.FinalDifferedFromUsual);
    }

    [Fact]
    public void TheLogSimplyEndingIsNotADeviation()
    {
        // Without this, the last step of every export reads as the machine doing something
        // unusual, because there is no next step to compare against.
        var story = StepOutcomeCheck.Check(Log(
            Step("07:32:45.0000000", 1400), Step("07:32:46.0000000", 1310),
            Step("07:33:27.0000000", 1400), Step("07:33:28.0000000", 1310),
            Step("07:34:07.0000000", 1400)));

        Assert.Equal(1400, story.LastWorkingStep!.Step);
        Assert.Null(story.LastWorkingStep.FinalNextStep);
        Assert.False(story.LastWorkingStep.FinalDifferedFromUsual);
    }

    [Fact]
    public void AZeroArrivingFromAHighStepIsAnOperatorStop()
    {
        var story = StepOutcomeCheck.Check(Log(
            Step("07:53:35.5493074", 1310),
            Step("07:53:38.9085106", 0)));

        Assert.True(story.OperatorStoppedFromHmi);
        Assert.Equal(TimeSpan.Parse("07:53:38.9085106"), story.StoppedAt);
        // The technician confirmed this meaning for the wall extruder specifically.
        Assert.Equal(Confidence.Confirmed, story.StopConfidence);
    }

    [Fact]
    public void ASecondaryStepCounterIsNotMixedIntoTheMainSequence()
    {
        // SidePLCStep is a different PLC keeping its own count. Merging the two would invent
        // transitions that never happened, and its own zero is a normal reset, not a stop.
        var story = StepOutcomeCheck.Check(Log(
            Step("07:32:45.0000000", 1310), Step("07:32:45.5000000", 12, "SidePLCStep"),
            Step("07:32:46.0000000", 1400), Step("07:32:46.5000000", 0, "SidePLCStep"),
            Step("07:33:27.0000000", 1310), Step("07:33:28.0000000", 1400),
            Step("07:34:07.0000000", 1310), Step("07:34:08.0000000", 1400)));

        Assert.Equal("WallExtruderStep", story.StepTag);
        Assert.Equal("SidePLCStep", Assert.Single(story.OtherStepTags));
        Assert.False(story.OperatorStoppedFromHmi);
        // The main sequence ended on 1400, so there is no next step and nothing to flag.
        Assert.Equal(1400, story.LastWorkingStep!.Step);
        Assert.Null(story.LastWorkingStep.FinalNextStep);
        Assert.False(story.LastWorkingStep.FinalDifferedFromUsual);
    }

    [Fact]
    public void AZeroOnAnUnknownTagIsOnlyInferred()
    {
        var story = StepOutcomeCheck.Check(Log(
            Step("07:53:35.0000000", 500, "SomeOtherStep"),
            Step("07:53:38.0000000", 0, "SomeOtherStep")));

        Assert.True(story.OperatorStoppedFromHmi);
        Assert.Equal(Confidence.Inferred, story.StopConfidence);
    }

    [Fact]
    public void TheShutdownCascadeIsGatheredAsOneEvent()
    {
        // Everything in the moment after a stop is the machine turning off. Reporting twenty
        // separate "axis disabled" lines as findings buries the one that matters.
        var story = StepOutcomeCheck.Check(Log(
            Step("07:53:35.5493074", 1310),
            Step("07:53:38.9085106", 0),
            "07:53:38.9085106,  MotionEvent, FloatingSidePuller,  Axis Disabled",
            "07:53:38.9866323,  OutputChange, TopStudClamp,  Output (COM7-6.7) Set Off",
            "07:53:39.1272533,  Other, SyncMove,  Axis Disable",
            "07:54:10.0000000,  OutputChange, PlateSupport,  Output (COM7-5.2) Set On"));

        // The late line is half a minute later - that is not part of the shutdown.
        Assert.Equal(4, story.ShutdownCascade.Count);
    }

    [Fact]
    public void ALogWithNoStepsReportsNothing()
    {
        Assert.False(StepOutcomeCheck.Check(Log(
            "07:53:30.1747290,  OutputChange, LowerGunFire,  Output (COM7-6.5) Set On")).Any);
    }
}
