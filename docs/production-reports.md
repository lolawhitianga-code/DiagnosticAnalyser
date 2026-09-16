# Production reports

Reads ProdLogV2 weekly production logs into this PC's database and builds a production report from
what is stored. **Production** on the main toolbar opens it.

Importing and reporting are separate: logs are read once and kept, so a report covering a year
does not re-read a year of files.

## What was checked, and against what

Ten real weeks from **M21737** (PlaceMakers Auckland, 4.8M Raked Extruder), weeks 28-37 of 2026 -
213,665 lines. Every rule below was measured on that data, not taken from the reference guide on
trust. Where the guide and the data disagree, the data won and the disagreement is recorded.

Result on that sample: 4,665 panels completed, 1,002 stepped past, 72 stopped by the operator,
3,350 superseded. **Fault rate 1.25%**, which lands inside the 0.5-3% band the delivered reports
show for other machines.

## Where this differs from the supplied guide

### Panel names are reused labels, not unique identifiers

This is the big one, and the guide does not mention it.

On the M21737 sample there are **426 distinct panel names across 10,364 `PanelStarted` events**.
`E5` was started 139 times and assembled 53 times. `E1`: 137 and 56.

The guide's rule 3 says a panel that never gets a matching `PanelAssembled` is abandoned, and
therefore a fault. Applied literally that gives a **37.7% fault rate** - and the guide itself says
a double-digit rate means the rules are being applied wrong.

So a panel left open when a *different* name starts is classified `Superseded` and is **not** a
fault. It is the operator moving around the HMI. The only unambiguous abandonment signal is
`PanelStopped`, which the controller writes on purpose. The report prints the superseded count with
an explanation, because a large number with no explanation looks alarming.

### "Every event type is written twice" is overstated

Measured across all ten weeks:

| Event | Consecutive duplicates |
|---|---|
| `MemberAssembled` | 41,305 of 83,244 - **49.6%** |
| `MachineStopped` | 4,809 of 16,090 - **29.9%** |
| `PanelAssembled`, `PanelStarted`, `PanelStopped`, `MachineStarted`, `MachineIdleStart`, `MachineIdleStop`, `MemberCut` | **0** |

De-duplication is still applied and is still right - it only ever suppresses a line byte-identical
to the one immediately before it - but the panel rows the report is built from were never at risk.
The doubling matters for member counts, not panel counts.

### The guide contradicts itself on what to keep

Rule 3 needs `PanelStarted` to spot abandoned and stepped-past panels. "What you actually need to
keep" says filter down to `PanelAssembled` and `PanelStopped` before doing anything else. Both
cannot be true. This implementation keeps `PanelStarted` and `MemberAssembled` through parsing and
discards them after classification.

### Rules that held up exactly

- Same-name restarts: **15.3%** measured, against the guide's "roughly 15-20%".
- Build time must come from the log's own field, never recomputed from elapsed time between events.
- Implausible spans are real: **77 panels** logged over 20 minutes, the worst at **304 minutes**.
  Flagged and kept out of time averages rather than counted at face value.
- Availability from a shift model rather than from `MachineStarted`/`MachineStopped`.

## Unknowns - flagged, to verify later

These are live in the code as comments and in the report as a "Not yet verified" callout.

**1. The fastener count column (field 3 of `PanelAssembled`).** The guide says it runs about 4x the
member count. On M21737 only **5.7%** of panels matched that and the mean ratio was **2.86**. It is
non-zero on 2,632 of 5,667 rows and its values are almost all multiples of 4. Curiously, the same
150 panels where it equals 4x members are exactly the ones where it equals the junction count.
Stored raw as `FastenerCount`, reported raw, and **nothing is derived from it**.
*To settle: count the fasteners physically on one panel and compare.*

**2. 26.7% of completed panels log a build time of exactly zero.** 1,245 of 4,665. They are counted
as panels; what a zero means is not known. *To settle: watch one build and see what gets logged.*

**3. 340 of 412 `PanelStopped` events arrive with no panel open.** Only 72 closed one. Those 340 are
currently ignored. *To settle: is a `PanelStopped` with nothing open meaningful, or HMI noise?*

