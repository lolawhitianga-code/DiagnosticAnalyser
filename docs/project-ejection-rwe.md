# Project Ejection RWE

Standardising the Raked Wall Extruder V3 ejection: what the variants are, what each step does,
and what the settings actually control.

**Status: open.** Read from logs. Everything below is measured unless marked open.

## Correction to the first version of this document

The first pass had the wrong sequence. It documented the **300-series** as the ejection. It is
not - it is the **reload**: grippers coming *on* at steps 360 and 390 are them closing on the
next panel's plates, which is why that sequence ends with `Waiting For Grippers To Close` and the
gripper product sensors making.

**The ejection is the 2000-series.** Everything below is that.

## Source

Three machines: M21737, M21844, and eleven M20771 exports spanning Feb 2025 to Sept 2026.
**158 ejection runs**, of which **302 release-and-push pairs** could be timed in full.

---

## 1. The ejection is already nearly standard

Eight distinct step paths across 158 runs, and the spread is small:

```
2000 2010 2011 2018 2019 2020 2030 2031 2040 2041 2045 2046 2047
     2060 [2062 2063] 2068 2070 2080 2100 2110 2120 [2130 2140]
```

| Variant | Runs |
|---|---|
| Full spine, ends 2140 | 71 |
| Full spine, ends 2120 | 65 |
| With `2062 → 2063` | 15 |
| Truncated (log ends / aborted) | 7 |

Only **two** real branches: the `2062 → 2063` pair, and whether it carries on past 2120. That is
a far tighter sequence than the reload, and a good starting point to standardise from.

---

## 2. Safe vs normal - the test, and why it is unambiguous

Per the description:

- **Normal** - grippers to 6040, release, then the ejectors drive positive **while** the grippers
  drive negative at the same time.
- **Safe** - grippers to 6040, release, grippers drive back **and arrive**, then the ejectors go.

Both use the same targets. The pullers go 6000 then 1200 in every ejection here; the 400 move
belongs to the reload and never appears inside an ejection (0 of 308). So the difference is
**purely timing**, and the test is: does the ejector command wait for the puller to arrive?

Arrival is reported on the **Node** lines, not the named axes. `FixedSidePuller` and
`FloatingSidePuller` only ever report Axis Disabled or Axis Reset - the OK comes from
`Node0 Status` / `Node1 Status`.

| Machine | Pullers 6000→1200 take | Ejectors commanded at | Gap |
|---|---|---|---|
| M21737 | **10.05 s** | 0.42 s | ejectors go 9.6 s early |
| M21844 | **8.69 s** | 0.17 s | 8.5 s early |
| M20771 | **16.32 s** | 0.46 s | 15.9 s early |

So on this data a normal ejection puts the ejector command at **0.2-0.5 s** and a safe one would
put it at **8.7-16.3 s**. There is nothing in between. **Anything over 3 seconds is a safe
ejection**; under 1 second is normal.

**All 302 timed ejections are normal.** Not one safe ejection in any of these logs.

Two things worth noting from the same measurement:

- **M20771's pullers take 16.3 s where M21844's take 8.7 s** - nearly double, for what should be
  the same 6000→1200 travel. Speed setting, or a different machine geometry? Open.
- The pullers go to **6000**, not 6040, on all three machines.

**Confirmed from the machine's own configuration** (`RakingWallExtruderV3DG.xml`, which every
bundle carries - it is UTF-16, which is why it had read as empty until now):

| Node | Axis |
|---|---|
| 0 | FixedSide Trolley - the fixed side puller |
| 1 | FloatingSide Trolley - the floating side puller |
| 2 | FixedSide EjectServo |
| 3 | FloatingSide EjectServo |
| 4 | TrolleyHeight |
| 5 | FloatingSide YAxis |

All three machines agree. So Node0/Node1 OK really is the pullers arriving, and Node2/Node3 the
ejectors - the earlier inference was right, and is now read rather than guessed.

Node numbers are **reused across controllers**: node 4 is TrolleyHeight on the Omron main and the
fixed side servo guns on a CLX at TCP192.168.50.2. The log's NodeN Status lines come from the
main.

The config does **not** explain why M20771's pullers take 16.3 s where M21844's take 8.7 s -
velocity is 1000 and scale about 420.4 on all three. Still open.

## 3. Staged release is staged by SIDE, not by upper/lower

This is the other thing the first pass had wrong.

Both grippers on a side drop together - lower and upper, same millisecond. What is staged is the
**two sides**:

```
+0.00s  pullers → 6000
+6.50s  step 2031   floating side: lower AND upper off
+14.34s step 2046   fixed side:    lower AND upper off
+19.81s pullers → 1200        }  together
+19.96s ejectors → 7225       }
```

| Machine | Gap between the two sides | Floating side first |
|---|---|---|
| M21737 | median **3.11 s** (3.08 - 8.87) | 138/138 |
| M21844 | median **7.64 s** (4.82 - 9.33) | 138/138 |
| M20771 | median **3.09 s** (2.80 - 8.52) | 26/26 |

**The floating side always releases first.** 302 out of 302, all three machines. Not once the
other way.

### The settings do not cleanly explain the gap

| Machine | StagedGripperRelease | ReleaseGrippersTogether | Side gap |
|---|---|---|---|
| M21737 | not in its change log | not in its change log | 3.11 s |
| M21844 | **True** (14 Jul 2026) | not in its change log | **7.64 s** |
| M20771 | **True** (26 Feb 2025) | **False** (7 Nov 2024) | 3.09 s |

M21844 and M20771 both have `StagedGripperRelease = True` and sit **4.5 seconds apart** on the
one number that setting is named for. So either something else sets the gap, or the setting does
something other than its name suggests.

That is worth 4.5 s × every panel on M21844.

---

## 4. Open questions, in order

1. **What triggers a safe ejection?** Never seen in 302 runs. Panel complexity, a setting, or an
   operator choice?
2. **Why is M21844's side gap 7.64 s when M20771's is 3.09 s**, with the same
   `StagedGripperRelease` setting? If the 3.1 s machines are fine, M21844 is giving away 4.5 s a
   panel.
3. **What do `StagedGripperRelease` and `ReleaseGrippersTogether` actually change?** Neither
   predicts what the logs show.
4. **What are steps 2062 and 2063?** The only real branch in the sequence, 15 runs of 158.
5. **Is 6040 or 6000 the intended release position?** All three machines command 6000.

## 5. What would move this fastest

- **The PLC step names for the 2000-series.** Twenty-two numbered steps; the program has their
  names. That answers 4 outright and probably 1 and 3.
- **A log containing a safe ejection.** If someone can make one happen on a test panel and export
  it, the discriminator above will identify it immediately and the whole branch becomes
  measurable.
- **`ReleaseGrippersTogether = True` on any machine.** All three here have it False or unset.

## Changelog

- **2026-09-22** - Safe/normal test sharpened: both use the same targets, so the discriminator is
  whether the ejector command waits for the pullers to arrive. Pullers take 8.7-16.3 s; ejectors
  are commanded at 0.2-0.5 s. A 3 second threshold separates the two cleanly.
- **2026-09-22** - Opened, then corrected. First pass documented the 300-series reload as the
  ejection; the ejection is the 2000-series. Staged release found to be by side, not upper/lower,
  with the floating side first in 302 of 302. All 302 timed ejections are normal, not safe.
