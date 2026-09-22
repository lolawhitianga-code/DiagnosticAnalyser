# Wall Sheather - the M20957 good panel, 22 September 2026

Trusstech 2019 Ltd, Omron build, SDN reported as V2.3.0.0 in the bundle. The operator wrote
**"good panel"** in the issue box, so this is a labelled reference rather than a guess.

## The panel

From `Reports/LatestReport.txt`, which is the only place the production figures appear:

```
SheathingStarted,   20260922 14:48:54, 39
SheathingCompleted, 20260922 14:51:55, 37, 0.079, 234
```

| | |
|---|---|
| Panel | 39 of job 250712U14-1 |
| Size | 3,600 x 2,720 mm (from the HMI) |
| Time | **3 min 1 s**, start to complete |
| Sheets / members | 37 |
| Volume | 0.079 m3 |
| Nails | **234** |

In the log that panel is 838 lines over 181 seconds.

## What a good panel looks like in the log

- **Three bridge guns and two bridge saws**, each with its own trolley and Z axis. In this panel:
  `BridgeSaw1Z` and `BridgeSaw2Z` each moved 14 times; each bridge gun trolley took 6 preload
  moves.
- **9 `FireSeqCompeted` per bridge gun**, all three guns, in step.
- `BridgeGunsMovePLC` cycles 100 -> 200 -> 1000, twelve times.
- The "Waiting for ..." lines are **normal**: `Waiting for Panel Height and Both Side Trolleys in
  Position` and `Waiting for Both Trolley Stud Pins Up` appear throughout a panel that ran well.
  They are the sequencer handing off, not a machine stuck.

## Nails are not in the I/O - do not try to count them

This is the thing to know before reading any Wall Sheather log.

The machine does not fire one nail per logged output. It **loads a pattern** into each gun:

```
LoadGunFire, Bridge1 Gun, Firstpos 138.75 Spacing 143.6765
LoadGunFire, Fixed Gun,   Firstpos 1955   Spacing 74.16666
```

and then logs one `FireSeqCompeted` when that whole row is done. A row of nails is one line.

Across the good panel, `IO-GunFire` went on **zero** times while 234 nails went in. Across the
whole 8.9 hour log it went on 134 times. Counting gun outputs would have said this machine barely
fired all day.

So: nail counts come from `LatestReport.txt`. The log tells you the **pattern** - first position
and spacing - and whether each sequence completed.

## The I/O: 24 named points, 67 numbers

Most things are fitted three or five times rather than twice, because of the three gun gantries
and two saws. `IO-GunFire` exists five times; `GunUp` five times; `IO-GunNotRotate`,
`IO-Lift10mm`, `IO-RamLock` and `IO-RotateLock` three times each.

That is the clearest case yet for the list being names rather than numbers - an address-led map
would have been 67 unrelated rows.

## A note on versions, and where not to read them

The machine is on **V2.3.0.0**, which is what the bundle says.

A screenshot of this job showed **V2.6.0.0** in the title bar, and that is not the machine - it is
the copy of SDN on the support laptop the files were opened with. SDN puts its own version in the
title bar whatever it is looking at.

So: **the version comes from the bundle, never from a screenshot of the HMI.** A screenshot taken
on a support machine reports the support machine. This is worth remembering because the version is
what a support answer gets pinned to, and getting it wrong sends somebody chasing a fix that is
not in the build the customer is running.

## This is one panel

A labelled good one, which is worth a great deal - but a baseline properly wants many.
`LatestReport.txt` only carries the most recent, so the weekly `ProdLogV2` / `ShiftLog` files off
this machine would turn this into a real distribution, the way M20771's 271 shifts did for the
Raked Extruder.
