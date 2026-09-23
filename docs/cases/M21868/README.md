# M21868 - Mainland, Component Nailer V2

Full SDN folder sent 23 September 2026: ProdLogV2 weeks 5 and 24-39 of 2026, four support bundles
(three on 8 July, one on 17 September). Omron, 192.168.250.1. SDN V2.4.3.1.

## Production

Imported under M21868 from the folder name. 17 weeks read, 3 empty weeks skipped, the duplicate
week 38 in `Logs/Support/Reports` read once. The two `ProdLog2017*.log` files are the old format
from the install this SDN folder was copied from and are not read.

This is the machine that showed a Component Nailer counts **components**, not panels - see
[production-reports.md](../../production-reports.md#component-nailers-count-components-not-panels).
5,094 components, about 64 a production day, 0.37% went wrong.

## Support bundle 8 July - "fires two nails on 140 mm and won't move to the next nog"

90 mm timber is fine; 140 mm fires both guns once and stops. The log shows why:

1. Step 230 says `Waiting for Gun(s) to Correct Heights` and turns on `IO-UpperGunLowerGoHigh`
   (output 0.3) - on 140 mm the upper gun's lower cylinder has to go high for the next nails.
2. The confirming input `UpperGunLowerIsHigh` (0.6, InUse, not simulated) **never changes once in
   any of the four bundles**. `UpperGunLowerIsLow` (0.5) does not even drop while the output is on.
3. So step 230 waits for ever. That was 66 times in the third July bundle, none answered within 5 s.
4. When the output turns off, `UpperGunLowerIsLow` blips 0 then 1 about 0.4 s later - the cylinder
   lifting a hair and dropping back, or the valve acting the wrong way round.

Look at: the upper gun lower cylinder and its high reed (0.6) - does the cylinder reach the top,
is the reed there and set, does the valve on output 0.3 shift. In the September bundle that move
was never asked for and 68 components went through, which fits the operator's 90 mm working.

## Support bundle 17 September - "clamp won't engage and clamp nog" (panel 9, soffit nog)

- The machine flagged `Incorrect Nog Height From Top Of Stud, Expected : 45.0 Got:` anywhere from
  about 12 to 30 mm (once -5.4) through the whole session - 07:10, 07:12-07:16, 07:50-07:51,
  09:36-09:38, 10:43. It stops for the operator each time.
- Worth checking whether a soffit nog is meant to sit at 45 mm from the top of the stud, or whether
  the nog height setting or sensor is off for that member.
- **Not a lead:** `ClampAirOK` (1.7) changes to 1 on the very last line, 10:55:22. Five other inputs
  "change" in the same instant, four never seen before that session, and in the July bundles none
  of them logged at all. The log does not record input states at start-up, so that burst is the
  software reading its inputs again, not the air coming back.

## Tool fix from this case

The report said `StudTrolley >>> Disabled since 10:43:25` while the trolley was moving at 10:52.
"Axis Disabled" is logged as a motion event but "Axis Enable" under Other, and only motion events
were read. Both are read now.

## Not this machine

The SDN folder's root `SupportInfo.txt` says TornadoM450, Carters Auckland, and the folder carries
`TornadoM450.xml`, `RakingWallExtruderV3DG.xml` and 2017 production logs. That is the install image
it was copied from. The support bundles' own files say ComponentNailerV2 / Mainland.
