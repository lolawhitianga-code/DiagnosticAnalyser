# Teaching the analyser a new machine

A step-by-step for what to send me, in what order, and why. Written after getting a fair few
things wrong on the Raked Wall Extruder V3 and working out which of them more data would have
fixed and which of them only you could.

---

## Part 1 - What went wrong so far, and what it means for you

Every mistake I made on the V3 fell into one of three buckets. They need different things from
you, so it is worth being clear about which is which.

### Bucket A - I made a rule out of too few examples

I predicted a missing input address at `192.168.250.1-0.3` by spotting that three pairs of I/O
sat two bits apart. The real address was `2.7`, and `0.3` is not an input at all - it is an
output. There is no single pairing offset on that machine: some pairs are 2 bits apart, some 11,
some adjacent.

Later I found the same thing with sides. Module 4 runs lower-bit-is-fixed for the grippers and
the plate clamp, then reverses for the horizontal stud clamp.

**What fixes it:** more than one machine of the same model. One log gives a pattern. Two logs
tell you whether it is a pattern or a coincidence.

### Bucket B - I measured the wrong thing

I counted how often a message appeared. But the PLC **repeats a message while it waits**, so the
count measures how long something took, not how often it happened - and I then read the gap
between two repeats (0.18 s) as how quickly the machine recovered. Both wrong, in a way that made
the healthy machine look worse than the stopped one.

**What fixes it:** you telling me **what ends the wait**. Once I knew the "clear of moving parts"
prompt ends at a THNTD press, the measurement became obvious and correct.

### Bucket C - I did not know what the message meant, and no log could tell me

I wrote up "Unsafe to move Floating Head" as the fault that stopped M21844. It is not a fault. It
is the guard working, normal going from a taller panel to a shorter one, and we would rather
spend that time than crash the head.

**Two machines and 166,000 lines of log did not contain that.** One sentence from you did.

**This is the bottleneck.** Buckets A and B I can mostly solve with files. Bucket C I cannot
solve at all without you. So the steps below are ordered to get the Bucket C answers out of you
quickly and cheaply, and let the files do the rest.

---

## Part 2 - The fleet, and the size of the job

Fourteen families, and **every one ships in a CLX and an Omron version with different node
addresses**. That is 28 cells to fill, not 14.

| # | Family | I/O map | Fault notes | Seen |
|---|---|---|---|---|
| 1 | Sprint M600 | - | - | - |
| 2 | Component Nailer V1 | - | - | - |
| 3 | Component Nailer V2 | - | - | AOR1613 (bundle only) |
| 4 | Component Maker | - | - | - |
| 5 | Wall Extruder DG | - | - | - |
| 6 | Wall Extruder MG | - | - | - |
| 7 | Raked Wall Extruder V1 | - | - | - |
| 8 | Raked Wall Extruder V2 | - | - | AOR1694 (serial-port) |
| 9 | **Raked Wall Extruder V3** | **83 points, network-addressed** | **16 faults + 3 guards** | M21737, M21844 + 5 |
| 10 | Rapid Stop | - | - | - |
| 11 | **Tornado M500** | - | **12 faults** | M20421 |
| 12 | Wall Sheather | - | - | - |
| 13 | Puck Table | - | - | - |
| 14 | Nocking Station | - | - | - |
| 15 | Apollo | - | - | - |

(AOR1694 reports itself as `RakingWallExtruderDG` - which of V1 or V2 that is, I do not know.)

**So: 1 cell of 28 is properly done, and 1 more is half done.**

### The two control systems

They are already distinguishable in the bundles, two independent ways that have always agreed:

| | Addresses | Axis status lines | Seen on |
|---|---|---|---|
| Network-addressed | `192.168.250.1-4.2` | `Node0 Status` … `Node5 Status` | M20716, M21737, M21844 |
| Serial-port | `COM7-6.5` | `FloatingSidePuller Status` - by name | AOR1694 |

The app now reads this off the log and **refuses to apply an I/O map from the wrong platform**,
or from any platform when it cannot tell. Before this, a V3 on the other control system would
have been handed these addresses - the right signal name against the wrong terminal.