**4. The delivered reports' "genuine faults" figure cannot be reproduced.** The Line 3 report shows
39 faults in 7,291 panels (0.53%); this implementation would call them operator stops. The
authoritative definition lives in the JavaScript inside
`Line3-RakedExtruder-CartersAuckland-production.html`, which has not been supplied.
*To settle: send that file, or a ProdLogV2 set for AOR1694 or M14454 so the numbers can be
reconciled against totals already signed off.*

**5. The shift model is an assumption, not a measurement.** Availability changes completely with it.
Since reading the delivered DGM20771 report the maths is now theirs - rostered time less start-up,
tail and every gap over the threshold, with break and off-shift minutes taken out of the middle of
each rather than off the ends. On M21737 that moved availability from 56.7% to 34.7%, into the
range the delivered reports show. There is now also an **Ignore shift** option, which reports no
availability at all rather than one built on a guessed roster.
The round-the-clock Carters model is transcribed from the delivered reports, including the 03:30
overnight gap those reports flag as not being on any official break sheet. Applying that model to
M21737 gives 56.7% - but nobody has confirmed PlaceMakers runs round the clock, and if it runs a
single shift the real figure is different. *To settle: confirm the shift pattern per site.*

**6. Serial numbers come from the folder, not the file.** ProdLogV2 carries no machine identity -
not in the file, not in the name. **Browse...** reads the serial out of the folder name
(`D:\Production\M21737\Reports` gives M21737, deepest folder first), and ticking *each sub-folder
is a different machine* sweeps a parent folder and files each one under its own name. A folder
whose name has no serial in it is skipped and said out loud rather than filed under a guess. Model
names are deliberately not matched - TornadoM450 and SprintM600 are types, and filing production
under a type would merge every machine of that type into one set of figures. *Still open: whether a
manifest ships alongside these exports that would settle identity properly.*

**7. `MemberCut` is rare and unused.** 13 events in ten weeks. Parsed and kept, never counted.

**8. `UserLogout` was not in the reference guide's event list.** It turns up in a real bundle's
production report. Recognised now, but unused - like `UserLogin`.

## The report is a page you work, not a page you read

The delivered DGM20771 report is interactive, and copying only its look was missing the point. The
app now writes two files from the same figures:

| File | What it is for |
| --- | --- |
| `production-<serial>-<date>.html` | The one that opens. Period switches between month, week, day and hour; measure switches between panels, cube and lineal metres; bars and day chips drill down; the day is drawn as a wall of studs. |
| `production-<serial>-<date>-print.html` | The same figures laid out flat, to print or paste into a ticket. |

Both are self-contained. No CDN, no web fonts, no second file - a report emailed to a site reads
the same on a PC with no internet.

### What the page recomputes, and why that is duplication on purpose

`ProductionPayload` ships the panel rows and the C#-computed per-day totals. The page then works
the availability maths out again in JavaScript, because the shift model is editable in the page -
a roster box that cannot change anything is not worth having, and a site knows its own roster
better than we do. The JavaScript rules are ports of the C# ones:

- rostered minutes are shift length less breaks;
- a gap counts as an unplanned stop when more than the threshold of it was rostered production,
  with break and off-shift minutes taken out of the middle rather than off the ends;
- run time is rostered time less start-up, tail and every stop.

Two implementations of one rule will drift, so the page checks itself: on load it compares its own
availability for the shift the report shipped with against the figure C# put in the payload, and
prints a "Check this" note in the notice box if they differ by more than a percentage point. If
that note ever appears in the field, one of the two is wrong.

### What the page deliberately does not show

The DGM20771 template drives several panels off a **Details CSV** export, which ProdLogV2 does not
carry: load time against a per-stage target, nails fired against nails called for, and plate width.
Those sections are left out rather than filled with an invented target. The one place a comparison
was wanted - colouring the studs - uses this machine's own **median build time** as the reference,
with the same 1.25x and 2x bands the template uses, and the legend says so in those words rather
than claiming a target.

## Two sources, and why both

