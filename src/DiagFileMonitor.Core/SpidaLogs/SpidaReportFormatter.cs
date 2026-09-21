using System.Text;
using DiagFileMonitor.Core.Knowledge;
using DiagFileMonitor.Core.Models;

namespace DiagFileMonitor.Core.SpidaLogs;

/// <summary>Writes the analysis up in the order the guide asks for.</summary>
public static class SpidaReportFormatter
{
    public static string Format(
        DiagnosticFileSummary file,
        SpidaLogAnalysis analysis,
        KnowledgeFindings? knowledge = null,
        ComplaintFindings? complaint = null)
    {
        var text = new StringBuilder();

        text.AppendLine($"DIAGNOSTIC ANALYSIS - {file.OriginalFileName}");
        text.AppendLine(new string('-', 78));
        text.AppendLine($"Machine:   {file.MachineType} ({file.MachineName})   serial {file.SerialNumber}");
        text.AppendLine($"Customer:  {file.Customer}, {file.SiteLocation}");
        text.AppendLine($"Software:  {file.SoftwareName} {file.Version}");

        if (SoftwareVersion.Note(file.Version) is { } versionNote)
        {
            text.AppendLine($"           !! {ReportText.Wrap(versionNote, 14)}");
        }
        text.AppendLine($"Arrived:   {file.ArrivedDisplay}");

        if (analysis.MachineModelFromLog is { } fromLog)
        {
            var agrees = fromLog.Contains(file.MachineType, StringComparison.OrdinalIgnoreCase);
            text.AppendLine($"MachineLog reports model: {fromLog}{(agrees ? string.Empty : "   <-- does not match Machine.xml")}");
        }

        AppendOperatorText(text, file);
        if (complaint is not null) AppendComplaint(text, complaint);
        // What normal looks like comes before the end of the log is picked over. Reading the last
        // few minutes closely without knowing the rest of the day is how an ordinary pause gets
        // written up as a symptom.
        AppendWhatNormalLooksLike(text, analysis);
        AppendHowItEnded(text, analysis, file);
        // Straight after the end of the log, because a machine still sitting on one step is the
        // callout itself rather than something to notice halfway down a report.
        if (knowledge is not null) AppendFloatingHead(text, knowledge.FloatingHead);
        if (knowledge is not null) AppendStuckStep(text, knowledge.StuckStep);
        // What the operator last asked for, and what the machine did about it. This goes high up
        // because on a hand-driven machine it is usually the answer.
        if (knowledge is not null) AppendLastOperatorAction(text, knowledge);
        // Higher still when it fires: it is the complaint itself, not context for it.
        if (knowledge is not null) AppendCutNotTaken(text, knowledge.CutNotTaken);
        // The machine says what it is waiting for. Whether it got it is the whole answer.
        if (knowledge is not null) AppendWaitingOn(text, knowledge.WaitingOn);
        if (knowledge is not null) AppendMotorConfirm(text, knowledge);
        if (knowledge is not null) AppendDriveFaults(text, knowledge);
        AppendUnits(text, analysis);
        AppendRepeats(text, analysis);
        AppendErrors(text, analysis);
        AppendChanges(text, analysis);
        if (knowledge is not null) KnowledgeReportFormatter.Append(text, knowledge);
        if (knowledge is not null) AppendSides(text, knowledge.Sides);
        AppendWhereToLook(text, file, analysis, knowledge);
        AppendQuestions(text, analysis, knowledge);
        AppendRawTail(text, analysis);

        if (analysis.Notes.Count > 0)
        {
            text.AppendLine();
            text.AppendLine("NOTES ON THIS ANALYSIS");
            foreach (var note in analysis.Notes) text.AppendLine($"  - {note}");
        }

        return text.ToString();
    }

    private static void AppendOperatorText(StringBuilder text, DiagnosticFileSummary file)
    {
        if (string.IsNullOrWhiteSpace(file.SupportIssue)
            && string.IsNullOrWhiteSpace(file.SupportPanel)
            && string.IsNullOrWhiteSpace(file.SupportMembers)) return;

        text.AppendLine();
        text.AppendLine("WHAT THE OPERATOR REPORTED");
        if (!string.IsNullOrWhiteSpace(file.SupportPanel)) text.AppendLine($"  Panel:   {file.SupportPanel}");
        if (!string.IsNullOrWhiteSpace(file.SupportMembers)) text.AppendLine($"  Members: {file.SupportMembers}");
        if (!string.IsNullOrWhiteSpace(file.SupportIssue)) text.AppendLine($"  Issue:   \"{file.SupportIssue}\"");
    }

    /// <summary>
    /// What the operator said, turned into somewhere to look. This goes first because the logs
    /// are loudest about whatever happens most often, which is rarely the complaint.
    /// </summary>
    private static void AppendComplaint(StringBuilder text, ComplaintFindings complaint)
    {
        if (!complaint.HasIssueText) return;

        text.AppendLine();
        text.AppendLine("START HERE - WHAT THE OPERATOR DESCRIBED");

        if (!complaint.Any)
        {
            text.AppendLine($"  \"{complaint.Issue}\"");
            text.AppendLine("  Nothing in that matches a complaint we have a routine for, so the rest of this");
            text.AppendLine("  report works from the logs alone. Read the operator's words first anyway.");
            return;
        }

        foreach (var match in complaint.Topics)
        {
            text.AppendLine();
            text.AppendLine($"  {match.Topic.Name.ToUpperInvariant()}");
            text.AppendLine($"    (from \"{string.Join("\", \"", match.MatchedOn)}\" in what the operator wrote)");

            foreach (var step in match.Topic.LookAt)
            {
                text.AppendLine($"      - {ReportText.Wrap(step, 8)}");
            }

