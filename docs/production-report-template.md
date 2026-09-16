# How the delivered production report works

Read out of `Extruder_DGM20771_Production1.html` (TrussTech, Raking Wall Extruder V3). This is the
authoritative implementation the reading guide pointed at - the one whose parser lives in a
`<script>` block - and it settles several things the guide left open.

## What it is

Not a report with numbers baked in. It ships **empty** and is a live tool: you drop ProdLogV2 files
(or a CSV export from the design software) onto it, and it parses, de-duplicates, classifies and
renders in the browser. The shift and break model is editable on the page, and every availability
figure moves with it.

Views are **Month / Week / Day / Hour**, and output can be measured as **panels, cube or lineal
metres** - the same three figures throughout, switched by one control.

## The classification - the part that matters

This is the answer to "what counts as a genuine fault", which could not be reproduced from the
written guide.

**Only `PanelAssembled` rows are kept.** `PanelStarted` and `MemberAssembled` are read past
entirely. That confirms what the M21737 data suggested: a `PanelStarted` with no completion is not
an abandoned panel, it is the operator moving around the HMI, and panel names are labels rather
than identities.

**`PanelStopped` does not become a record of its own.** It is matched to a completion with the same
name within **two minutes** and flags that panel `stopped`. A stop that matches no completion is
dropped.

Then each panel is judged in this order:

| Test | Result |
|---|---|
| ProdLog, `build <= 0`, `nails <= 0`, not stopped | **Stepped past** - routine, not a fault |
| `stopped` | Fault: *Stopped by the operator* |
| `nails <= 0` **and the nail counter was alive that day** - with junctions missing | Fault: *Abandoned part way* |
| `nails <= 0` **and the nail counter was alive that day** | Fault: *Ran but nailed nothing* |
| anything else | A real panel |

Then a second pass, CSV only: a cycle shorter than one minute is *Cycle too short to be real*.

Panels completed = everything that survived. Faults = the skipped ones marked as faults; stepped
past = the skipped ones that are not.

### The nail counter rule

Worth quoting, because it is the kind of thing that only comes from someone watching real data:

> Every one of the 366 panels built in November 2025 carries zero nails fired despite real build
> time and real junctions, and the counter starts reporting partway through 1 December 2025.
> Applying the no-nails rule blindly would throw that whole month away as faults.

So the rule is switched on **per day**: if no panel with real build time reports a single nail that
day, the counter is treated as offline and build time alone decides whether a panel was made.

## Availability

```
planned = (shift end - shift start) - breaks
run     = planned - startup - tail - unplanned stops
availability = run / planned
```

- **startup** - shift start to the first panel of the day.
- **tail** - last panel to shift end.
- **stops** - each gap between consecutive panels longer than the threshold.

Every one of those is measured with `nonProd(a, b)` taken out, which removes break minutes **and**
off-shift minutes lying inside the interval. So a gap spanning lunch has lunch deducted from it,
rather than the gap being counted whole or discarded whole.

It also tracks clean cycles - consecutive completions with no stop between them - and the longest
continuous run of them.

## De-duplication

Within one source, only an **exact timestamp and name match** is a reload:

> Two panels sharing a name seconds apart are two real records - the bulk writes do exactly that -
> so they must both survive.

Across sources, a match within 90 seconds on the same name is the same panel, and **the log wins
over the CSV**.

## Where our C# differs

Four of these are worth acting on.

**1. Break handling inside a gap is wrong in ours.** We discount a gap entirely if it *started*
during a break; they subtract the break minutes lying within it. A three hour stoppage that happens
to begin at lunch is currently discounted in full by ours. This is a real defect.

**2. We do not subtract startup or tail**, only the gaps between panels, so our availability reads
higher than theirs on the same data and the two are not comparable.

**3. A panel with members assembled but no nails** is Completed for us and a fault for them, when
the nail counter was live that day. We have no nail-counter rule at all, and without one the
blanket version would be worse than nothing - see November 2025.

**4. `PanelStopped` handling differs.** Ours closes an open panel and creates a record; theirs
flags a nearby completion and drops a stop that matches nothing.

Two differences that do **not** matter: we carry `Superseded` rows they never create, but we
already exclude them from the fault rate, so the headline figures agree. And our field mapping is
confirmed identical - fired, name, cube, lineal, build, idle, junctions.

## What is worth taking as a template

- The **Month / Week / Day / Hour** views with one measure control (panels / cube / lineal).
- **Where the shift went**: running, scheduled breaks, unplanned stops, start-up and tail.
- **The day drawn as a wall** - every panel as a block, coloured against load target, with breaks,
  stops and off-shift time shown in place.
- **Panels the machine did not build**, split into stepped past and went wrong, with a plain
  sentence per panel saying why.
- Production rate quoted twice - **while running** and **across the whole shift** - which stops an
  availability problem reading as a speed problem.
