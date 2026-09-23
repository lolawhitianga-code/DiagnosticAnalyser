# ProdLogV2 Field Guide: `MembersSubAssembled`

**Purpose:** reference spec for parsing the `MembersSubAssembled` line type in Spida ComponentNailer `ProdLogV2` log files. Written for ingestion by an automated diagnostic/parsing tool — every rule below has been validated against real production data, not inferred from a single example.

---

## 1. What this line represents

`MembersSubAssembled` fires once per **completed stud** — the moment a stud and all its nogs finish being nailed together as one sub-assembly. It is the stud-level completion event, as distinct from `MemberAssembled`, which fires once per individual nog nailed.

---

## 2. Field layout

```
MembersSubAssembled, <timestamp>, <nog_count>, <nails_field>, <triple_1>, <triple_2>, ..., <triple_N+1>
```

| Position | Field | Type | Notes |
|---|---|---|---|
| 0 | Event type | string | Always `MembersSubAssembled` |
| 1 | Timestamp | `YYYYMMDD HH:MM:SS` | |
| 2 | `nog_count` | int | Number of nogs on this stud |
| 3 | `nails_field` | int | **Not reliable as a literal nail count — see §5** |
| 4… | Repeating triples | `(code, cube, length_mm)` | One triple per nog, **plus exactly one extra triple for the stud itself** — total triples = `nog_count + 1` |

Each triple is `code, cube_m3, length_mm` — three comma-separated values, repeated inline (not nested/bracketed).

---

## 3. Worked example

```
MembersSubAssembled, 20260727 04:19:15, 2, 3, I, 0.001650375, 407.5,I, 0.001650375, 407.5,C, 0.0094365, 2330
```

| Field | Value |
|---|---|
| Timestamp | 2026-07-27 04:19:15 |
| `nog_count` | 2 |
| `nails_field` (logged) | 3 |
| Triple 1 | code `I`, cube `0.001650375`, length `407.5mm` |
| Triple 2 | code `I`, cube `0.001650375`, length `407.5mm` |
| Triple 3 | code `C`, cube `0.0094365`, length `2330mm` |

3 triples total = `nog_count (2) + 1` ✓

**Identifying which triple is the stud:** codes are `I, I, C` — `I` repeats, `C` appears once. The **odd-one-out code is the stud**, not the first or last triple by position (see §6). Here: stud = triple 3 (`C`), nogs = triples 1–2 (`I`, `I`).

**Deriving nog cross-section** (see §4 for the formula):
- Nog: `0.001650375 ÷ (407.5 / 1000) = 0.00405 m² = 4050mm²` → matches standard **45×90mm** dressed timber exactly.
- Stud: `0.0094365 ÷ (2330 / 1000) = 0.00405 m² = 4050mm²` → also 45×90mm (a standard NZ stud section).

**Expected vs logged nails** (see §5): 2 nogs at 90mm face width → expected `2 × 2 = 4` nails. Logged `nails_field = 3`. This is the systematic one-short undercount, not a data error to "fix" — see §5.

---

## 4. Deriving nog/stud cross-section (size)

**Formula:**
```
area_m2 = cube ÷ (length_mm / 1000)
area_mm2 = area_m2 × 1,000,000
```

Match the result against standard NZ dressed timber sizes:

| Size | Area (mm²) |
|---|---|
| 35×45 | 1,575 |
| 35×70 | 2,450 |
| 35×90 | 3,150 |
| 45×45 | 2,025 |
| 45×70 | 3,150 |
| **45×90** | **4,050** |
| 45×100 | 4,500 |
| 45×140 | 6,300 |
| 45×190 | 8,550 |
| 45×240 | 10,800 |
| 45×290 | 13,050 |
| 70×45 | 3,150 |
| 70×70 | 4,900 |
| 70×90 | 6,300 |
| 90×45 | 4,050 |
| 90×90 | 8,100 |

Pick the closest match; in practice the match is exact or within rounding.

**⚠️ Critical precision rule:** only derive size from the cube/length values on the **`MembersSubAssembled`** line. The individual `MemberAssembled` line for the same nog carries a **cube field truncated to 3 decimal places** — far too coarse for a small nog's volume, and will produce garbage areas (errors of 50%+) if used for this calculation. `MembersSubAssembled` carries full floating-point precision for every triple, including the nogs.

---

## 5. The `nails_field` is not a literal nail count

Confirmed pattern, holding across every ComponentNailer checked to date: for a stud where every nog is the common 45×90mm size, `nails_field` reads exactly `(2 × nog_count) − 1` — **never** the true `2 × nog_count`. It is short by exactly one nail, on essentially every stud, on every machine. This is a logging/counter artifact, not noise.

**Business rule for true expected nail count** (derive from nog size, not from `nails_field`):

| Nog face width | Nails per nog |
|---|---|
| 90mm | 2 |
| 140mm | 3 |
| 190mm | 4 |

Sum across all nogs on the stud for the expected total. Treat `nails_field` as unreliable for any KPI or reporting purpose — flag it as a known reporting issue rather than trusting it as ground truth.

---

## 6. Identifying the stud triple — don't assume position

The stud triple is **not reliably the last triple**, despite that being true in the common case. In a small but real fraction of records (~1.8%), the stud triple appears **first**. The only reliable identification method:

- If the triples' codes are not all identical, the **stud is the code that appears exactly once**; the nogs share a repeated code.
- If all codes happen to be identical (single-nog stud, or a coincidental match), fall back to treating the **last** triple as the stud.

Do not hardcode "last triple = stud."

---

## 7. Other known data-quality characteristics (don't treat as parser bugs)

- **Zero preceding nog events:** a small fraction of `MembersSubAssembled` records (0.68%–3.36%, varies by machine) fire with fewer — sometimes zero — individually-logged `MemberAssembled` events preceding them, despite `nog_count > 0`. This is a genuine gap in the underlying event log, not a parsing error. Exclude these studs from nog-to-nog timing calculations (nothing to time), but still count them toward stud/nog volume totals.
- **Consecutive duplicate lines** occur in ProdLog and must be deduplicated — except `MemberCut` lines, where consecutive duplicates are genuine stack duplicates and should be kept.
- **ProdLog over CSV:** where both exist, ProdLog is the primary source — CSV exports only carry panels with build > 0 and will under-report.

---

## 8. Quick reference (pseudocode)

```
nog_count      = field[2]
nails_logged   = field[3]                      # unreliable, see §5
triples        = field[4:]                     # groups of 3, count = nog_count + 1
stud_triple    = the one whose code is unique among the triples (else: last)
nog_triples    = all triples except stud_triple

for each triple (code, cube, length_mm):
    area_mm2 = cube / (length_mm / 1000) * 1_000_000
    size     = nearest_standard_size(area_mm2)   # see §4 table

expected_nails = sum(nails_per_nog(nog.size) for nog in nog_triples)
    where nails_per_nog: 90mm→2, 140mm→3, 190mm→4
```
