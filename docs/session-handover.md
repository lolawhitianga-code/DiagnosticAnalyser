# Session handover - 23 September 2026

What this session was working on, what it found that is not written anywhere else, what is still
open, and what to do next. The longer background is in [HANDOVER.md](HANDOVER.md); case write-ups
are in [cases/](cases/).

State at hand-over: `main`, all checks green (`bash tools/check.sh`: Core build, view-model
type-check, XAML binding check, 797 Core tests). Nothing uncommitted.

---

## 1. Where it stopped: M21868 nog clamp (Mainland, Component Nailer V2)

Support bundle 17 September 2026, panel 9, soffit nog. Operator: "clamp wont engage and clamp nog".

**Cause, found on the machine by Spida support:** the nog clamp was coming down very slowly from
its upper position (about 200 mm) and was locked in place before it arrived at its 45 mm position.

**What the log shows, measured:**

| | |
|---|---|
| Failed checks | `Incorrect Nog Height From Top Of Stud, Expected : 45.0 Got:` **41 times**, 07:10-10:43 |
| Readings | 12 to 30 mm, twice below zero (-5.4, -2.3). **Every one short of 45** |
| Good cycles | clamp lock (`IO-VertFrontClampLock` / `IO-VertBackClampLock`) comes on about **1.24 s** after the clamps are sent down (`IO-VertFrontClamp` / `IO-VertBackClamp`) |
| Failed cycles | the height check comes at **2.62 s** after the clamps go down, the same every time (2.59-2.63 s), with **no lock logged before it** |
| Change.log | `ClampDelay` and `LockDelay` changed 13 times on 10-11 June 2026. LockDelay went 200, 280, 500, 200, 1000, 100, 500, 1000, 200. Now ClampDelay 300, LockDelay 200 |

A fixed 2.62 s on every failure reads as a set time running out while the clamp is still on its
way down. Also seen twice at 07:51: `Incorrect Stud Height at back clamp, Expected : 90.0 Got: 181.4`.

**Built from it** (commit `b15cafd`): `Knowledge/MeasurementCheck.cs` and a report section
**THE MACHINE'S OWN CHECKS FAILED**. It reads every `Incorrect ... Expected : X Got: Y` message on
any machine, gives the count, the spread and whether all readings fell short, times the check from
the clamps going down against the good-cycle lock time, lists clamp/lock delay changes from
Change.log, and for nog height quotes the M21868 cause. The earlier report missed all 41.

**Open:** what fixed it on the machine - flow control / exhaust restrictor, air supply, the
cylinder or its guides, or a setting? Nobody has said yet. When it is known, add it to the
`SeenBefore` text for "Nog Height" in `MeasurementCheck.cs` and to
[cases/M21868/README.md](cases/M21868/README.md), so the next tech knows where to look first.

**Not a lead, and why:** `ClampAirOK` (1.7) changes to 1 on the last line, 10:55:22. Five other
inputs "change" in the same instant, four never seen before that session, and in the July bundles
none of them logged at all. The log does not record input states at start-up; that burst is the
software re-reading its inputs, not the air coming back.

---

## 2. Other M21868 findings (full SDN folder, 23 September)

Written up in [cases/M21868/README.md](cases/M21868/README.md). In short:

