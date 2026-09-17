# Reading I/O out of MachineLog.txt

Reading a machine log line by line tells you what **changed**. It does not tell you what was
already **held on**, which is usually the thing that explains the fault - a clamp still energised,
a sensor still made, a gun that never released.

`IoTimeline` replays every `InputChange` and `OutputChange` from the top of the file so the state
of every point can be asked for at any moment in it. The app puts that behind **I/O at a moment**,
on the toolbar and the right-click menu.

## A point is identified by kind, name and address - all three

This is the finding that matters, and it was not obvious. Neither half is unique on a real machine:

| Case | Seen on | What happens if you key on one half |
| --- | --- | --- |
| One name, two addresses | M21737: `LowerGunFire` is COM7-6.5 **and** COM7-6.2, one coil per side | Keying on the name loses a coil |
| One address, two output names | M20716: `192.168.250.1-1.12` carries `IO-OutfeedDriveTopClamp2Down` and `...3Down` | Keying on the address merges two outputs that really do move apart |
| One address, an input and an output | M20716: `192.168.250.1-0.2` is input `FollowerUp` and output `IO-DeckRev` | Keying on the address reports one as the other |

On the 100,000 line M20716 log that is **66 points across 45 addresses**. Both halves are shown in
the window, and where an address carries more than one point the window says so underneath rather
than hiding it - somebody tracing a wire to `1.12` needs to know two named outputs sit on it.

## These lines are changes, so the state before one can be read backwards

Two wordings appear, and only two, across every sample log:

```
07:53:34.8774739,  OutputChange, TopStudClamp,   Output (COM7-6.7) Set On
07:31:50.3341329,  InputChange,  THNTD,          Input (COM7-2.12) Changed to 1
```

Across 20,747 change lines on the sample logs, **no point ever logs the same value twice running**.
So a change really is a change, and that makes an inference sound: a point whose first event in the
file is `Set Off` was **on** before it, and one whose first event is `Set On` was off.

That matters because a support bundle's log starts mid-cycle. On the M21737 sample, six of the
twelve outputs appear first as `Set Off` - they were energised when the log opened. Assuming
everything starts off would be wrong for exactly those six.

The window marks those rows grey and italic and says "read backwards from its next change", so an
assumed state never reads as a measured one. Anything asked about the first few seconds of a log
is mostly assumption, and it says so.

## The moment, and what "at" means

The state shown is as at the chosen line **including it**, so clicking a line that reads
`TopStudClamp Set On` shows that output on. That is what anybody reading the log expects.

Replay is by **line order, not by clock**. The clock is not reliable: the M20716 log jumps back
1.3 seconds once, and a long log runs past midnight. The file is written in the order things
happened, so line order is the one thing that can be trusted. A backwards jump of more than twelve
hours is treated as a new day; anything smaller is jitter and left alone.

A typed time lands on the **last** line at or before it, so on a log that crosses midnight it finds
the later occurrence. The window says so when it applies, and picking the line is always exact.

Times are parsed strictly, by `MachineLogTime`. `TimeSpan.TryParse` reads a bare number as a count
of days - "8" for eight o'clock becomes eight days, and a line number pasted into the time box
becomes 981 days. Both parse happily and land past the end of any log, with nothing on screen to
say the time was not understood. Refusing them is the better answer.

## The list of known points is learned, because nothing carries it

`Machine.xml` has no I/O map. The only way to know a machine has an output called `TopStudClamp` at
COM7-6.7 is to have seen it move.

So `SignalCatalogueService` keeps what every bundle teaches, per serial, in `MachineSignals`. Every
machine log that comes through the watch folder folds into it automatically - counts update, the
list does not double. Like the production import, anything going wrong is logged and swallowed: it
must never cost the user the diagnostic import they asked for.

What that buys is the half a single log cannot tell you. A reading can say *this machine is known
to have 54 other points that never move anywhere in this log* - and a clamp that moves in every
other bundle and sits still in this one is usually the thing worth looking at.

The catch is honest and worth stating: a point only enters the catalogue once it has moved at
least once somewhere. A sensor that has never been seen to change on any bundle is not in the list
at all, and that absence is itself worth noticing.

## Still open

- **No wiring map.** Everything here is learned from behaviour. If a manifest of I/O points ships
  with the machine, or exists in the PLC project, loading it would turn a learned list into a known
  one and would immediately say which points have never once been seen to move.
- **Two names on one address** are tracked separately because the log moves them separately. Whether
  that is two coils on a shared reported address, or one coil the software drives under two names,
  has not been confirmed with anybody who knows the machine.
