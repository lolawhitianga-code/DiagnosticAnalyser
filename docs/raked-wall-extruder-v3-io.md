# Raked Wall Extruder V3 - I/O list

Read from an **82,149 line M21737 MachineLog** covering **04:07 to 13:51 on 27 July 2026**
- nine and a half hours of production and 38,447 I/O changes.

**75 points: 39 inputs, 36 outputs.** Addresses are `IP-module.bit`.

Confidence: **inferred**. One machine, one log, not checked against a wiring diagram. A
point this machine did not use that day is not here.

**Which physical side each half of a pair is, is not known** - both sides act within the same
millisecond, so nothing in the log attributes an address to the floating or the fixed side.


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
