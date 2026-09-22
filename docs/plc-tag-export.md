# Getting the tag names out of the PLC

The name is what identifies a point; the number is whatever that machine happens to use. So a tag
export gives the half that is expensive to get from logs, and the first bundle from any machine
fills in its own numbers automatically.

A log only shows what **moved**. On the Raked Wall Extruder V3 the last few points took a full
shift to appear, and neither of the first two machines had all of them. A tag export has the lot
on the first try - including the ones that only speak when something is wrong, which are exactly
the ones worth having.

## What to export

### CLX (Studio 5000 / RSLogix 5000)

Either is fine:

- **Controller tags as CSV** - Tools -> Export -> Tags and Logic Comments, or right-click the
  controller tags and Export. Keep the descriptions.
- **The .L5X** - File -> Save As -> `.L5X`. Bigger, but it carries the alias and the description
  together.

The useful tags are usually **aliases** onto a module point (`Local:2:O.Data.3`), not base tags.
An export of base tags only will give names with no numbers, which is still worth having.

### Omron (Sysmac Studio)

- **Global variables to CSV** - the variable table, right-click -> Export, or Tools -> Export
  Variables. Keep the `AT` column and the comments.

### If neither is easy

**A plain list of names is enough.** One name per line, or a single-column CSV with `Name` as the
heading. That alone gets a model most of the way.

## What happens to it

- Names starting `IO-` are read as outputs, everything else as an input. That is the convention in
  every log seen so far, on both platforms.
- `FixedSide` / `Floating` / `CommonIO` in a name folds the two sides into one point fitted twice,
  with the side attached - which took a duty-time fingerprint to work out from logs alone.
- A trailing number is **left alone**. `StudPinUp2` is a second stud pin, not the second side of
  `StudPinUp`, and the V3 has both. If a PLC numbers its sides instead of naming them, tell me and
  I will handle that model separately rather than guessing.
- Numbers are kept where the export has them, but only as reference. The numbers that get used are
  the ones in each machine's own log.

## Order

Worth doing the ones that generate the most support calls first - three or four models, not all
fourteen. One model with a tag list, a guard list and a normal baseline is worth more than
fourteen with a name list each.

## One caveat

The reader is written against the documented shapes of both vendors' exports and tested against
realistic samples, but **not yet against a real one from your system**. The first real export will
probably need a small adjustment - send one and I will fix it before you do the rest.
