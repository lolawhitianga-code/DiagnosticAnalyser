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
