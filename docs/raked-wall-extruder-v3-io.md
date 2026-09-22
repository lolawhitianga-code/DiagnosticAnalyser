# Raked Wall Extruder V3 - what it has

**48 named points. 35 of them fitted twice, one per side.**

Read from three machines: M21737 (82,149 lines), M21844 (84,360) and M20771 (eleven exports
across nineteen months). All three agree on every name.

## The name is the identity, the number is not

They do **not** all agree on the numbering, and that is normal. `UpperGunUpperIsLow` sits at
`0.19` on M21737 and M21844 and at `0.18` on M20771. Some Wall Extruder DGs run a point on node
5 and some on node 6. None of that is a fault.

So this list says what the machine **has**. What each point is numbered is read off the log in
front of you, and the app now prints exactly that - the name, and this machine's own numbers:

```
  OUTPUTS
      IO-HorizStudClamp            4.12 floating, 4.13 fixed
      IO-PlateClamp                4.4 fixed, 4.5 floating
      IO-RackLock                  0.1 shared
```

The same applies across control systems. CLX and Omron builds of a model have the same names and
different numbering - CLX writes `COM7-6.5` on the older serial protocol and
`TCP192.168.50.2-3.17` since the move to TCP, Omron writes a bare `192.168.250.1-4.2`. The
transport prefix is stripped before anything is compared.

## What is still worth flagging

A name the model has that **never moved in this log**. A log records changes, so a sensor that
never came on and a sensor that is not fitted look identical - and that is the gap that cost us
the M21737 case, where a plate support that never came down left no trace at all.

What is deliberately **not** said is where the missing one is numbered. That was the original
mistake: an offset read off three pairs predicted `0.3`, the real address was `2.7`, and on the
next machine it would have been different again.

## Sides

Fifteen points now carry a side. They were **measured, not guessed**, two different ways.

**Inputs - the machine's own words.** When a plate is lost the machine writes
`Lost product, revert and try again. Fixed Product: X Floating: Y`, which names the side.
Across 25 of those messages on M21844, the side it named was always the one whose address was
reading 0: `192.168.250.1-4.2` every time it said the fixed side, `4.4` for the floating side,
and both when it said both. **25 out of 25.**

**Outputs - CloudLog/maint_data.json.** That file, previously written off as never populated,
is populated on both machines. It carries the current hour's on-count and on-time for every
output **under its real name**: `FixedSide/PlateClamp`, `FloatingSide/UpperGripper`,
`CommonIO/RackLock`. The on-times run to seven decimal places, so matching them back to the
addresses in MachineLog.txt is a fingerprint match - the ones that landed came within a few
hundredths of a second.

It only separates a pair whose two halves ran for **measurably different** lengths of time in
the counted hour. Twenty-six could not be separated: both sides clamped and released together
all hour, so their on-times are identical and nothing distinguishes them. Those are left
unnamed.

**The sides do not follow a bit order.** Lower-bit-is-fixed holds for the gripper, plate clamp
and upper gripper pairs in module 4, then breaks on the horizontal stud clamp, where `4.12` is
the floating side and `4.13` the fixed. Reading a side off a neighbouring pair is a guess.

| Address | Kind | Side |
|---|---|---|
| `192.168.250.1-4.2` | Input | Fixed (PlatePresentSwitch) |
| `192.168.250.1-4.4` | Input | Floating (PlatePresentSwitch) |
| `192.168.250.1-0.0` | Output | Fixed (PlatePresentBypass) |
| `192.168.250.1-0.14` | Output | Fixed (StudPinUp2) |
| `192.168.250.1-1.9` | Output | Floating (StudPinUp2) |
| `192.168.250.1-4.4` | Output | Fixed (PlateClamp) |
| `192.168.250.1-4.5` | Output | Floating (PlateClamp) |
| `192.168.250.1-4.8` | Output | Fixed (UpperGripper) |
| `192.168.250.1-4.9` | Output | Floating (UpperGripper) |
| `192.168.250.1-4.10` | Output | Fixed (LowerGripper) |
| `192.168.250.1-4.11` | Output | Floating (LowerGripper) |
| `192.168.250.1-4.12` | Output | Floating (HorizStudClamp) |
| `192.168.250.1-4.13` | Output | Fixed (HorizStudClamp) |
| `192.168.250.1-0.1` | Output | Shared (RackLock) |
| `192.168.250.1-1.5` | Output | Shared (SideClamp) |

Note `192.168.250.1-4.2` and `4.4` appear as both an input and an output at different
addresses in the two spaces. Inputs and outputs are separate address spaces, which is why a
point is only ever identified by **(kind, name, address)** together.

## The eight points M21844 added

