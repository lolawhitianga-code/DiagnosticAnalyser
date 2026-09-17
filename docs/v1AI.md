# v1AI - what a machine manufacturer could be reporting on

Branch `v1ai`, kept off `main` so v1.1 stays exactly as it is for testing.

## The thesis

v1.1 is a good support tool. It answers **"what is wrong with this machine?"** one bundle at a
time, and after this week it answers it well.

That is one of the four questions a business like Spida gets asked. The other three are:

| Question | Who asks | Answerable today? |
|---|---|---|
| What is wrong with this machine? | support | yes - that is v1.1 |
| How is the installed base? | the owner | no |
| Who should we ring this week? | sales and support | no |
| What does this model *actually* do? | sales engineering | no |

All three of the unanswered ones need the same thing: **one row per machine instead of one report
per bundle.** That is what v1AI builds.

The asset being used here is unusual and Spida already owns it. Every bundle ever received is
measured performance for a machine you built, on a real site, with real timber and real operators.
Almost no machinery manufacturer has that, and the ones that do rarely look at it. It is sitting on
a hard drive being read one file at a time.

## What I went looking for, and what was actually there

I opened every file in a bundle, including the ones nothing has ever read.

| File | What is in it | Used? |
|---|---|---|
| `Reports/LatestReport.txt` (saw) | **Boards, member cuts, lineal metres, saw motor spans, operator logins** | **yes - new** |
| `StockList.xml` | **Member sizes and stock lengths the site has switched on** | **yes - new** |
| `MachineLog.txt`, `ErrLog.txt`, `Change.log` | faults, settings, behaviour | already used by v1.1 |
| `Reports/LatestReport.txt` (extruder) | panels | already used |
| `CloudLog/maint_data.json` | on/off counts and on-time per output, and a Servos section | **not used - see below** |
| `UserSettings.xml` | `NailsRemain*` per gun, feature flags, access rights | **not used - see below** |
| `MemberRoles.xml`, `FileMemberRoles.xml` | member naming | no value found |
| Model `.xml` files | shipped machine definitions, near-identical across bundles | no value found |

### The two finds worth having

**A saw's production was completely unreadable.** The app could read an extruder's panel log and
nothing at all from a saw, so half the installed base produced no measurable output. The formats
are different - an extruder logs panels, a saw logs boards and individual member cuts. On one
M22215 bundle that was **270 boards, 657 member cuts and 840 lineal metres across four days**,
none of which the business could see.

**Every bundle carries the customer's timber profile.** `StockList.xml` lists the member sizes and
stock lengths a site has switched on. It is the closest thing to a statement of what a customer
builds, and it has been arriving with every support bundle since the beginning without ever being
opened.

### The two I checked and deliberately did not build on

**`maint_data.json` looks like a wear counter** - `_onCount`, `_offCount`, `_onTime` per output,
plus an empty `Servos` section. Structurally it is exactly what a service interval should be built
from. On the sample it is **all zeros with an on-time of 5e-05**, so either it had just been reset
or it is not populated on this build. Building a service schedule on it would have been building on
nothing. **Worth asking Spida: is this file live on any machine?** If it is, it is the best data in
the bundle.

**`UserSettings.xml` has `NailsRemainFixedLower` and seven siblings.** They look like magazine
levels, which would make them a consumables signal. They read **30 on every gun on every machine,
including a saw with no guns at all** - so it is a warning threshold, not a level. I nearly built a
consumables report on a constant.

## What v1AI builds

### 1. Machine passport - one page per machine

What you read before you ring a customer. Identity, how long we have known it, what it produces,
what its best day looked like against its average, what timber the site runs, and what support has
cost. Built for support and sales to read the same page.

### 2. Fleet report - the installed base

- **Where the fleet is** - machines, customers, bundles, how many have gone quiet
- **Who to ring** - the radar, below
- **What each model actually does** - measured rates across the machines we have data for, with
  rows greyed where we have fewer than three machines, because one machine's habits are not a
  model's performance
- **Every machine** - the whole list
- **What this cannot tell you** - the limits, in the report, because a fleet report that does not
  carry its own caveats gets quoted to a customer inside a week

### 3. Opportunity radar - who is worth a call

Ranked, with the evidence attached. Five signals:

| Signal | For | Why it is worth a call |
|---|---|---|
| Running well under its own best | both | The gap is measured on their site with their timber, so it is not a brochure figure they can wave away |
| Sending a lot of bundles | support | Repeat bundles mean the last answer did not land |
| Gone quiet | sales | Either running beautifully or not being used, and only one is good news |
| Stocking deep material | sales | Stock lists change before machines do |
| Running old software | support | The cheapest thing we can ever do for a customer |

Deliberately conservative. On the three real bundles I had, it produced **one** entry. A radar
that lights up every week gets switched off.

## The measurement decisions, and why

**Rates are measured first-output-to-last-output on each producing day.** No site has confirmed a
roster. A rate built on a guessed roster is a number that looks measured and is not, and this one
gets quoted to customers.

**"Best day" is the machine's own best**, not another customer's and not a specification. It is the
only benchmark a customer cannot argue with: their machine has already done it, on their floor.

**`MachineStarted` / `MachineStopped` is not machine run time.** It pairs 307 times across four
days with a **median span of 25 seconds**. That is the saw motor running for a cut. It is a good
wear proxy for the blade and nothing else, and it is named `MotorRunTime` so nobody builds a
service interval on it.

**Model averages are suppressed below three machines** and shown greyed with a note.

## What this cannot do yet

- **No money in it.** No price, no margin, no service cost, no labour rate. It can tell you where
  the hours are going and not what they are worth. Adding a single editable hourly rate would turn
  the headroom figure into a dollar figure, which is the number that actually sells a machine - but
  it needs a real rate from Spida, not one I invented.
- **Only machines that have sent a bundle.** A machine running perfectly and never sending one is
  invisible, and those are the best ones. A fleet list from the sales system would fix this and
  would immediately show which machines we have never heard from.
- **No trend yet.** With one bundle per machine there is nothing to trend. The snapshot is built to
  carry it - `EnoughHistoryForATrend` is already there - but with real history, "output down 30%
  over eight weeks" is the single most valuable line this tool could print, for either team.
- **No install date or machine age.** Not in any file I found. It would make the whole
  lifecycle/replacement conversation possible, and it probably exists in Spida's sales records.
- **Customer account view not built.** Designed but not written: every machine at a site on one
  page, with the site's total trend. It is the natural next report and it needs no new data.

## A correction I owe

While researching I told you Akarana Timbers runs member sizes up to 45x290 and does heavy floor
work. **That was wrong.** My first stock list parser read the `<InUse>` tag nested inside each
stock length rather than the one belonging to the item, so every size in the file looked switched
on. A test I wrote afterwards caught it.

Akarana has six sizes defined and **two switched on: 45x90 and 45x140.** The deep sizes are
present in the file and turned off. The "deep material" signal correctly no longer fires for them.

Worth noting because it is exactly the failure mode this kind of report is prone to: a plausible,
specific, confident sentence about a customer's business, generated from a parsing mistake, that
nobody would think to check.
