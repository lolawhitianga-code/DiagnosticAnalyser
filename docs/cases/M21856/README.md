# M21856 - fixed side gripper out by 30 mm

**Mainland, Raked Wall Extruder V3, Omron build. 22 September 2026.** Solved.

## What the operator said

> fixe side out by 30mm

## What was wrong

The fixed side trolley's **home sensor was sitting too far from its aluminium block.**

Nothing had changed in `Change.log`, which is the tell. The trolleys home to a sensor
(`HomeMode = Sensor` in `RakingWallExtruderV3DG.xml`), and that sensor sets where the machine
thinks zero is. So when the sensor is wrong, every position the machine drives to afterwards is out
by the same amount - and no setting shows it, because no setting was touched.

## How home is found

1. The trolley drives onto its aluminium block until the sensor sees it.
2. It then drives **slowly off** the block.
3. **The exact moment the sensor turns off is home.**

That falling edge is the accurate spot. For it to be crisp, the sensor has to sit **1-2 mm from the
block**. Too far and the switching point drifts, so home lands in the wrong place.

| Before - sensor too far from the block | After - 1-2 mm |
|---|---|
| ![before](1-before-sensor-too-far.jpg) | ![after](2-after-sensor-1-2mm.jpg) |

## The fix

With the gripper clear of timber and safe to move, send the fixed gripper home and look at the
sensor. Reposition it 1-2 mm from the aluminium block. Re-home.

## Was it in the log? No

Nothing in MachineLog.txt points at this. An earlier version of this write-up claimed the fixed
side finishing homing 2.17 s after the floating side was the signature. **It is not.** The two
trolleys are independent, each with its own home sensor and its own home position -
`RakingWallExtruderV3DG.xml` has them at 5840 and 5830 on this machine - so they reach home at
different times as a matter of course.

## How it was actually found

1. The operator reported one side out by a fixed amount - 30 mm.
2. `Change.log` showed nothing changed. A position error with no setting change points at the
   hardware that sets the position.
3. `HomeMode = Sensor` in the config: the home sensor defines zero for that trolley.
4. Sent the gripper home and looked at the sensor. It was too far from the block.

That chain is the diagnostic - a consistent offset on one side, a clean change log, and a
sensor-homed axis. The log is not part of it.