**One thing I need from you:** which of those two is the CLX and which is the Omron? The evidence
leans one way, but naming it from a lean is exactly what put the M21737 address two bits and a
module away from the real one. It is a one-line change once you say.

### A bonus, if the platforms line up

The serial-port machines **name their axes**; the network ones only number them (`Node0`…
`Node5`). If the same physical axes sit in the same order on both versions of a model, a
serial-port log would name the nodes for its network-addressed twin. That has been on the
unproven list since the start and I could never tie it down - because the answer was on the other
platform all along.

I am not going to assume it. But a matched pair of logs - same family, one of each control
system - would settle it, and would be worth more than either log on its own.

### Where to start

Twenty-eight cells is a lot, so the order matters more than it did. What I need from you first
is not files, it is a ranking:

> Of those fourteen, which three or four generate the most support calls? And for each, is it
> mostly CLX or mostly Omron in the field?

Then we do those, properly, one at a time. A model with a real I/O map, a guard list and a normal
baseline is worth more than fourteen models with a fault list each.

## Part 3 - The steps

### Step 0 - Rank them (done the list; now the order)

The fourteen are known. What is missing is which ones matter:

> Which three or four generate the most support calls, and is each one mostly CLX or mostly
> Omron in the field?

This decides everything below. There is no point mapping a model you have three of when there
are forty of something else, and no point mapping the CLX version if the field is all Omron.

### Step 1 - One long log from a good day (per model)

**The single most valuable thing you can send.** A full shift from a machine that was working
properly.

Why a full shift: a log only records **changes**, so an input that never moved is invisible. I
measured how fast the I/O list fills up on the two V3 machines:

| Log length | Points found |
|---|---|
| 5 min | 13-27% |
| 15 min | 40-93% |
| 30 min | 93-99% |
| Full shift | 100% |

Half an hour gets you most of it. **The last few points take hours** - M21844 was still finding
new ones at the seven hour mark. And those rare ones are exactly the ones that matter, because a
point that never appears in a log is what a broken sensor looks like.

**What makes a good day good:**

- A busy shift, start to finish, including the morning startup (homing exercises everything).
- **Say which control system it is** if you know, though the app now reads it off the log.
- **Varied product.** Different panel sizes, different timber, both sides working. A day of one
  repeated job will miss half the machine.
- Nothing much went wrong. If something did, say so - a "good day" I take on trust and that
  wasn't is worse than no baseline at all.

Send the whole `.szip`, not just MachineLog.txt. The other files matter more than they look -
`maint_data.json` turned out to be the only place an output carries its real name and its side.

**One specific ask:** take the backup **during production**, not at the end of the day after a
break. `maint_data.json` only holds the **current hour**. On M21737 the hour did not line up with
the log and I got nothing from it; on M21844 it did and I named 13 outputs to a hundredth of a
second.

### Step 2 - The same model, a different machine

Another full shift, different serial, ideally a different site. **Same control system** - a CLX
and an Omron machine of the same model are two separate mapping jobs, not two samples of one.

If you can also send the *other* control system's version of the same model, send it, but as a
separate pair rather than instead of the second machine. That is the matched pair in Part 2 that
might name the nodes.

This is what turns "inferred from one machine" into something I would put in front of a customer.
On the V3 it caught my wrong address, found 8 points the first machine never used, and confirmed
all 75 of the others sat at identical addresses.

Without this I will keep telling you things are "inferred, one machine, not checked against a
wiring diagram" - which is honest, but not much use.

### Step 3 - The guard list (10 minutes, and I cannot do it without you)

**This is the Bucket C step, and it is the one that pays off most per minute of your time.**

For each model, go through the messages the machine writes and tell me which ones are the machine
**refusing to move until a person deals with something**, rather than something being broken.

For each one, three things:

| | Example |
|---|---|
| The message | "**Ensure You Are Clear Of Any Moving Parts**..." |
| What it actually is | The machine asking if everyone is clear before it moves |
| **What ends the wait** | The operator presses the clamp/fire buttons |