| Address | Kind | Name |
|---|---|---|
| `192.168.250.1-0.1` | Input | Control Box 1 |
| `192.168.250.1-0.20` | Input | UpperGunLowerIsHigh |
| `192.168.250.1-2.2` | Input | UpperGunLowerIsHigh |
| `192.168.250.1-4.3` | Input | THNTD (second station) |
| `192.168.250.1-0.2` | Output | IO-UpperGunLowerGoHigh |
| `192.168.250.1-1.6` | Output | IO-UpperGunLowerGoHigh |
| `192.168.250.1-0.3` | Output | IO-UpperGunUpperGoHigh |
| `192.168.250.1-1.7` | Output | IO-UpperGunUpperGoHigh |

`192.168.250.1-0.3` is worth a note. An earlier answer predicted the second `PlateSupportDown`
**input** there by extrapolating a pairing offset. The real one is at `2.7`, and `0.3` is not
an input at all - it is an output, `IO-UpperGunUpperGoHigh`.


## Inputs

| Address | Name | Pairs with | Changes | Ends |
|---|---|---|---|---|
| `192.168.250.1-0.4` | ResetPB | - | 28 | Off |
| `192.168.250.1-0.6` | TrolleyBottomClampOpen | `192.168.250.1-1.4` | 1 | On |
| `192.168.250.1-0.7` | TrolleyBottomClampClosed | `192.168.250.1-1.5` | 167 | On |
| `192.168.250.1-0.8` | TrolleyTopClampOpen | `192.168.250.1-1.6` | 139 | On |
| `192.168.250.1-0.10` | PlateClampUp | `192.168.250.1-1.8` | 157 | On |
| `192.168.250.1-0.12` | PlateClampLifted10mm | `192.168.250.1-1.10` | 909 | On |
| `192.168.250.1-0.13` | TopStudClampUp | `192.168.250.1-1.11` | 915 | On |
| `192.168.250.1-0.15` | HorizStudClampDown | `192.168.250.1-1.13` | 791 | On |
| `192.168.250.1-0.16` | StudPinDown | `192.168.250.1-1.14` | 649 | On |
| `192.168.250.1-0.17` | StudPin2Down | `192.168.250.1-1.15` | 155 | On |
| `192.168.250.1-0.19` | UpperGunUpperIsLow | `192.168.250.1-2.1` | 1 | On |
| `192.168.250.1-0.21` | UpperGunLowerIsLow | `192.168.250.1-2.3` | 1 | On |
| `192.168.250.1-1.0` | PlateSupportUp | `192.168.250.1-2.6` | 151 | On |
| `192.168.250.1-1.1` | PlateSupportDown | `192.168.250.1-2.7` | 139 | On |
| `192.168.250.1-1.3` | SideClampOff | - | 1 | On |
| `192.168.250.1-1.4` | TrolleyBottomClampOpen | `192.168.250.1-0.6` | 1 | On |
| `192.168.250.1-1.5` | TrolleyBottomClampClosed | `192.168.250.1-0.7` | 167 | On |
| `192.168.250.1-1.6` | TrolleyTopClampOpen | `192.168.250.1-0.8` | 139 | On |
| `192.168.250.1-1.8` | PlateClampUp | `192.168.250.1-0.10` | 156 | Off |
| `192.168.250.1-1.10` | PlateClampLifted10mm | `192.168.250.1-0.12` | 905 | On |
| `192.168.250.1-1.11` | TopStudClampUp | `192.168.250.1-0.13` | 907 | On |
| `192.168.250.1-1.13` | HorizStudClampDown | `192.168.250.1-0.15` | 769 | On |
| `192.168.250.1-1.14` | StudPinDown | `192.168.250.1-0.16` | 629 | On |
| `192.168.250.1-1.15` | StudPin2Down | `192.168.250.1-0.17` | 157 | On |
| `192.168.250.1-2.1` | UpperGunUpperIsLow | `192.168.250.1-0.19` | 1 | On |
| `192.168.250.1-2.3` | UpperGunLowerIsLow | `192.168.250.1-0.21` | 1 | On |
| `192.168.250.1-2.6` | PlateSupportUp | `192.168.250.1-1.0` | 151 | On |
| `192.168.250.1-2.7` | PlateSupportDown | `192.168.250.1-1.1` | 1 | On |
| `192.168.250.1-2.9` | RackLockOff | - | 1,156 | Off |
| `192.168.250.1-2.10` | GunAirOK | - | 1 | On |
| `192.168.250.1-2.11` | ClampsAirOK | - | 1 | On |
| `192.168.250.1-4.0` | EStop | - | 1 | Off |
| `192.168.250.1-4.1` | THNTD | - | 1,624 | Off |
| `192.168.250.1-4.2` | PlatePresentSwitch | `192.168.250.1-4.4` | 151 | On |
| `192.168.250.1-4.4` | PlatePresentSwitch | `192.168.250.1-4.2` | 151 | On |
| `192.168.250.1-4.6` | GripperProductSensor | `192.168.250.1-4.7` | 163 | On |
| `192.168.250.1-4.7` | GripperProductSensor | `192.168.250.1-4.6` | 163 | On |
| `192.168.250.1-4.8` | SafetyBarPressed | - | 7 | Off |
| `192.168.250.1-4.9` | EstopResetButton | - | 10 | Off |

