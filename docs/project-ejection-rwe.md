# Project Ejection RWE

Standardising the Raked Wall Extruder V3 ejection sequence: what the variants actually are, what
each step does, and what the settings really control.

**Status: open.** This is the starting position, read from logs. Everything below is marked as
either measured or open.

## Source

Three machines, every ejection run in them:

| Machine | Ejection runs |
|---|---|
| M21737 | 107 |
| M21844 | 112 |
| M20771 | 50 (across eleven exports, Feb 2025 - Sept 2026) |

**269 ejection runs.**

---

## 1. There is one sequence, not several

This is the first useful finding, and it cuts against how it is usually described.

All 269 runs are the same spine with **four optional branches**. There are not several different
ejection methods; there is one, with decision points.

**The spine** (every run that completed follows this):

```
300 → 302 → 304 → 310 → [311] → 315 → 320 → [321…] → 330
    → 350 → 351 → 352 → 360 → [355 → 357 → 352 → 360]
    → 363 → 370 → 375 → [378…] → 380 → 390 → 391 → 392
```

**The four branches:**

| Branch | Runs | What it is |
|---|---|---|
| `311` between 310 and 315 | 30 | Plate present bypass turned **off** |
| `321` looping with 320 | 16 | Floating head laser sees an obstruction - the guard, covered in `docs/guard-stops.md` |
| `355 → 357 → 352 → 360` | 21 | Side pullers re-enabled and the 352-360 stretch runs **again** |
| `378` after 375 | 3 | `THNTD released early` - upper grippers **off**, back round |

The most common path (137 of 269) runs the full spine with no branch at all.

---

## 2. What each step does

Measured from what happens inside each step across all 269 runs.

| Step | Ran | Median | What happens in it |
|---|---|---|---|
| 300 | 269 | 0.18 s | Reset lamps **on**, plate supports **off** |
| 302 | 269 | 1.08 s | Plate clamps up |
| 304 | 267 | 0.24 s | Both side pullers commanded to move |
| 310 | 266 | 0.24 s | Both eject servos commanded to move |
| **311** | 34 | 0.44 s | Plate present bypass **off** |
| 315 | 265 | 0.19 s | TrolleyHeight and FloatingSideHeight axes **disabled** |
| 320 | 348 | - | Floating head asked to move in; THNTD presses land here |
| **321** | 39 | - | Laser obstruction. Polls back to 320 until clear |
| 330 | 254 | - | Nodes 4 and 5 servo-disabled |
| 350 | 220 | 0.29 s | FloatingSideHeight and TrolleyHeight commanded |
| 351 | 220 | 2.75 s | FloatingSideHeight disabled, **rack lock on** |
| 352 | 227 | - | Nodes settling |
| **355** | 26 | - | Both side pullers **axis re-enabled** |
| **357** | 26 | - | Nodes 0 and 1 back to OK |
| 360 | 224 | 2.95 s | **Lower grippers on**, both sides. Pullers move. Plate present bypass back **on** |
| 363 | 198 | - | Trolley bottom clamps cycle closed |
| 370 | 198 | - | THNTD released |
| 375 | 198 | 0.18 s | Clamped/fire release lamp **on** - the operator prompt |
| **378** | 3 | 8.12 s | `THNTD released early` - **upper grippers off** |
| 380 | 201 | **10.22 s** | Release lamp **off**. The longest step in the sequence |
| 390 | 195 | 0.18 s | **Upper grippers on**, both sides |
| 391 | 198 | - | `Waiting For Grippers To Close`; gripper product sensors make |
| 392 | 172 | - | `Clamps Within safe distance - Auto Clamping`; plate heights read |

---

## 3. The grippers - what the logs show, and what they do not

The two settings are `StagedGripperRelease` and `ReleaseGrippersTogether`. **The observed
behaviour does not line up with either of them.**

What actually happens in all 269 runs: **lower grippers come on at step 360, upper grippers at
step 390.** Between them sit steps 363, 370, 375 and 380 - including the operator prompt and a
**10.2 second** dwell at 380. Lower and upper are never simultaneous in any log here.

There is exactly **one** variant, and it is small:

| Where upper grippers come on | Count |
|---|---|
| Step 390 | 390 of 396 (98.5%) |
| Step 380 | 6 (1.5%) |

All six of the step-380 cases are in **one M20771 export** (the 21 Sept 2026 one), not spread
across that machine's history.

And the settings do not predict it:

| Machine | StagedGripperRelease | ReleaseGrippersTogether | Uppers at |
|---|---|---|---|
| M21737 | not in its change log | not in its change log | 390, 100% |
| M21844 | **True** (14 Jul 2026) | not in its change log | 390, 100% |
| M20771 | **True** (26 Feb 2025) | **False** (7 Nov 2024) | 390 90%, 380 10% |

M21844 has `StagedGripperRelease = True` and never once uses the 380 variant. So whatever that
setting changes, **it is not which step the upper grippers come on at** - or it is, and something
else overrides it.

**This is the first thing to settle with the engineering team.** Two settings named for gripper
release, and the logs show one variant that neither of them predicts.

---

## 4. Open questions, in the order worth answering

1. **What do `StagedGripperRelease` and `ReleaseGrippersTogether` actually change?** The names
   suggest they control what the logs show happening at 360 and 390, and the evidence says they
   do not. Ask the PLC author before anything else.
2. **What is the 10.2 second step 380?** It is the single longest step in the ejection. Is it a
   timer, an operator wait, or a motion? The release lamp goes off at its start and the upper
   grippers come on at its end. 269 runs × 10.2 s is real money.
3. **What decides step 311?** Plate present bypass off, on 30 of 269 runs. Panel type, or a
   setting?
4. **What decides the 355 → 357 loop?** The 352-360 stretch runs a second time on 21 runs. A
   retry, or a per-section repeat on a more complex panel?
5. **Is `THNTD released early` (step 378) recoverable or a restart?** Three runs. Median 8.12 s
   and the upper grippers drop.

## 5. What I need to go further

- **The PLC step numbers with their names.** The logs give numbers; the program gives meaning.
  This would answer questions 2-5 outright.
- **A log from a machine with `ReleaseGrippersTogether = True`.** All three here have it False or
  unset, so the setting's effect has never been observed.
- **Panel complexity alongside a few runs.** Simple vs raked vs with-openings, for a handful of
  ejections, would tie the branches to panel type - which is the thing you actually want to
  standardise on.

## Changelog

- **2026-09-22** - Project opened. 269 ejection runs read from three machines. One spine and four
  branches established; the gripper settings found not to predict the observed variant.