That third column is the one that becomes code. "The operator sorts it out" cannot be measured.
"A THNTD press" can.

Also worth flagging in the same pass:

- Messages that are an **operator action**, not a fault ("Stop All Pressed" appeared 29 times in
  one log and I had it down as hardware).
- Messages that are **advisory** - the software commenting on how a job is set up.
- Anything the machine says constantly that means nothing.

On the V3 this one step took the fault list from 202 entries to 4.

### Step 4 - The normal numbers (5 minutes)

Per model, roughly:

- How long should one cycle take? (panel, board, whatever the unit is)
- What is an acceptable fault rate? I flagged M21737 at 12.14% without knowing whether that is
  terrible or Tuesday.
- How many units in a good shift?
- What does the machine do at startup that is not worth reporting?

I can measure the machine against itself without these. I cannot tell you whether the machine is
any good without them.

### Step 5 - Labelled cases, ongoing

Use the **Send to Claude** button - it is already built for this and the package has everything I
need. What matters is the fields:

- **Send the good ones too.** A bundle where the report got it right becomes a regression test. If
  I only ever see failures I cannot tell whether a change made things better or just different.
- **"How did you know?" is the field that becomes code.** "The output came on and the confirm
  input never did" turns into a check. "The saw was broken" does not.
- **Fill in the issue text.** The M21844 bundle arrived with the operator issue field blank, so I
  had to guess why it had been sent - and guessed wrong. One line would have saved the whole
  detour.

---

## Part 4 - A form you can fill in per model

Copy this, fill in what you know, leave the rest blank. Blanks are fine - "don't know" is a
useful answer and stops me inventing one.

```
MODEL: ..................................    roughly ...... in the field

WHAT IT MAKES, IN ONE LINE:
  ..........................................................................

THE UNIT OF WORK:        panel / board / frame / other: ..........
TYPICAL CYCLE TIME:      ...... seconds
GOOD SHIFT OUTPUT:       ...... units
ACCEPTABLE FAULT RATE:   ...... %

GUARDS - the machine waiting on a person, not a fault
  Message .................................................................
    what it is ............................................................
    what ends the wait ....................................................
  (repeat)

OPERATOR ACTIONS - a person pressed something, not a failure
  ..........................................................................

NOISE - says it constantly, means nothing
  ..........................................................................

THE THREE FAULTS THAT ACTUALLY GET CALLED IN
  1. ................................  usual cause: ........................
  2. ................................  usual cause: ........................
  3. ................................  usual cause: ........................

WHICH SIDE IS WHICH (if it has sides)
  Is there any message, lamp or test that names one side while only that side
  moves? That is what lets me attach a side to an address.
  ..........................................................................

ATTACHED
  [ ] full shift, good day, machine 1   serial ..........  date ..........
  [ ] full shift, good day, machine 2   serial ..........  date ..........
  [ ] taken during production, not after a break
```

---

## Part 5 - Things that will waste your time

- **Short exports.** A 20 minute bundle is fine for a specific fault and nearly useless for
  learning a machine.
- **Only bad days.** I cannot tell abnormal from normal without normal. This is why the report
  now establishes what normal looks like before it reads the end of the log.
- **Bundles with no issue text.** See M21844.
- **Reading the software version off a screenshot.** SDN shows its own version in the title bar
  whatever files it has open, so a screenshot taken on a support laptop reports the laptop, not
  the machine. The version comes from the bundle.
- **A wiring diagram would be better than all of it.** If an I/O manifest or electrical schedule
  exists for any of these models, that replaces Steps 1 and 2 entirely and turns "inferred" into
  "confirmed". I have been rebuilding from logs something that may well already be on a drawing.

---

## Part 6 - What I will send back

Per model, once I have Steps 1-3:

- An I/O list, with confidence marked, and the points where two machines disagree called out.
- Guards timed rather than counted, with what they cost as a share of the shift.
- A fault list with the guards and operator actions taken out.
- A written note of everything I am **not** sure about, so nothing gets quoted to a customer as
  fact when it is a guess.

That last one matters. Almost every problem in this list came from me being confident about
something I had inferred from one example.