            AppendRelatedChanges(text, match);
        }
    }

    private static void AppendRelatedChanges(StringBuilder text, MatchedTopic match)
    {
        if (match.Topic.SettingWords.Count == 0) return;

        text.AppendLine();

        if (match.RelatedChanges.Count == 0)
        {
            text.AppendLine($"      Change.log has no {string.Join("/", match.Topic.SettingWords)} settings changed at all,");
            text.AppendLine("      so this is not a setting somebody moved.");
            return;
        }

        text.AppendLine($"      {match.RelatedChanges.Count} matching setting change(s), newest first:");
        foreach (var change in match.RelatedChanges)
        {
            text.AppendLine($"        {change.Display}");
        }
    }

    /// <summary>
    /// The end of the log, first. A bundle is normally exported within a few minutes of the
    /// problem, so the last thing the machine did is usually the thing being reported - and a
    /// long quiet tail says it stopped and sat there rather than carrying on.
    /// </summary>
    /// <summary>
    /// What a normal attempt looks like on this machine, before anything about the end of the log
    /// is said.
    /// <para>
    /// Without it every report reads as though the last few minutes are remarkable. Sometimes they
    /// are not, and saying so plainly is worth as much as finding a fault.
    /// </para>
    /// </summary>
    private static void AppendWhatNormalLooksLike(StringBuilder text, SpidaLogAnalysis analysis)
    {
        if (analysis.Baseline is not { Any: true } baseline) return;

        text.AppendLine();
        text.AppendLine("WHAT NORMAL LOOKS LIKE HERE - before reading the end");

        var window = analysis.LogStart is { } from && analysis.LogEnd is { } to
            ? $"{from:hh\\:mm\\:ss} to {to:hh\\:mm\\:ss}"
            : "this log";

        text.AppendLine($"  Measured from this machine's own log, {window}. This machine against itself,");
        text.AppendLine("  not against any other machine and not against a specification.");
        text.AppendLine();

        if (!baseline.WasRunningNormally)
        {
            text.AppendLine(baseline.Completed == 0
                ? $"  None of the {baseline.Attempted} attempt(s) in this log completed."
                : $"  Only {baseline.Completed} of {baseline.Attempted} attempt(s) completed.");

            text.AppendLine("  That is too few to call anything normal, so there is no yardstick here to hold the");
            text.AppendLine("  end of the log against. Read the whole file rather than trusting the end of it.");
        }
        else
        {
            text.AppendLine($"  {baseline.Completed} of {baseline.Attempted} attempt(s) completed. A typical one took "
                            + $"{MachineCycle.Describe(baseline.Typical)},");
            text.AppendLine($"  with most falling between {MachineCycle.Describe(baseline.Quickest)} and "
                            + $"{MachineCycle.Describe(baseline.Slowest)}.");

            if (baseline.WithFaults > 0)
                text.AppendLine($"  {baseline.WithFaults} attempt(s) carried a machine fault of some kind.");
        }

        if (baseline.Last is { } last)
        {
            text.AppendLine();
            var verdict = baseline.LastAgainstTypical switch
            {
                null => "there is nothing to compare it against",
                < 1.5 and > 0.5 => "which is in line with the rest of the log",
                >= 1.5 and < 3 => "which is longer than this machine's own habit",
                >= 3 => ">>> which is far longer than this machine's own habit",
                _ => "which is shorter than this machine's own habit"
            };

            text.AppendLine($"  The last attempt took {MachineCycle.Describe(last)}, {verdict}.");

            if (!baseline.LastCompleted)
                text.AppendLine("  It did not complete.");
        }
    }

    /// <summary>
    /// How long after the last log line the file was exported.
    /// <para>
    /// The whole report leans on the end of the log being the problem. That is only true when the
    /// file was taken shortly afterwards, and how long afterwards is knowable, so it should be
    /// said rather than assumed. The machine's clock and the export timestamp are two different
    /// clocks, so anything beyond a few hours apart is reported as not knowable rather than
    /// turned into a number nobody should trust.
    /// </para>
    /// </summary>
    private static void AppendHowFresh(StringBuilder text, TimeSpan logEnd, DiagnosticFileSummary file)
    {
        var exported = file.ArrivedAtLocal.TimeOfDay;
        var gap = exported - logEnd;

        // Across midnight the other way round.
        if (gap < TimeSpan.Zero) gap += TimeSpan.FromDays(1);

        if (gap > TimeSpan.FromHours(6))
        {
            text.AppendLine($"  The file is timestamped {file.ArrivedDisplay}, which is a long way from the end of");
            text.AppendLine("  the log. The machine's clock and this one may not agree, so treat the end of the");
            text.AppendLine("  log as the problem only if the operator says the export was taken straight after.");
            return;
        }

        text.AppendLine($"  The file was exported {MachineCycle.Describe(gap)} after that.");

        if (gap < TimeSpan.FromMinutes(5))
        {
            text.AppendLine("  >>> Taken right after it happened, so the end of this log really is the problem.");
        }
        else
        {
            text.AppendLine("  The operator may have carried on working before taking the export, so the end of");
            text.AppendLine("  the log is not necessarily the moment they are complaining about.");
        }
    }

    /// <summary>
    /// The end of the log exactly as written, every line and every category.
    /// <para>
    /// Everything above this is a reading. This is the file. A support person asked for it after a
    /// case where the summary dropped the six InputChange lines that explained the whole thing.
    /// </para>
    /// </summary>
    private static void AppendRawTail(StringBuilder text, SpidaLogAnalysis analysis)
    {
        if (analysis.RawTail.Count == 0) return;

        text.AppendLine();
        text.AppendLine($"THE LAST {analysis.RawTail.Count} LINES OF MACHINELOG.TXT, EXACTLY AS WRITTEN");
        text.AppendLine(new string('-', 78));
        text.AppendLine("  Nothing filtered, nothing reordered, every category. Everything above this is a");
        text.AppendLine("  reading of the file; this is the file.");
        text.AppendLine();

        foreach (var entry in analysis.RawTail)
        {
            text.AppendLine($"  {entry.LineNumber,6}  {entry.Display}");
        }
    }

    private static void AppendHowItEnded(
        StringBuilder text, SpidaLogAnalysis analysis, DiagnosticFileSummary file)
    {
        if (analysis.FinalEntries.Count == 0) return;

        text.AppendLine();
        text.AppendLine("HOW IT ENDED - read this first");
        text.AppendLine($"  The file is normally exported within minutes of the problem, so the end of");
        text.AppendLine($"  MachineLog.txt is usually the problem itself.");
        text.AppendLine();

        if (analysis.LogEnd is { } end)
        {
            text.AppendLine($"  The log ends at {end:hh\\:mm\\:ss}.");
            AppendHowFresh(text, end, file);
        }

        if (analysis.LastNotableEvent is { } last && analysis.SilenceBeforeEnd is { } silence)
        {
            text.AppendLine($"  The last thing the machine actually did was at {last.Time:hh\\:mm\\:ss}:");
            text.AppendLine($"      {last.Display}");

            if (silence >= TimeSpan.FromSeconds(30))
            {
                text.AppendLine();
                text.AppendLine($"  {ReportText.Wrap($"Nothing of note happened for the last {MachineCycle.Describe(silence)}. "
                    + "The machine was sitting there when the file was taken, so whatever stopped it is "
                    + "above, not below.", 2)}");
            }
        }

        text.AppendLine();
        var skipped = analysis.ChatterSkipped > 0
            ? $" (heartbeat lines left out - {analysis.ChatterSkipped} of them repeat too often to mean anything)"
            : string.Empty;

        text.AppendLine($"  The last {analysis.FinalEntries.Count} lines that say something{skipped}:");
        foreach (var entry in analysis.FinalEntries)
        {
            text.AppendLine($"      {entry.Display}");
        }
    }

    /// <summary>
    /// What the floating head obstruction guard cost in time.
    /// <para>
    /// This is not written up as a fault, because it is not one. The laser stops the head before
    /// it drives into whatever is in front of it - almost always the pieces set by hand for a
    /// taller panel, still standing there when the next panel is shorter. Time spent clearing
    /// that is time well spent against the machine crashing into it. So the report gives the
    /// cost and leaves it at that.
    /// </para>
    /// </summary>
    private static void AppendFloatingHead(StringBuilder text, FloatingHeadFindings findings)
    {
        if (!findings.Any) return;

        text.AppendLine();
        text.AppendLine("FLOATING HEAD OBSTRUCTION - WHAT IT COST IN TIME");
        text.AppendLine($"  {ReportText.Wrap("The laser stopping the head before it drives into something. Going from a "
            + "taller panel to a shorter one, the pieces set by hand for the taller one are still in the way, and the "
            + "operator moves them and presses THNTD. This is the guard working - far better than the machine "
            + "crashing into what it saw. So this is a cost to know about, not a fault to fix.", 2)}");
        text.AppendLine();

        var share = findings.ShareOfShift is { } fraction ? $" - {fraction:P2} of the log" : string.Empty;
        var waits = findings.Episodes.Count == 1 ? "1 wait" : $"{findings.Episodes.Count} waits";
        text.AppendLine($"  {waits}, {MachineCycle.Describe(findings.TotalTime)} in total{share}.");

        if (findings.LongestEpisode is { } worst && findings.Episodes.Count > 1)
        {
            var why = worst.HeightChange is { } change && change < 0
                ? $" (head coming in {Math.Abs(change):F0} mm)"
                : string.Empty;
            text.AppendLine($"  Longest was {MachineCycle.Describe(worst.Lasted)} at {worst.StartedAt:hh\\:mm\\:ss}{why}.");
        }

        if (findings.AbandonedCount > 0)
        {
            text.AppendLine($"  {ReportText.Wrap($"{findings.AbandonedCount} of them went back to step 0 rather than "
                + "being cleared - the operator gave up on it and started again.", 2)}");
        }

        foreach (var episode in findings.WorthALook)
        {
            text.AppendLine();
            text.AppendLine($"  {episode.StartedAt:hh\\:mm\\:ss} - worth a second look. Held it up for "
                            + $"{MachineCycle.Describe(episode.Lasted)}.");

            if (episode.HeightChange is { } change)
            {
                text.AppendLine($"      Floating head target went {episode.HeightBefore:F0} -> {episode.HeightAfter:F0} "
                                + $"({change:+0;-0} mm), so a lower panel does not explain this one.");
            }
            else if (episode.HeightBefore is { } before)
            {
                text.AppendLine($"      {ReportText.Wrap($"Floating head was last sent to {before:F0} and was never given "
                    + "a new target, so there is nothing to say how far it was being asked to come in.", 6)}");
            }

            if (episode.LogEndedDuringIt)
            {
                text.AppendLine($"      {ReportText.Wrap("The log ends here, so we cannot see it clear. That is not the "
                    + "same as it never clearing - ask whether the operator moved the pieces, pressed THNTD, and "
                    + "whether it carried on.", 6)}");
            }
        }
    }

    /// <summary>
    /// The machine sitting on one step with the clock running. Dwell, not frequency: a guard
    /// that trips forty times a shift and clears in a fifth of a second each time is the machine
    /// working, and the same message once with nothing moving four minutes later is the fault.
    /// </summary>
    private static void AppendStuckStep(StringBuilder text, StuckStep? stuck)
    {
        if (stuck is null || !stuck.WorthReporting) return;

        text.AppendLine();
        text.AppendLine("IT STOPPED ON ONE STEP AND STAYED THERE");
        text.AppendLine($"  Step {stuck.Step}, reached at {stuck.ReachedAt:hh\\:mm\\:ss}, still there "
                        + $"{MachineCycle.Describe(stuck.HeldFor)} later when the file was taken.");

        if (stuck.Message.Length > 0)
        {
            text.AppendLine($"  It said this {stuck.RepeatsOfMessage} time(s) while it sat there, and nothing else:");
            text.AppendLine($"      \"{stuck.Message}\"");
        }

        text.AppendLine();

        if (stuck.FirstTimeToday)
        {
            text.AppendLine($"  {ReportText.Wrap($"The machine reached step {stuck.Step} once in this whole log - "
                + "this once, at the end. It is a branch it does not normally take, so this is not "
                + "something it has been living with all day.", 2)}");
        }
        else if (stuck.HeldFarTooLong)
        {
            text.AppendLine($"  {ReportText.Wrap($"It reached step {stuck.Step} {stuck.TimesReachedInLog} times today and "
                + $"normally passes through in {MachineCycle.Describe(stuck.TypicalHold)}. This time it did not "
                + "move on at all. The step is routine; sitting on it is not.", 2)}");
        }

        text.AppendLine();
        text.AppendLine("  Worth asking:");
        text.AppendLine("      - What was the operator looking at? The machine was waiting on something,");
        text.AppendLine("        so did the screen say what, and did they do it?");
        text.AppendLine("      - If it needs a button press to carry on, was it pressed? A condition that");
        text.AppendLine("        never cleared and one nobody answered look the same in the log.");
        text.AppendLine("      - Did it come right on its own afterwards, or did it need a power cycle?");
    }

    /// <summary>
    /// Which side of the machine each output address is, where CloudLog/maint_data.json says.
    /// That file is the only place in a bundle where an output carries its real name and its
    /// side; MachineLog.txt records the same points as bare addresses with the side stripped.
    /// </summary>
    private static void AppendSides(StringBuilder text, SideFindings? sides)
    {
        if (sides is null || !sides.Any) return;

        var usable = sides.Trustworthy.Where(r => r.Side != MachineSide.Unknown).ToList();
        if (usable.Count == 0) return;

        text.AppendLine();
        text.AppendLine("WHICH SIDE EACH OUTPUT IS ON");
        text.AppendLine($"  {ReportText.Wrap("Read from CloudLog/maint_data.json, which carries an hour of run time for "
            + "every output under the machine's own name. Matching those run times back to the addresses in "
            + "MachineLog.txt names them. Measured, not guessed.", 2)}");
        text.AppendLine();

        foreach (var r in usable.OrderBy(r => r.Side).ThenBy(r => r.MachineName, StringComparer.Ordinal))
        {
            text.AppendLine($"  {r.Id.Address,-22} {r.MachineName}");
        }

        if (sides.Ambiguous.Count > 0)
        {
            text.AppendLine();
            text.AppendLine($"  {ReportText.Wrap($"{sides.Ambiguous.Count} more could not be told apart: both sides ran for "
                + "exactly the same length of time in the counted hour, which is what you would expect of a machine "
                + "clamping and releasing both sides together. Nothing separates them, so they are left unnamed "
                + "rather than guessed at.", 2)}");
        }
    }

    /// <summary>
    /// What each motor's run command and its confirmation input did. This is the difference
    /// between "the machine was asked to cut" and "the blade was turning", and it is usually the
    /// whole answer when an operator writes that something is not running.
    /// <para>
    /// It says when the confirmation last read 1 and last read 0 whatever the verdict, because
    /// "it never appears in this file" is an answer to that question too - a short export taken
    /// after the motor stopped carries no change at all, and staying silent there reads as
    /// nothing being wrong.
    /// </para>
    /// </summary>

    /// <summary>
    /// The last thing the operator asked the machine to do, and what happened next.
    /// <para>
    /// Added after a real AOR1694 case where an operator reported a gun firing on its own. The
    /// report at the time said only that the last log line was an axis disable, which was true and
    /// useless. The two-hand control and the step the machine was sitting in are what tell the
    /// story.
    /// </para>
    /// </summary>
    private static void AppendLastOperatorAction(StringBuilder text, KnowledgeFindings knowledge)
    {
        var hand = knowledge.TwoHandControl;
        var story = knowledge.StepStory;

        if (!hand.Any && !story.Any) return;

        text.AppendLine();
        text.AppendLine("WHAT THE OPERATOR LAST ASKED FOR");
        text.AppendLine(new string('-', 78));

        if (story.OperatorStoppedFromHmi && story.StoppedAt is { } stoppedAt)
        {
            var sure = story.StopConfidence == Confidence.Confirmed ? string.Empty : " (inferred)";
            text.AppendLine($"  The machine was stopped from the HMI at {stoppedAt:hh\\:mm\\:ss\\.fff}{sure}.");
            text.AppendLine($"  {story.StepTag} went to 0 part way through a cycle, which is somebody");
            text.AppendLine("  pressing stop rather than a cycle finishing.");

            if (story.ShutdownCascade.Count > 0)
            {
                var span = story.ShutdownCascade[^1].Time - story.ShutdownCascade[0].Time;
                text.AppendLine($"  The {story.ShutdownCascade.Count} entries in the {span.TotalMilliseconds:0}ms that follow are the machine");
                text.AppendLine("  shutting down - axes disabling, clamps and supports dropping. They are the");
                text.AppendLine("  consequence of the stop, not separate faults.");
            }

            text.AppendLine();
        }

        if (story.LastWorkingStep is { } step)
        {
            text.AppendLine($"  Last working step: {story.StepTag} {step.Step}, reached at {step.FinalReachedAt:hh\\:mm\\:ss\\.fff}.");

            if (step.FinalDifferedFromUsual)
            {
                text.AppendLine($"  >>> That step came up {step.Occurrences} times in this log. {step.UsualNextCount} of those went on to");
                text.AppendLine($"      step {step.UsualNextStep}. This time it went to {step.FinalNextStep}.");

                if (step.FinalDwell is { } finalDwell && step.TypicalDwell is { } typical)
                {
                    text.AppendLine($"      It sat there {finalDwell.TotalSeconds:0.#}s, against {typical.TotalSeconds:0.#}s typically.");
                }

                text.AppendLine("      Whatever that step is waiting for, this is the time it did not get it.");
            }
            else if (step.Occurrences > 1 && step.FinalNextStep is not null)
            {
                text.AppendLine($"  It behaved as it usually does - on to step {step.FinalNextStep}, same as the other"
                                + $" {step.Occurrences - 1} time(s).");
            }

            text.AppendLine();
        }

        if (hand.Presses.Count > 0 && hand.LastPress is { } last)
        {
            var address = hand.Address.Length > 0 ? $" ({hand.Address})" : string.Empty;
            text.AppendLine($"  Two-hand control {hand.InputTag}{address}: {hand.Presses.Count} press(es) in this log.");

            foreach (var press in hand.Presses.TakeLast(3))
            {
                var held = press.Held is { } h ? $"held {h.TotalSeconds:0.00}s" : "still held when the log ended";
                var marker = ReferenceEquals(press, last) ? "  <-- last" : string.Empty;
                text.AppendLine($"    {press.PressedAt:hh\\:mm\\:ss\\.fff}  {held}{marker}");
            }

            if (last.Held is { } lastHeld && hand.MedianHold is { } median
                && median > TimeSpan.Zero && lastHeld < median / 2)
            {
                text.AppendLine($"  The last press was {lastHeld.TotalSeconds:0.00}s against a usual {median.TotalSeconds:0.00}s - a short jab");
                text.AppendLine("  rather than a held press. Worth asking the operator about.");
            }

            text.AppendLine();
        }

        AppendFiringAudit(text, hand);
    }

    /// <summary>
    /// What the machine said it was waiting for, joined up with whether it got it.
    /// <para>
    /// From a real M21737 case. The log ended repeating "Waiting for Both Panel Height Servos in
    /// position and PlateSupports Down". The report of the day showed that line and stopped, which
    /// was true and useless. The answer was in the same file: the plate support input is logged at
    /// one address, and every paired input on that machine has a partner two bits down on the
    /// module below. The second plate support's input never changed once in the whole log.
    /// </para>
    /// </summary>
    private static void AppendWaitingOn(StringBuilder text, WaitingOnFindings waiting)
    {
        if (!waiting.Any) return;

        text.AppendLine();
        text.AppendLine("WHAT IT SAID IT WAS WAITING FOR");
        text.AppendLine(new string('-', 78));
        text.AppendLine($"  {waiting.Tag}: \"{waiting.Message}\"");

        var span = waiting.From is { } from && waiting.To is { } to && to > from
            ? $" over {MachineCycle.Describe(to - from)}"
            : string.Empty;

        text.AppendLine($"  Said {waiting.Repeats} time(s){span}."
                        + (waiting.StillWaitingAtTheEnd ? " The log ends still saying it." : string.Empty));
        text.AppendLine();

        if (waiting.Axes.Count > 0)
        {
            text.AppendLine("  The axes it names:");

            foreach (var axis in waiting.Axes)
            {
                var verdict = axis.Ready ? "in position" : ">>> " + axis.State;
                text.AppendLine($"    {axis.Name,-28} {verdict}"
                                + (axis.Since is { } at ? $"   since {at:hh\\:mm\\:ss}" : string.Empty));
            }

            text.AppendLine();
        }

        if (waiting.Named.Count > 0)
        {
            text.AppendLine("  The inputs and outputs it names:");

            foreach (var signal in waiting.Named)
            {
                var held = signal.Since is { } since
                    ? $"since {since:hh\\:mm\\:ss}"
                    : "never changed in this log";

                text.AppendLine($"    {signal.Id.Name,-24} {signal.Id.Address,-22} "
                                + $"{(signal.On ? "on " : ">>> OFF")}   {held}");
            }

            text.AppendLine();
        }

        foreach (var orphan in waiting.Named.Where(s => s.PartnerMissing))
        {
            text.AppendLine($"  >>> {orphan.Id.Name} is logged at one address only, {orphan.Id.Address}.");

            // Where the second address comes from changes how much it should be trusted, so say.
            text.AppendLine(orphan.PartnerFromTheMap
                ? $"      This model is known to have a second one at {orphan.MissingPartnerAddress}, and that"
                : $"      This machine pairs its inputs one per side, so its partner would be"
                  + $" {orphan.MissingPartnerAddress} - and that");

            text.AppendLine("      address never appears anywhere in this log.");
            text.AppendLine("      A log records changes, so an input that never came on leaves no trace at all.");
            text.AppendLine("      That is what a sensor stuck off looks like from here, and it fits a machine");
            text.AppendLine("      waiting for something it says it has not got.");
            text.AppendLine("      Check that sensor, its cable and its connector on the other side of the machine.");
            text.AppendLine();
        }

        // Only worth showing the working when the working is what produced the answer.
        if (waiting.PairedExamples.Count > 0
            && waiting.Named.Any(s => s.PartnerMissing && !s.PartnerFromTheMap))
        {
            text.AppendLine("      The pairs this machine does log, which is where that reading comes from:");

            foreach (var pair in waiting.PairedExamples.Take(4))
                text.AppendLine($"        {pair}");

            text.AppendLine();
        }

        if (!waiting.SomethingIsUnsatisfied && waiting.Named.Count > 0)
        {
            text.AppendLine("  Everything it names was satisfied at that moment, so whatever held it up is not");
            text.AppendLine("  in this list. Read the raw tail at the end of this report.");
        }
    }

    /// <summary>
    /// The machine driven into position over and over with no cut following.
    /// <para>
    /// From a real M22215 case where the operator wrote "manual to 335 thntd, no action". The
    /// report of the day said the log ended on an axis status - true, and no use to anybody. What
    /// the log shows is the trolley reaching position and then nothing at all being written for
    /// thirteen seconds, six times over.
    /// </para>
    /// </summary>
    private static void AppendCutNotTaken(StringBuilder text, CutNotTakenFindings cut)
    {
        if (!cut.Any) return;

        text.AppendLine();
        text.AppendLine("ASKED FOR A CUT AND NOTHING HAPPENED");
        text.AppendLine(new string('-', 78));

        text.AppendLine($"  The machine was driven into position {cut.Waits.Count} time(s) and no cut followed");

        if (cut.LastCutAt is { } at)
            text.AppendLine($"  any of them. The last cut this machine made was at {at:hh\\:mm\\:ss}.");
        else
            text.AppendLine("  any of them.");

        text.AppendLine("  Each time, the operator gave up and started again.");
        text.AppendLine();

        foreach (var wait in cut.Waits)
        {
            text.AppendLine($"    {wait.RequestedAt:hh\\:mm\\:ss\\.fff}  {wait.Describe()}");

            var arrived = wait.InPositionAt is { } inPosition
                ? $"in position {inPosition:hh\\:mm\\:ss\\.fff}, "
                : "no arrival logged, ";

            var silence = wait.EntriesWhileWaiting == 0
                ? "nothing logged at all"
                : $"only {wait.EntriesWhileWaiting} line(s) logged";

            text.AppendLine($"                  {arrived}{silence} for {wait.Waited.TotalSeconds:0.#}s,");
            text.AppendLine($"                  then {wait.GaveUpBy} at {wait.GaveUpAt:hh\\:mm\\:ss\\.fff}");
        }

        text.AppendLine();

        if (!cut.TwoHandEverLogged)
        {
            text.AppendLine("  >>> No two-hand control input appears anywhere in this log.");
            text.AppendLine("      Where the buttons are wired straight into the PLC, the software only ever");
            text.AppendLine("      sees a press the PLC has already accepted. An operator pressing and getting");
            text.AppendLine("      nothing looks exactly like this - silence. The log cannot tell you whether");
            text.AppendLine("      the buttons were pressed, so it cannot rule the operator out either.");
            text.AppendLine();
        }

        if (cut.CutModeDuringWaits.Length > 0)
        {
            text.AppendLine($"  Cut mode was {cut.CutModeDuringWaits} for every one of those attempts.");

            var cut_ = string.Join(", ", cut.CutModeAtCutStart.Select(m => $"{m.Value} under {m.Key}"));
            if (cut_.Length > 0)
                text.AppendLine($"  The {cut.CutCycles} cut(s) this log does contain: {cut_}.");

            if (cut.CutModesThatNeverCut.Contains(cut.CutModeDuringWaits, StringComparer.OrdinalIgnoreCase))
            {
                text.AppendLine($"  >>> Nothing in this log ever cut while cut mode was {cut.CutModeDuringWaits}.");
                text.AppendLine("      That is measured here, not a rule we have been told - but it is the");
                text.AppendLine("      first thing to check.");
            }

            text.AppendLine();
        }

        text.AppendLine("  What to check, in order:");
        text.AppendLine("    1. Are the two-hand buttons making? Meter them at the PLC input, not at the HMI.");
        text.AppendLine("    2. If they are, is the PLC accepting them in the mode the machine was in?");
        text.AppendLine("    3. Ask the operator what they pressed and what the screen said at the time.");
    }

    /// <summary>
    /// What the guns were told to do. Where an operator says a gun went off and nothing commanded
    /// it, the absence in the log is the finding - a gun that fires with no output asked for is an
    /// air or valve problem, not a control one.
    /// </summary>
    private static void AppendFiringAudit(StringBuilder text, TwoHandControlFindings hand)
    {
        if (hand.Firings.Count == 0 && hand.Presses.Count == 0) return;

        if (hand.Firings.Count == 0)
        {
            text.AppendLine("  No gun firing was commanded anywhere in this log.");
            text.AppendLine();
            return;
        }

        text.AppendLine($"  Gun firing commanded {hand.Firings.Count} time(s). Last one:");
        text.AppendLine($"    {hand.Firings[^1].Describe()}");

        if (hand.LastPress is not null && hand.FiringsAfterLastPress.Count == 0)
        {
            text.AppendLine();
            text.AppendLine("  >>> Nothing was commanded to fire after the operator's last press.");
            text.AppendLine("      If the operator says a gun went off after that, the PLC did not ask");
            text.AppendLine("      it to - so look at the valve and the air side, not the program.");
            text.AppendLine("      A gun that fires with no output commanded leaves no trace in this log.");
        }

        text.AppendLine();
    }

    private static void AppendMotorConfirm(StringBuilder text, KnowledgeFindings knowledge)
    {
        var findings = knowledge.MotorConfirm;
        if (!findings.Any) return;

        var failures = findings.Failures.ToList();

        text.AppendLine();
        text.AppendLine(failures.Count > 0
            ? "*** A MOTOR WAS TOLD TO RUN AND DID NOT REPORT BACK ***"
            : "MOTORS TOLD TO RUN - DID THEY REPORT BACK?");

        foreach (var status in findings.Statuses.Take(6))
        {
            text.AppendLine();
            text.AppendLine($"  {ReportText.Wrap(status.Summary, 2)}");
            text.AppendLine($"      {ReportText.Wrap(status.ConfirmSentence, 6)}");

            if (status.MachineWaitedFor is { } waited)
            {
                text.AppendLine($"      the machine's own words: \"{waited}\"");
            }
        }

        if (failures.Count == 0) return;

        text.AppendLine();
        text.AppendLine("  The command went out and the confirmation did not come back, so the motor was");
        text.AppendLine("  not turning. Check the contactor, its auxiliary contact, the overload and the");
        text.AppendLine("  confirmation wiring before anything further down the process.");

        var healthy = findings.Healthy.Select(h => h.Motor).ToList();
        if (healthy.Count > 0)
        {
            text.AppendLine($"  For comparison, {string.Join(" and ", healthy)} confirmed normally in this "
                            + "same log.");
        }
    }

    /// <summary>
    /// Drive fault codes, high up and on their own. The drive says exactly what is wrong and on
    /// which axis - "F02 Encoder Wiring Fault on Axis-FixedSidePusher" - but buried in a block of
    /// quoted context it reads as just more log noise, and the report ends up advising a generic
    /// sensor check when the machine has already named the part.
    /// </summary>
    private static void AppendDriveFaults(StringBuilder text, KnowledgeFindings knowledge)
    {
        if (knowledge.DriveFaults.Count == 0) return;

        var faults = knowledge.DriveFaults.Where(f => f.Code.IsFault).ToList();
        var states = knowledge.DriveFaults.Where(f => !f.Code.IsFault).ToList();

        text.AppendLine();
        text.AppendLine("*** DRIVE FAULT CODES - THE MACHINE HAS NAMED THE PROBLEM ***");

        foreach (var sighting in faults)
        {
            text.AppendLine();
            text.AppendLine($"  {sighting.Code.Code}  {ReportText.Wrap(sighting.Code.Meaning, 7)}");
            text.AppendLine($"      x{sighting.Occurrences}{sighting.Where}"
                            + Window(sighting) + ".");
            text.AppendLine($"      Check: {ReportText.Wrap(sighting.Code.WhatToCheck, 13)}");

            if (sighting.Code.CallCyberLogix)
            {
                text.AppendLine("      This one is not fixable on site - CyberLogix need to see it.");
            }
        }

        foreach (var sighting in states)
        {
            text.AppendLine();
            text.AppendLine($"  {sighting.Code.Code}  {ReportText.Wrap(sighting.Code.Meaning, 6)}");
            text.AppendLine($"      x{sighting.Occurrences}{sighting.Where}{Window(sighting)}. "
                            + "Not a failure in itself.");
        }

        AppendElectronics(text, knowledge);
    }

    private static string Window(MotionControllerSighting sighting)
    {
        if (sighting.FirstSeen is not { } first || sighting.LastSeen is not { } last) return string.Empty;

        return first == last
            ? $", at {first:hh\\:mm\\:ss}"
            : $", from {first:hh\\:mm\\:ss} to {last:hh\\:mm\\:ss}";
    }

    /// <summary>
    /// Which electronics the axes run on. F-codes come from the CLX drives, so on a machine that
    /// mixes CLX and Omron it is worth knowing which family the faulting axis belongs to.
    /// </summary>
    private static void AppendElectronics(StringBuilder text, KnowledgeFindings knowledge)
    {
        var hardware = knowledge.AxisHardware;

        text.AppendLine();
        text.AppendLine("  These are CyberLogix CLX drive codes, read off the drive's status display.");

        if (!hardware.Any) return;

        text.AppendLine($"  This machine's axes: {hardware.Summary}.");

        if (hardware.IsMixed)
        {
            text.AppendLine("  It runs both families, so check the faulting axis is a CLX one:");
            foreach (var axis in hardware.InUse.OrderBy(a => a.Family).ThenBy(a => a.DisplayName))
            {
                text.AppendLine($"    {axis.DisplayName,-46} {AxisHardwareMap.Describe(axis.Family)}");
            }
        }

        if (hardware.HasOmron) AppendOmronNote(text);
    }

    /// <summary>
    /// What to do when the axis in question is an Omron one. The CLX codes above do not apply to
    /// it, so the drive has to be read directly - it shows its own alarm as "Er" and two bytes.
    /// </summary>
    private static void AppendOmronNote(StringBuilder text)
    {
        text.AppendLine();
        text.AppendLine("  The Omron axes run 1S-series drives (R88D-1SN..-ECT) on EtherCAT. The CLX codes");
        text.AppendLine("  above do not apply to them. An Omron drive shows its own alarm as \"Er\" and two");
        text.AppendLine("  bytes - Er 16 00 is Overload - so ask site to read the display and the LEDs:");

        foreach (var (name, meaning) in OmronServoDrives.Indicators.Take(4))
        {
            text.AppendLine($"    {name,-9} {ReportText.Wrap(meaning, 14)}");
        }

        text.AppendLine();
        text.AppendLine("  Safety: CHARGE stays lit after power off. Wait the drive's discharge time before");
        text.AppendLine("  touching anything - 10 minutes on the 400 V models, 15 to 20 on 100 and 200 V.");
        text.AppendLine("  A dark display is not proof the bus is discharged.");
    }

    private static void AppendUnits(StringBuilder text, SpidaLogAnalysis analysis)
    {
        text.AppendLine();
        text.AppendLine("UNITS ATTEMPTED");

        if (analysis.Cycles.Count == 0)
        {
            text.AppendLine("  No unit boundaries could be found in MachineLog.txt.");
            return;
        }

        text.AppendLine($"  {analysis.Cycles.Count} attempt(s) found across {analysis.MachineLogLines} log lines.");
        text.AppendLine();

        foreach (var (cycle, label) in new[] { (analysis.Last, "Last attempt"), (analysis.SecondToLast, "Previous attempt") })
        {
            if (cycle is null) continue;

            text.AppendLine($"  {label} (#{cycle.Number}, {cycle.Start:hh\\:mm\\:ss} to {cycle.End:hh\\:mm\\:ss}): {cycle.Outcome}");

            foreach (var fault in cycle.Faults.TakeLast(3))
            {
                var atStep = fault.StepAtFault is { } step ? $" at step {step}" : string.Empty;
                text.AppendLine($"      {fault.Time:hh\\:mm\\:ss}{atStep}: {fault.Text}");
            }

            // The guide asks for quoted context so the reading can be checked against the file.
            var lastFault = cycle.Faults.LastOrDefault();
            if (lastFault is not null && lastFault.Context.Count > 0)
            {
                text.AppendLine();
                text.AppendLine($"      What the machine was doing around that fault:");
                foreach (var line in lastFault.Context)
                {
                    text.AppendLine($"        {line.Display}");
                }
            }

            text.AppendLine();
        }
    }

    private static void AppendRepeats(StringBuilder text, SpidaLogAnalysis analysis)
    {
        text.AppendLine("DOES IT REPEAT?");

        if (analysis.RepeatedFaults.Count == 0)
        {
            text.AppendLine("  No machine fault repeated across attempts. Treat what you see as a one-off unless");
            text.AppendLine("  the customer says otherwise.");
        }

        var atSameStep = false;

        foreach (var repeat in analysis.RepeatedFaults)
        {
            var atStep = repeat.StepAtFault is { } step
                ? $", every time at step {step}"
                : repeat.StepsVary ? ", at a different step each time" : string.Empty;

            if (repeat.StepAtFault is not null) atSameStep = true;

            var where = repeat.CycleNumbers.Count > 8
                ? $"{repeat.CycleNumbers.Count} attempts, first {string.Join(", ", repeat.CycleNumbers.Take(4))}"
                  + $" and last {repeat.CycleNumbers[^1]}"
                : $"attempts {string.Join(", ", repeat.CycleNumbers)}";

            text.AppendLine($"  x{repeat.Occurrences} across {where}{atStep}:");
            text.AppendLine($"      {repeat.Text}");
        }

        // Only said where it is earned. Printing it under whatever repeated sent a technician to
        // meter a sensor for an operator pressing stop, on a real M22215 report.
        if (atSameStep)
        {
            text.AppendLine();
            text.AppendLine("  A fault landing at the same step across attempts is worth treating as real - a");
            text.AppendLine("  sensor, a connector or the mechanism at that step - rather than a one-off.");
        }

        AppendOperatorActions(text, analysis);
    }

    /// <summary>
    /// What the operator kept doing. Worth knowing and never a fault.
    /// <para>
    /// "Stop All Pressed" 29 times used to head the repeating-fault list on a real M22215 report,
    /// under a sentence saying a repeat points at hardware. It points at a person. An operator
    /// stopping the machine over and over is a real signal - about the operator's experience of
    /// the machine, which is a different question from what is broken.
    /// </para>
    /// </summary>
    private static void AppendOperatorActions(StringBuilder text, SpidaLogAnalysis analysis)
    {
        if (analysis.OperatorActions.Count == 0) return;

        text.AppendLine();
        text.AppendLine("  What the operator did (not faults):");

        foreach (var action in analysis.OperatorActions.Take(5))
        {
            text.AppendLine($"    x{action.Occurrences} across {action.CycleNumbers.Count} attempt(s): {action.Text}");
        }

        var most = analysis.OperatorActions[0];

        if (most.Occurrences >= 5)
        {
            text.AppendLine($"  Somebody did that {most.Occurrences} times in one session. That is worth asking about -");
            text.AppendLine("  it says how the machine was behaving, even where nothing logged a fault.");
        }
    }

    private static void AppendErrors(StringBuilder text, SpidaLogAnalysis analysis)
    {
        text.AppendLine();
        text.AppendLine("ERRLOG.TXT");

        if (analysis.RealErrors.Count == 0)
        {
            text.AppendLine("  Nothing left after filtering cosmetic, out-of-window and background entries.");
        }
        else
        {
            text.AppendLine($"  {analysis.RealErrors.Count} entr(ies) worth a look:");
            foreach (var error in analysis.RealErrors.Take(10))
            {
                text.AppendLine($"      {error.Timestamp:yyyy-MM-dd HH:mm:ss}  {error.Text}");
            }
        }

        if (analysis.CosmeticErrors.Count > 0)
        {
            text.AppendLine($"  {analysis.CosmeticErrors.Count} ignored as cosmetic (a matching Change.log entry at the");
            text.AppendLine("      same second shows the save actually worked).");
        }

        if (analysis.ErrorsOutsideLogWindow.Count > 0)
        {
            text.AppendLine($"  {analysis.ErrorsOutsideLogWindow.Count} fall outside the MachineLog window, so there is no");
            text.AppendLine("      machine context for them.");
        }

        if (analysis.RepeatingBackgroundErrors.Count > 0)
        {
            text.AppendLine("  Background errors seen every session (not this complaint):");
            foreach (var noise in analysis.RepeatingBackgroundErrors.Take(5))
            {
                text.AppendLine($"      {noise}");
            }
        }
    }

    private static void AppendChanges(StringBuilder text, SpidaLogAnalysis analysis)
    {
        text.AppendLine();
        text.AppendLine("SETTINGS CHANGED AROUND THIS SESSION");

        if (analysis.RecentSettingChanges.Count == 0)
        {
            text.AppendLine("  Nothing was changed around this session.");
        }
        else
        {
            foreach (var change in analysis.RecentSettingChanges.Take(15))
            {
                text.AppendLine($"  {change.Display}");
            }

            text.AppendLine();
            text.AppendLine("  Listed as context. Ask the customer to confirm whether any of these are related");
            text.AppendLine("  rather than assuming they are.");
        }

        AppendLatestChanges(text, analysis);
    }

    /// <summary>
    /// The last few changes whatever their date. A machine can run for months on a setting
    /// somebody changed once, so the most recent change is worth seeing even when it is old -
    /// otherwise this section reads "none" on a machine whose settings were quietly altered.
    /// </summary>
    private static void AppendLatestChanges(StringBuilder text, SpidaLogAnalysis analysis)
    {
        if (analysis.LatestSettingChanges.Count == 0) return;

        var alreadyListed = analysis.RecentSettingChanges.Take(15).ToHashSet();
        var latest = analysis.LatestSettingChanges.Where(c => !alreadyListed.Contains(c)).ToList();

        if (latest.Count == 0) return;

        text.AppendLine();
        text.AppendLine($"  Last {analysis.LatestSettingChanges.Count} change(s) on this machine, whenever they happened:");

        var sessionLocal = analysis.SessionDateUtc.ToLocalTime();
        foreach (var change in latest)
        {
            text.AppendLine($"    {change.Display}{Age(change.Timestamp, sessionLocal)}");
        }
    }

    /// <summary>How long before this bundle a change was made.</summary>
    private static string Age(DateTime changedAt, DateTime sessionLocal)
    {
        var days = (sessionLocal.Date - changedAt.Date).Days;

        return days switch
        {
            < 0 => "   after this file",
            0 => "   same day",
            1 => "   1 day before",
            < 31 => $"   {days} days before",
            < 365 => $"   about {days / 30} month(s) before",
            _ => $"   about {days / 365} year(s) before"
        };
    }

    private static void AppendWhereToLook(
        StringBuilder text,
        DiagnosticFileSummary file,
        SpidaLogAnalysis analysis,
        KnowledgeFindings? knowledge)
    {
        text.AppendLine();
        text.AppendLine("WHERE TO START LOOKING");

        var leads = new List<string>();

        // How long before the end of the log something happened, since the file is taken within
        // minutes of the problem and the end of the log is where the problem is.
        string Age(TimeSpan? when) =>
            when is { } t && analysis.LogEnd is { } end && end > t
                ? $", {MachineCycle.Describe(end - t)} before the log ends"
                : string.Empty;

        // The last thing the machine did comes first, whatever else is in the log.
        if (analysis.LastNotableEvent is { } lastEvent)
        {
            leads.Add($"The machine's last act was {lastEvent.Tag} \"{lastEvent.Description}\" at "
                      + $"{lastEvent.Time:hh\\:mm\\:ss}{Age(lastEvent.Time)}. Start at the end of the log "
                      + "and work back.");
        }

        // A machine still saying what it wants when the log runs out, with something it named
        // sitting unsatisfied. That is as close to the machine answering the question as it gets.
        if (knowledge?.WaitingOn is { Any: true, StillWaitingAtTheEnd: true } waiting
            && waiting.SomethingIsUnsatisfied)
        {
            var orphan = waiting.Named.FirstOrDefault(sig => sig.PartnerMissing);

            var what = orphan is not null
                ? $"{orphan.Id.Name} is logged at one address only and its partner "
                  + $"{orphan.MissingPartnerAddress} never appears - check that sensor on the other side"
                : waiting.NotOn.Count > 0
                    ? $"{string.Join(", ", waiting.NotOn.Select(sig => sig.Id.Name))} was off"
                    : $"{string.Join(", ", waiting.NotReady.Select(a => a.Name))} was not in position";

            leads.Add($"The machine was still saying \"{waiting.Message}\" when the log ran out"
                      + $"{Age(waiting.To)}, and {what}.");
        }

        // The machine put in position over and over with no cut. Above the motor checks because
        // when it fires it is the complaint itself.
        if (knowledge?.CutNotTaken is { Any: true } cut)
        {
            var mode = cut.CutModeDuringWaits.Length > 0
                ? $" Cut mode was {cut.CutModeDuringWaits} throughout, and nothing in this log ever cut in that mode."
                : string.Empty;

            leads.Add($"The machine was driven into position {cut.Waits.Count} times with no cut following"
                      + $"{Age(cut.Waits[^1].RequestedAt)}, and the operator retried every time.{mode} "
                      + "Check the two-hand buttons at the PLC input, then whether the PLC accepts them "
                      + "in that mode.");
        }

        // A motor that never reported itself running is as concrete as it gets, and it is
        // normally the exact thing the operator wrote down.
        foreach (var failure in knowledge?.MotorConfirm.Failures.Take(2)
                                ?? Enumerable.Empty<MotorConfirmStatus>())
        {
            leads.Add($"{failure.Motor} was commanded on at {failure.LastCommandedOn:hh\\:mm\\:ss}"
                      + $"{Age(failure.LastCommandedOn)} and {failure.ConfirmTag} did not come on. "
                      + $"{failure.ConfirmSentence} Check the contactor, its auxiliary contact, the "
                      + "overload and the confirmation wiring.");
        }

        // A drive fault outranks everything else: the machine has named the failed part, so a
        // generic "check the sensor and cable" is worse than useless beside it. Most recent first.
        foreach (var fault in knowledge?.DriveFaults.Where(f => f.Code.IsFault).Take(2)
                              ?? Enumerable.Empty<MotionControllerSighting>())
        {
            leads.Add($"{fault.Code.Code} {fault.Code.ShortMeaning}{fault.Where} (x{fault.Occurrences}"
                      + $"{Age(fault.LastSeen)}) - {fault.Code.WhatToCheck}");
        }

        // Something physically stopping the machine homing outranks everything else - nothing
        // else can happen until it is cleared.
        if (knowledge?.HomeInterlock.Refused.FirstOrDefault(a => a.Blocking.Count > 0) is { } refused)
        {
            leads.Add($"The machine was told to home at {refused.Time:hh\\:mm\\:ss} and {refused.Outcome}, "
                      + $"with {string.Join(" and ", refused.Blocking.Select(b => b.Display))}. All four product "
                      + "sensors have to read 0 before it will home - start by clearing that one.");
        }

        // A fault we already understand beats anything worked out from the log shape alone.
        foreach (var match in knowledge?.MatchedFaults.Take(2) ?? Enumerable.Empty<MatchedFault>())
        {
            leads.Add($"\"{match.SeenAs}\" - {match.Known.WhatToCheck.FirstOrDefault() ?? match.Known.Meaning}");
        }

        foreach (var issue in knowledge?.IssuesSeenInThisLog.Take(1)
                              ?? Enumerable.Empty<KnownIssue>())
        {
            leads.Add($"Known problem on this machine, and this log shows signs of it: {issue.Title}. "
                      + "Rule it in or out before looking elsewhere.");
        }

        foreach (var glitch in knowledge?.PlatePresentEvents.Where(e => e.Verdict == PlatePresentVerdict.SensorGlitch).Take(1)
                               ?? Enumerable.Empty<PlatePresentEvent>())
        {
            leads.Add($"A plate present sensor glitched at {glitch.Time:hh\\:mm\\:ss}{Age(glitch.Time)} on the "
                      + $"{glitch.Side.ToLowerInvariant()} side with nothing physically moving - check that "
                      + "sensor and its cable.");
        }

        // What to check depends on what failed. Telling somebody to meter a sensor because a
        // servo reported a movement error sends them to the wrong end of the machine.
        foreach (var repeat in analysis.RepeatedFaults.Take(2))
        {
            var where = repeat.Text.Contains("servo", StringComparison.OrdinalIgnoreCase)
                        || repeat.Text.Contains("movement error", StringComparison.OrdinalIgnoreCase)
                        || repeat.Text.Contains("following error", StringComparison.OrdinalIgnoreCase)
                ? "check the drive, the coupling and whether anything is fouling that axis - "
                  + "a movement error is the drive saying it could not get where it was told to go"
                : "check the sensor, cable and connector for that mechanism";

            leads.Add($"The repeating fault \"{repeat.Text}\" - {where}.");
        }

        if (analysis.Last?.Faults.LastOrDefault() is { } lastFault && analysis.RepeatedFaults.Count == 0)
        {
            leads.Add($"The last attempt stopped on \"{lastFault.Text}\" - start there.");
        }

        if (!string.IsNullOrWhiteSpace(file.SupportIssue))
        {
            leads.Add($"The operator's own words: \"{file.SupportIssue}\".");
        }

        if (analysis.RecentSettingChanges.Count > 0)
        {
            leads.Add($"{analysis.RecentSettingChanges.Count} setting(s) changed around this session - rule them in or out.");
        }

        if (leads.Count == 0)
        {
            text.AppendLine("  Nothing in the logs points anywhere specific. Confirm the complaint with the");
            text.AppendLine("  customer before going further.");
            return;
        }

        foreach (var lead in leads) text.AppendLine($"  - {ReportText.Wrap(lead, 4)}");
    }

    private static void AppendQuestions(StringBuilder text, SpidaLogAnalysis analysis, KnowledgeFindings? knowledge)
    {
        text.AppendLine();
        text.AppendLine("QUESTIONS FOR THE CUSTOMER");

        var questions = new List<string>();

        // Questions that matter on this particular machine go first.
        if (knowledge?.Knowledge is { } machine)
        {
            questions.AddRange(machine.CustomerQuestions);
        }

        if (analysis.RepeatedFaults.Count > 0)
        {
            questions.Add("Does this happen on every panel, or only some?");
            questions.Add("When did it start - was there a day it began?");
            questions.Add("Has anyone been working near that sensor, cable or guard recently?");
        }
        else
        {
            questions.Add("Has this happened more than once, or was it a one-off?");
            questions.Add("What was the operator doing at the time?");
        }

        if (analysis.RecentSettingChanges.Count > 0)
        {
            questions.Add("Was anything adjusted on the machine that day, and by whom?");
        }

        questions.Add("Does the machine recover if it is restarted, and for how long?");

        foreach (var question in questions.Distinct().Take(6))
        {
            text.AppendLine($"  - {ReportText.Wrap(question, 4)}");
        }
    }
}