Production data arrives two ways, and they complement each other exactly:

| | Weekly export | Inside a support bundle |
|---|---|---|
| File | `ProdLogV2<year>W<week>.log` | `Reports/LatestReport.txt` |
| Covers | a full ISO week | the few days before the bundle was taken |
| Says which machine? | **no** - nothing in the file, name or content | **yes** - the bundle's own `Machine.xml` |
| How it gets imported | Browse to a folder | automatically, whenever a `.szip` is processed |

That second row is the point. The weekly exports carry plenty of production data and no identity;
a bundle carries a few days of the same data and a serial number. So a bundle's report is filed
under the serial the machine reported about itself, with nobody typing anything.

Measured on real bundles: an M20716 export held 2 days and 108 panels; an AOR1694 export held
4 days, 17,021 events and 572 assembled panels. One real bundle (AOR1613) carries a **zero byte**
`LatestReport.txt`, which is skipped without complaint.

The two sources overlap on purpose - the weekly export for a week arrives later holding days a
bundle already covered. Both files are recorded, and **panels are de-duplicated**, matched on
machine, moment and panel name. Neither source has to be preferred over the other, and counting a
day twice would inflate every figure built on it.

A bundle's report has no week in its name, so its ISO week comes from its own events. Anything
going wrong while reading it is logged and swallowed: production figures are a bonus on top of a
diagnostic import, never a reason to fail the import the user asked for.

## How it fits together

```
ProdLogV2*.log → ProdLogParser → PanelClassifier → ProductionPanels table
                                                          │
                                              ProductionAnalyser → ProductionSummary
                                                          │
                                              ProductionReport → ReportModel
                                                          │
                                              ReportHtmlRenderer → one .html file
```

Same renderer, blocks and chart code as the diagnostic reports, so a production report is
self-contained and offline the same way.

| File | Job |
|---|---|
| `Production/ProdLogParser.cs` | Reads a weekly log; dedupe, NUL stripping, bad-line survival |
| `Production/PanelClassifier.cs` | Completed / stepped past / stopped / superseded |
| `Production/ShiftModel.cs` | Shift and break model, per site |
| `Production/ProductionAnalyser.cs` | Day, month and window figures; cross-machine downtime |
| `Services/ProductionImportService.cs` | Import, de-duplicate by week, load back |
| `Reports/ProductionReport.cs` | Builds the report model |

## The folder layout these arrive in

```
D:\Raw logs\
  M21737 raked extruder 4.8\
    SDN\Reports\ProdLogV22026W28.log
                 ProdLogV22026W29.log
                 ShiftLog2019W28.log      <- left alone
                 LatestReport.txt         <- the bundle's own copy
  AOR1694 line 3\
    SDN\Reports\...
```

Point **Browse...** at the top folder, tick **each sub-folder is a different machine**, and every
machine is read under the serial in its own folder name. The logs can sit any number of levels
down - the whole tree under each machine folder is searched - so the unpacked bundle structure
needs no flattening.

Files that are not read are counted and named rather than passed over silently:

| | What happens |
|---|---|
| `ProdLogV2<year>W<week>.log` | Read |
| An empty one | Skipped. A zero byte log is a file with nothing in it, not a week the machine stood idle - storing it would put a phantom shutdown in the machine's history |
| `ProdLog<year>W<week>.log` | **Recognised, not read.** Same naming without the V2. No one has supplied one with data in it, and guessing a file format is how this project has gone wrong before |
| `ShiftLog…`, `.txt`, `.zip` | Left alone |

## A week is imported once

A week is identified by serial plus ISO week from the file name, not by path - exports carry a
duplicate copy of recent weeks in a second folder, and the guide is right that processing both
would double the figures. Re-importing a week already held is skipped unless "replace" is ticked.

## Days with no output are kept

Every calendar day between the first and last panel gets a row, including empty ones. A day where a
machine sat at zero while the rest of the site worked is the most useful thing this data surfaces.
An empty day contributes **zero planned minutes**, so a shutdown nobody was rostered for does not
drag availability down as though the machine had been idle on shift.
