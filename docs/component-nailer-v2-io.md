# Component Nailer V2 - I/O

Read from the machine's own Diagnostics screens (September 2026). All on port `192.168.250.1`;
numbers are node.address. The numbers were checked against the M21868 logs, but as with every
model, **the name is what matters** - another machine may number a point differently.

In code: `MachineIoMap.ComponentNailerV2`.

## Inputs

| Point | Name | Notes |
|---|---|---|
| 0.1 | THNTD | Two-hand no tie-down. Inverted |
| 0.2 | ProductSensor | |
| 0.3 | UpperGunUpperIsLow | Gun head |
| 0.4 | UpperGunUpperIsHigh | Gun head |
| 0.5 | UpperGunLowerIsLow | Gun head |
| 0.6 | UpperGunLowerIsHigh | Gun head. The reed the July M21868 fault hung on |
| 0.7 | FenceRetracted | |
| 0.8 | HorizClampRetracted | |
| 0.9 | VertClampBackExtended | |
| 0.10 | VertClampBackRetracted | |
| 0.11 | VertFrontClampExtended | |
| 0.12 | VertFrontClampRetracted | |
| 1.0 | TableUpRetracted | Table is down |
| 1.1 | TableUpExtended | Table is up |
| 1.2 | ClampLift5mmRetracted | |
| 1.3 | ClampLift5mmExtended | |
| 1.6 | GunAirOK | |
| 1.7 | ClampAirOK | |

## Outputs

| Point | Name | Notes |
|---|---|---|
| 0.0 | IO-LowerGunFire | Gun head |
| 0.1 | IO-UpperGunFire | Gun head |
| 0.2 | IO-THNTDLED | |
| 0.3 | IO-UpperGunLowerGoHigh | Gun head |
| 0.4 | IO-UpperGunUpperGoHigh | Gun head |
| 0.5 | IO-FenceUp | |
| 0.6 | IO-HorizClamp | |
| 0.7 | IO-VertBackClamp | |
| 0.8 | IO-VertFrontClamp | |
| 0.9 | IO-VertTableUp | |
| 1.0 | IO-VertBackClampLock | Inverted |
| 1.1 | IO-VertFrontClampLock | Inverted |
| 1.2 | IO-ClampLift5mm | |
| 1.3 | IO-VertBackClampUp | |
| 1.4 | IO-VertFrontClampUp | |

The screen names outputs without the `IO-` prefix; the log adds it.

## Analog inputs

Not in the map: the log does not record these as input changes.

| Point | Name | Units | What |
|---|---|---|---|
| 1.0 | FixedSideHeight | mm | Height of the fixed side clamp |
| 1.1 | ClampAirPressure | bar | The clamps' air pressure |
| 1.2 | AirPressure | bar | Main incoming air |
| 1.3 | NogDistaceFromTopOfPlate | mm | Behind the nog height check (spelt this way on the machine) |

## Not in use

Configured but shown as Not In Use, so left out of the map. Listing them would make the report
call every one a sensor that never came on.

- Inputs: HorizClampExtended, ThreePhaseOK, LowerNailSensor, UpperNailSensor, PlateHeightOver85,
  UpperGunWoodSensor, LowerGunWoodSensor
- Output: HorizClampBack
