# Sending a case back to Claude

The analysis only gets better by meeting real files. Every improvement in it so far came from a
support person saying *"it should have spotted X, and here's how I knew"*. This turns that into a
button rather than a conversation.

## Using it

1. Select a bundle, then **Send to Claude** on the toolbar (or right-click → *Send feedback to
   Claude*). The app analyses it first, so the notes are written against the same report that
   goes in the package.
2. The window shows that report on the left and four fields on the right.
3. **Create package** writes a `.zip` to `%AppData%\DiagFileMonitor\Feedback\` and *Show me the
   file* opens it in Explorer.
4. Attach the zip to a Claude session. It needs no explanation — `README.md` says what it is and
   `PROMPT.md` says what to do.

## The four fields, and why they are separate

| Field | Why |
|---|---|
| **How did it do?** | A working case is worth keeping too — it becomes a regression test rather than a change. |
| **What was actually wrong** | The ground truth. Without it there is nothing to check a change against. |
| **How did you know?** | **The one that becomes code.** *"The output came on and the confirm input never did"* can be turned into a check. *"The saw was broken"* cannot. |
| **What should the report have done** | Optional. A suggestion, not a requirement — the code may want a different shape. |

A single free-text box would lose the third, which is the whole point.

## What is in the package

```
feedback-M20421-2026-09-15-1831-missed.zip
├── README.md              what this is, and a note that it carries customer data
├── PROMPT.md              the learning prompt - start here
├── feedback.md            the notes, unedited
├── report-produced.txt    exactly what the app said
├── context.json           machine, serial, customer, versions, dates, app version
└── bundle/                the diagnostic files the analysis actually read
```

The filename carries the serial, the date and the verdict, so a folder of them sorts sensibly and
you can see what each one is without opening it.

## Decisions worth knowing

- **The extracted files go in, not the original `.szip`.** A bundle can be re-processed; the
  extracted copy is what produced the report being complained about.
- **A single file over 25 MB is left out and named** — in the result *and* in the package's own
  README, so the person reading it knows what is missing. The cap is generous because a multi-
  megabyte `MachineLog.txt` is the whole point and compresses to very little; it exists to stop a
  stray video or database file going along for the ride.
- **A bundle whose files have been cleaned up still packages** — the report and the notes are
  worth having on their own, with a note explaining what is missing.
- **Notes with no ground truth are refused.** A verdict and a suggestion with nothing to check
  them against produce a package nobody can act on.
- **The package carries customer name and site.** That is deliberate — the machine's identity is
  context the analysis needs — and the README says so before it goes anywhere.

---

## Case 1 - AOR1694, 16 Sep 2026: "the gun fired by itself"

The first real case to come back through this loop, and the first change made because of one.

**Verdict:** missed it. **Raised by:** mark.

**What the operator said:** the floating side lower gun fired on its own when they pressed stop all.

**What the report said at the time:** that the machine's last act was a `SyncMove Axis Disable` at
07:53:39. True, and no use to anybody. It never mentioned the two-hand control, never said the
machine had been stopped by hand, and never noticed that the step it stopped in normally does
something else entirely.

### What the technician asked for

Work backwards to the last two THNTD inputs (1 then 0), say when they happened and how long the
button was held. Then say what happened after the release. Then compare that against what the same
step did the other times it came up. And note that `WallExtruderStep 0` is an operator stopping the
machine from the HMI, while `SidePLCStep 0` is a different counter resetting - the two must not be
read as the same thing.

### What the data said when checked

Everything in the note held up, and one thing was sharper than it first looked:

| | |
|---|---|
| THNTD presses in the log | 11, median hold 1.34s |
| The last press | 07:53:34.643, held **0.14s** - a jab, not a press |
| Step 1310 occurrences | 6. Five went on to step 1400; the sixth went to step 0 |
| Dwell at 1310 the last time | 3.4s, against 1.4s typically |
| Firings commanded after the last press | **none** |

That last row is the finding. Every firing in the log is four gun outputs on together and off
about 230ms later, and the final one is at 07:53:30 - four seconds *before* the last THNTD press.
So if a gun went off after that press, **the PLC never asked it to**. A gun firing with no output
commanded leaves no trace in this log at all, which points at the valve and the air side rather
than the program. The absence is the evidence.

### What changed because of it

- `Knowledge/TwoHandControlCheck.cs` - reads every THNTD press with its hold time, groups gun
  outputs fired together into single firings, and reports when nothing was commanded to fire after
  the operator's last press.
- `Knowledge/StepOutcomeCheck.cs` - compares the last step against every other time the machine was
  at that step. It needs no idea what the numbers mean, which is the point, since most of them are
  still undecoded. It also separates the main step counter from a secondary PLC's own counter,
  which were previously read as one sequence.
- A new **WHAT THE OPERATOR LAST ASKED FOR** section, placed directly after HOW IT ENDED.
- The confirmed step meanings and the three-press firing sequence are now in
  `RakedWallExtruderKnowledge` and print under **HOW THIS MACHINE IS DRIVEN**.
- `AOR1694`, `M21036`, `M21737` and `M21844` added to the known serials, and `RakingWallExtruderDG`
  to the models this knowledge covers.

### Still open from this case

- Whether a 0.14s jab is enough to advance the sequence, or whether a short press behaves
  differently from a held one.
- Whether a gun can fire pneumatically with no output commanded. The log cannot settle it either
  way; somebody has to look at the valve.
- What the rest of the WallExtruderStep numbers mean. 1310 and 0 are now known. The rest are not.

### What this case taught about the loop itself

The technician's note was worth more than the bundle. The bundle had been analysed already and the
report was wrong; what made the difference was somebody writing down *how they read it* - work
backwards from the last operator action, and compare a step against its own history. Both of those
are now checks that run on every export, including machines nobody has written up.

## Case 2 - M22215, 17 Sep 2026: "manual to 335 thntd, no action"

SprintM600 saw at Akarana Timbers, Christchurch. Verdict: **missed it**.

The operator drove the trolley to a position by hand, pressed the two-hand control, and nothing
happened. The report of the day said the log ended on an axis status and the machine had been
stopped from the HMI. Both true. Neither any use.

### What the log actually says

The support person's reading was right, and the evidence is stronger than they realised:

- **THNTD appears nowhere in the bundle.** Not once, in any file. On this machine the two-hand
  buttons are wired straight into the PLC, so the software only ever sees a press the PLC has
  already accepted. An operator pressing and getting nothing leaves no trace at all.
- The trolley **did** reach 339.1, at 09:00:00.762, 2.2 s after the command.
- Then **not one line was written for 13.1 seconds** until the operator opened the lid. That
  silence is the finding.
- The same thing happened **six times** after the last cut: driven into position, nothing, lid
  opened or reset pressed, try again.
- **Every one of the 27 cuts in that log began with cut mode `Board`. Not one began with `None`,
  and cut mode was `None` for all six attempts.** That is measured in this log, not a rule anyone
  has told us, but it makes the suspicion concrete.

One correction to the report of the case: the servos disabling was not a second symptom. The
operator opened the lid, and the interlock dropped them 0.17 s later. Eight of the thirteen lid
requests in that log are followed by a servo disable inside half a second.

### What was built

`CutNotTakenCheck`, and the report section **ASKED FOR A CUT AND NOTHING HAPPENED**.

Generalising it took more care than the detection:

- **Cut cycles are found by the machine's own habit, not by step numbers.** A SprintM600 runs
  10, 20, 30, 40, 0; the M20716 saw goes to 60. Bursts of `BladeCutStep` are grouped by time and
  one counts as a cut when it reaches at least the median top step. That drops the short 5/7/12
  bursts a SprintM600 logs when the blade is raised by hand - count those as cuts and "the last
  cut" moves forward, hiding the very silence being looked for.
- **`Cutmode` is only used where the log has it.** The M20716 saw never writes one.
- **The retry is what stops it crying wolf.** A saw being put away at the end of a shift leaves
  the same idle moves behind - the 100,000 line M20716 control log has five - and not one is
  retried. Checked against that log: 34 cut cycles found, 10 idle moves after the last one, zero
  waits reported.
- Two waits are needed before anything prints. One is a moment; two is a pattern.

### Still unproven

Whether cut mode `None` is *why* the PLC ignored the buttons. The correlation is perfect within
this one log and that is all it is. The report says so in those words and puts it first on the
list to check rather than stating it as fact. **Second sighting would make it inferred; a word
from somebody who knows the PLC would make it confirmed.**

## Case 3 - M21737, 21 Sep 2026: waiting for a plate support that never came down

RakingWallExtruderV3DG at PlaceMakers Wiri. Verdict: **missed it**.

The log ends repeating, 68 times over 26 minutes:

```
Extruder, Waiting for Both Panel Height Servos in position and PlateSupports Down'
```

v1.1 showed that line - the raw tail put it on the page, which is the v1.1 fix working - and then
said nothing about it. The machine had named exactly what it wanted and the report did not go and
look.

### What the log actually says

- **`PlateSupportDown` is logged once**, at `192.168.250.1-1.1`, and reads 1 from 07:49:29 on.
- **Its partner never appears.** Every paired input on this machine is the same bit two lower on
  the module below: `PlateClampUp` 1.8 / 0.10, `TrolleyTopClampOpen` 1.6 / 0.8,
  `TrolleyBottomClampOpen` 1.4 / 0.6. So the partner of 1.1 is **0.3**, and `0.3` appears nowhere
  in the bundle.
- A log records **changes**. An input that never came on never appears at all - which is exactly
  what the second plate support looks like from here, and exactly why a report that only reads
  what is written could not see it.

The support person got there by knowing the machine has two plate supports. The report can get
there by reading the machine's own addressing.

### What was built

`WaitingOnCheck`, and the report section **WHAT IT SAID IT WAS WAITING FOR**.

It takes the last "Waiting for ..." message, pulls the named things out of it, and reports the
state of each one at that moment:

- **Signals**, matched by name. Every word of the signal's name has to appear in the message,
  allowing a plural - so "PlateSupports Down" finds `PlateSupportDown`, and `PlateClampUp` stays
  out of a message about plate supports.
- **Axes**, matched more loosely, because no wording joins "Panel Height Servos" to
  `FloatingSideHeight`. One distinctive word is enough, with the words every axis shares - servo,
  axis, motor, drive, status, node, side - excluded so `FloatingEjectServo` is not dragged in.
- **The missing partner**, where the machine's own pairing offset can be derived from at least two
  pairs that agree. No agreement, no guess.

### Kept quiet where it should be

Checked against three other logs:

| Log | Last waiting message | Reported |
|---|---|---|
| M22215 saw | "waiting for clamps" (369×) | nothing - no signal by that name, and guessing would be worse |
| M20716, 100k lines | "Waiting for Saw Blade Running" | nothing - no signal name matches |
| AOR1694 | none | nothing |

One real fix came out of the cross-check: the pattern was anchored at the start of the line and
the common shape is `Step Condition, Waiting for X`, so it was missing 4,123 messages in one
sample log.

### Still unproven

- **That 0.3 is really the second plate support.** The offset is derived from three pairs that
  agree, which is good evidence and not a wiring diagram. The report says where the partner
  *would* be and tells the reader to go and look.
- **Deliberately conservative matching.** "Waiting for Saw Blade Running" ought to point at
  `IO-SawMotor` and does not, because the words do not line up. Under-reporting beats pointing a
  technician at the wrong sensor, but an I/O map would fix both this and the guess above.