- **Production:** a Component Nailer's output is components (a stud with its blocks or noggings),
  closed by `MembersSubAssembled`, not panels. Now counted that way (commit `9fa2628`); see
  [production-reports.md](production-reports.md#component-nailers-count-components-not-panels).
  5,094 components over 80 production days, about 64 a day, median cycle 15 s, 0.37% went wrong.
- **July bundles, "140 mm fires two nails and won't move to the next nog":** step 230 waits on
  `Waiting for Gun(s) to Correct Heights` after turning on `IO-UpperGunLowerGoHigh` (output 0.3).
  The confirming input `UpperGunLowerIsHigh` (0.6, InUse, not simulated) never changes once in
  any of the four bundles. 66 times unanswered in the third July bundle. **Not yet confirmed on
  the machine** - see open questions.
- **Stale files:** the SDN folder's root `SupportInfo.txt` says TornadoM450 / Carters Auckland,
  with 2017 production logs and other models' configs. That is the install image it was copied
  from, not this machine.
- **The user still has to import it:** Production -> pick the `M21868 SDN mainland compn nailer`
  folder -> Import. Tick "Re-read weeks already stored, replacing them" if M21868 was imported
  with an older build, or its rows are the wrong shape (panels, not components).

---

## 3. Also found this session and not written anywhere else: M17311 Tornado follower delay

Asked: why the infeed follower pauses so long before backing off for the last saw cut.
From the M17311 Tornado M500 CLX bundle:

| Phase | Time | What happens |
|---|---|---|
| Follower moves in, `Move to : 10` to axis `OK` | 2.20 s | real motion |
| `MoveControlStep` 120 -> 125 | **3.15 s** | nothing logged at all |
| `MoveControlStep` 125 -> 130 | **3.16 s** | nothing logged at all |

- 8.61-8.79 s in total across 29 changeovers. A 0.18 s spread is a timer, not a mechanism.
- 46 of the log's 61 silent gaps between 2.5 and 4.0 s are these two.
- The outfeed top clamps are **output only** - no confirming input - so the PLC counts instead of
  waiting. `IO-OutfeedDriveTopClamp1Down` is held a median 0.86 s elsewhere in the same log
  (178 times, shortest 0.73 s).
- Nothing in `TornadoM500.xml`, `UserSettings.xml` or `Machine.xml` sets either delay. **Both
  timers are in the PLC program.**
- Cost: about 68 s a board, so 12.8% of the cycle is these timers and about 9.3% is recoverable.
  Roughly 56 minutes in a ten-hour shift.

Suggested, in order: (1) shorten the 125 -> 130 clamp dwell to 1.0-1.2 s, about 2 s a board;
(2) ask whoever wrote the PLC what the 120 -> 125 wait is for before touching it - all three
axes are already `OK` 0.9 s before it starts; (3) the real fix is a down-reed on the outfeed top
clamps, so the dwell becomes a wait-for-confirm. The machine also measures `FollowerDistance`
(node 3 address 8) and `InfeedLaserDistance` (3.5) on port 192.168.50.5, but neither reaches
MachineLog.txt. Logging them would turn the follower work from inference into measurement.

---

## 4. Built this session (all on `main`)

| Commit | What |
|---|---|
| `507f3ee` | Complaint-driven home sensor check for **any servo on any machine**. "Out by N mm" in the operator's issue -> which servo from the machine's own config, its HomeMode, position-setting changes in the last 60 days, the M21856 steps, and the wrong/right sensor photos beside the report. Servos that define their position (Tornado belts) get scale/coupling advice instead |
| `ca3d576`, `00dcdf5` | When a bundle carries two copies of a log, the freshest wins: Change.log and ErrLog by newest entry, MachineLog by file time from the zip (its lines have no date). Change.log moved between SDN versions |
| `712aeee` | "Axis Enable" is logged under Other; the report had called a re-enabled axis disabled |
| `9fa2628` | Component Nailer production counted as components; the fastener-counter-live flag is worked out again on load (it was never stored, so every report from the database said the counter was off every day) |
| `b15cafd` | THE MACHINE'S OWN CHECKS FAILED section (above) |

Not seen running: the analysis window's reference-photo panel (WPF) only passes the Linux
compile and XAML-binding checks. Check it on Windows with an "out by 30mm" issue.

---

## 5. Open questions

1. **M21868 nog clamp - what was the fix on the machine?** (Section 1.)
2. **M21868 July, 140 mm:** is the upper gun's lower cylinder reaching the top, and is its high
   reed (input 0.6) there and set? Does the valve on output 0.3 shift? Was this fixed?
3. **MembersSubAssembled field 2** is taken as fasteners fired (3, 5, 7, 11, 13 against the block
   count). Unverified - count the nails on one component.
4. **M17311:** has anyone asked about PLC steps 120 -> 125 and 125 -> 130, or tried a shorter dwell?
5. **Project Ejection RWE** - all open questions are in
   [project-ejection-rwe.md](project-ejection-rwe.md#4-open-questions-in-order): safe-ejection
   examples (the user was going to find some), 2000-series PLC step names, a log with
   `ReleaseGrippersTogether = True`, M21844's 7.64 s side gap, M20771's pullers at 16.3 s against
   M21844's 8.7 s.
6. **PLC tag exports for every model** - asked for, not yet received. How to export them is in
   [plc-tag-export.md](plc-tag-export.md).

## 6. Next steps

1. Record the nog clamp fix once known (section 1), and add a complaint topic so "clamp won't
   engage" points straight at the measurement section.
2. Get a confirmed answer on the July upper-gun sensor, then add it to the Component Nailer
   knowledge (there is no `ComponentNailerKnowledge` yet - only Raked Wall Extruder and Tornado).
3. Import M21868 on the user's PC and check the production page matches: about 5,094 components,
   0.37%.
4. Run the photo panel on Windows.
5. Keep feeding the remaining models through [teaching-a-new-machine.md](teaching-a-new-machine.md).

## Re-running a bundle outside the app

The session used a small console program to push a real bundle folder through the app's own
pipeline. It lived in a scratch folder and is gone; to recreate it, a net8.0 console project
referencing `src/DiagFileMonitor.Core` with this `Program.cs`:

```csharp
using DiagFileMonitor.Core.Data;
using DiagFileMonitor.Core.Services;
using Microsoft.EntityFrameworkCore;
using System.IO.Compression;

var bundleDir = args[0];                       // an unzipped .szip
var zipName = args.Length > 1 ? args[1] : "bundle.szip";
var issue = args.Length > 2 ? args[2] : null;  // optional: override the operator's issue

var root = Path.Combine(Path.GetTempPath(), "rerun", Guid.NewGuid().ToString("N"));
var incoming = Path.Combine(root, "In"); var extract = Path.Combine(root, "Ex");
Directory.CreateDirectory(incoming); Directory.CreateDirectory(extract);
var db = Path.Combine(root, "t.db");
DiagDbContext Ctx() { var b = new DbContextOptionsBuilder<DiagDbContext>(); b.UseSqlite($"Data Source={db}"); return new DiagDbContext(b.Options); }
using (var c = Ctx()) DatabaseInitializer.Initialize(c);

var repo = new DiagFileRepository(Ctx);
var proc = new DiagFileProcessor(extract, repo, true, new ProductionImportService(Ctx), new SignalCatalogueService(Ctx));
var zipPath = Path.Combine(incoming, zipName);
ZipFile.CreateFromDirectory(bundleDir, zipPath);

var bundle = await proc.ProcessAsync(zipPath);
if (bundle is null) { Console.WriteLine("not processed"); return; }
if (issue is not null)
{
    await using var c = Ctx();
    var row = await c.DiagnosticFiles.FindAsync(bundle.Id);
    if (row is not null) { row.SupportIssue = issue; await c.SaveChangesAsync(); }
}
Console.WriteLine(await new DiagnosticAnalysisService(repo).AnalyseAsync(new[] { bundle.Id }));
```

For production, `new ProductionImportService(Ctx).ImportFolderAsync(folder, ProductionSerial.FromPath(folder)!)`
then `LoadPanelsAsync` and `ProductionAnalyser.Summarise` does the same as the Production page.
