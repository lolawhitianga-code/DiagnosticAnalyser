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

## 2. Safe vs normal - now measurable, and all 302 are normal

Per the description:

- **Normal** - grippers to 6040, release, then ejectors drive positive **while** grippers drive
  negative at the same time.
- **Safe** - grippers to 6040, release, grippers drive to 400 **and finish**, then the ejectors
  drive positive.

So the discriminator is the gap between the puller retract command and the ejector push command.

| Machine | Ejections | Ejector push after puller retract | Overlapped |
|---|---|---|---|
| M21737 | 138 | median **+0.42 s** (+0.42 to +0.45) | 138/138 |
| M21844 | 138 | median **+0.23 s** (+0.12 to +26.40) | 136/138 |
| M20771 | 26 | median **+0.48 s** (+0.24 to +0.49) | 26/26 |

**300 of 302 ejections are normal.** The push follows the retract by under half a second - they
are running together, not one after the other.

**A safe ejection has not been observed once in any of these logs.** Either it is rare, or it is
not enabled on these three machines, or it is triggered by a panel type none of these days
produced. That is question 1.

The pullers go to **6000**, not 6040, on all three machines. Worth confirming whether 6040 is the
spec and 6000 what is actually commanded, or whether the number varies by machine.

---

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

- **2026-09-22** - Opened, then corrected. First pass documented the 300-series reload as the
  ejection; the ejection is the 2000-series. Staged release found to be by side, not upper/lower,
  with the floating side first in 302 of 302. All 302 timed ejections are normal, not safe.