## Outputs

| Address | Name | Pairs with | Changes | Ends |
|---|---|---|---|---|
| `192.168.250.1-0.0` | IO-PlatePresentBypass | - | 171 | Off |
| `192.168.250.1-0.1` | IO-RackLock | - | 1,284 | Off |
| `192.168.250.1-0.4` | IO-FireLamp | `192.168.250.1-0.7` | 1,337 | On |
| `192.168.250.1-0.5` | IO-ResetLamp | `192.168.250.1-0.8` | 229 | On |
| `192.168.250.1-0.6` | IO-ClampedFireReleaseLamp | `192.168.250.1-0.9` | 2,518 | Off |
| `192.168.250.1-0.7` | IO-FireLamp | `192.168.250.1-0.4` | 1,337 | On |
| `192.168.250.1-0.8` | IO-ResetLamp | `192.168.250.1-0.5` | 229 | On |
| `192.168.250.1-0.9` | IO-ClampedFireReleaseLamp | `192.168.250.1-0.6` | 2,518 | Off |
| `192.168.250.1-0.12` | IO-EstopResetLamp | - | 2 | Off |
| `192.168.250.1-0.13` | IO-StudPinUp | `192.168.250.1-1.8` | 654 | Off |
| `192.168.250.1-0.14` | IO-StudPinUp2 | `192.168.250.1-1.9` | 160 | Off |
| `192.168.250.1-1.0` | IO-PlateClampLock | `192.168.250.1-1.11` | 907 | On |
| `192.168.250.1-1.1` | IO-PlateClampLift10mm | `192.168.250.1-1.12` | 915 | On |
| `192.168.250.1-1.2` | IO-PlateSupport | `192.168.250.1-1.13` | 159 | On |
| `192.168.250.1-1.5` | IO-SideClamp | - | 852 | Off |
| `192.168.250.1-1.8` | IO-StudPinUp | `192.168.250.1-0.13` | 630 | Off |
| `192.168.250.1-1.9` | IO-StudPinUp2 | `192.168.250.1-0.14` | 162 | Off |
| `192.168.250.1-1.11` | IO-PlateClampLock | `192.168.250.1-1.0` | 901 | On |
| `192.168.250.1-1.12` | IO-PlateClampLift10mm | `192.168.250.1-1.1` | 905 | On |
| `192.168.250.1-1.13` | IO-PlateSupport | `192.168.250.1-1.2` | 159 | On |
| `192.168.250.1-4.0` | IO-LowerGunFire | `192.168.250.1-4.2` | 1,152 | Off |
| `192.168.250.1-4.1` | IO-UpperGunFire | `192.168.250.1-4.3` | 1,152 | Off |
| `192.168.250.1-4.2` | IO-LowerGunFire | `192.168.250.1-4.0` | 1,142 | Off |
| `192.168.250.1-4.3` | IO-UpperGunFire | `192.168.250.1-4.1` | 1,142 | Off |
| `192.168.250.1-4.4` | IO-PlateClamp | `192.168.250.1-4.5` | 914 | Off |
| `192.168.250.1-4.5` | IO-PlateClamp | `192.168.250.1-4.4` | 904 | Off |
| `192.168.250.1-4.6` | IO-PlateClampUp | `192.168.250.1-4.7` | 160 | Off |
| `192.168.250.1-4.7` | IO-PlateClampUp | `192.168.250.1-4.6` | 156 | Off |
| `192.168.250.1-4.8` | IO-UpperGripper | `192.168.250.1-4.9` | 163 | On |
| `192.168.250.1-4.9` | IO-UpperGripper | `192.168.250.1-4.8` | 163 | On |
| `192.168.250.1-4.10` | IO-LowerGripper | `192.168.250.1-4.11` | 167 | On |
| `192.168.250.1-4.11` | IO-LowerGripper | `192.168.250.1-4.10` | 167 | On |
| `192.168.250.1-4.12` | IO-HorizStudClamp | `192.168.250.1-4.13` | 780 | Off |
| `192.168.250.1-4.13` | IO-HorizStudClamp | `192.168.250.1-4.12` | 804 | Off |
| `192.168.250.1-4.14` | IO-TopStudClamp | `192.168.250.1-4.15` | 922 | Off |
| `192.168.250.1-4.15` | IO-TopStudClamp | `192.168.250.1-4.14` | 914 | Off |
