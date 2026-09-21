# Guard stops - what they cost, not whether they happened

Some of what a Spida machine writes to MachineLog.txt is not a fault. It is the machine refusing
to move until a person has dealt with something. **We would far rather spend the time than have
the machine crash into what it saw, or move with somebody against the bar.** These are worth
having.

They are also not free, and "how much of my day goes on this" is a fair question from a customer.
So the app **counts and times** them, and keeps them **out of the fault list**.

## The three guards, and how each is timed

| Guard | Timed from | Timed to |
|---|---|---|
| Floating head obstruction | first complaint, or `Step = 321` | the step where the sequencer leaves the 320/321 poll |
| Floating side safety bar | `SafetyBarPressed` input goes to 1 | the next sequencer step after the bar is let go |
| "Clear of any moving parts" prompt | the prompt | the THNTD press that answers it |

Each is timed off the thing that actually ends the wait, not off the message. That matters
because **the PLC repeats a prompt while it waits**, so counting log lines ranks the guards
exactly backwards - the loudest one in the log is usually the cheapest.

## What they actually cost

| | M21737 (9.7 h) | M21844 (8.3 h) |
|---|---|---|
| Floating head obstruction | 6 × , 2.7 min | 1 × , 3.8 min |
| "Clear of moving parts" prompt | 25 × , 1.3 min | 42 × , 2.5 min |
| Floating side safety bar | 3 × , 20.8 s | 1 × , 5.2 s |
| **Total** | **34 × , 4.3 min (0.73%)** | **44 × , 6.5 min (1.30%)** |

Around one percent of a shift on both machines. That is the figure to quote.

Note the ranking. On M21844 the moving-parts prompt appeared **42 times** against the floating
head's **once**, and still cost less time. A fault list sorted by frequency would have put the
cheapest guard at the top and buried the four things that genuinely went wrong.

## Why they are out of the fault list

On M21844 the moving-parts prompt alone appeared 42 times - more than every real fault in that
log put together - and each one was answered in about three seconds. Leaving them in meant a
technician read forty lines of the machine checking it was safe to move before reaching anything
broken. The fault list now holds what actually went wrong; the ledger says what the guards cost,
and says plainly that it has taken them out.

## When a guard IS the fault

The knowledge notes are still there, because the guard itself can go wrong:

- A bar switch reading pressed with nobody near it.
- A floating head wait with **no height reduction** behind it that held the machine up for over a
  minute - see `docs/raked-wall-extruder-knowledge.md`.
- A prompt left unanswered for minutes, meaning the operator walked away or could not reach the
  buttons.

Those are reported individually, on top of the time figures.
