# A local model for a second-pass analysis - plan

Status: **plan only, nothing built.**

## The shape you drew, with the arrows filled in

```
  C# app  ──POST /analyse──▶  Python service  ──▶  local model
     ▲                            (FastAPI)          (weights on disk)
     │                                 │
     └────── JSON hypotheses ◀─────────┘
                    │
                    ▼
       a clearly-marked block in the report
```

The service is a separate process on the same PC, reached over `http://127.0.0.1:<port>`. Nothing
leaves the machine, which is the whole reason for doing it locally: these bundles carry customer
job files and site names.

## First, the model name

**`Qwen3.8-27B` is not a model that exists.** There is a Qwen3 family and there is a 27B model, but
they are different things and the sizes do not line up. Worth settling before anything is built,
because the choice decides the hardware.

Realistic candidates, all open-weight and runnable locally:

| Model | Size | Rough VRAM at 4-bit | Notes |
|---|---|---|---|
| Qwen3-32B | 32B dense | ~20 GB | Closest to what you described |
| Qwen3-30B-A3B | 30B, 3B active | ~18 GB | Mixture-of-experts: much faster, only 3B active per token |
| Gemma 3 27B | 27B dense | ~17 GB | This is probably where the "27B" came from |
| Qwen3-14B | 14B dense | ~9 GB | Fits a 12 GB card; the realistic option on a normal workstation |

At bf16 rather than 4-bit, multiply by roughly four - a 32B model wants ~64 GB and is out of reach
of a single consumer card.

**The question that decides this: what hardware would it run on?** A workstation with a 24 GB card
runs any of the top three at 4-bit. A normal office PC with no discrete GPU runs the 14B slowly on
CPU and nothing bigger usefully. Everything below works either way; only the speed changes.

## What it gets fed - and what it must not

Measured on real bundles in this project:

| | Tokens (rough) |
|---|---|
| The analysis report the app already writes | **~1,500** |
| A modest MachineLog.txt (1,000 lines) | ~16,600 |
| A real ErrLog.txt (40,937 lines) | **~527,000** |
| The 100,000-line Tornado MachineLog | **~1,560,000** |

So: **the model is fed the analysis, not the logs.** A local 32B model runs 32k-128k of context and
gets slower and worse long before it fills. Feeding raw logs is not a tuning problem, it is
arithmetic.

What goes in the prompt:

1. The existing text report - it is already ordered the way a technician reads.
2. What the operator typed in `SupportInfo.txt`, verbatim.
3. The structured findings the checks produced: two-hand presses, step outcomes, motor confirms,
   drive faults, plate sensor events.
4. A bounded excerpt: the last ~200 machine log lines, plus ~20 lines either side of each finding.
5. The machine's own knowledge entries - the axis map, known faults, how the machine is driven.

That is roughly 4,000-8,000 tokens. Comfortable, fast, and it is the material a technician would
actually look at.

## Where it sits in the report - and what it is not allowed to do

This project labels every interpretation Confirmed, Inferred or Unconfirmed, because the reports
get quoted to customers. A language model cannot be allowed to quietly join that scale.

**Rules the design has to enforce, not merely intend:**

- The deterministic analysis runs first and stays authoritative. The model is a **second pass** and
  never edits, reorders or suppresses a finding.
- Its output lands in its own section, marked as machine-generated and not verified.
- Nothing it says is ever labelled Confirmed. The strongest it gets is a suggestion with the
  evidence it leaned on.
- **Every log line it quotes is checked against the real log before the report is written.** Any
  quote that does not appear verbatim is dropped and the drop is counted. This is the single most
  important guard here: a fabricated log line in a Spida report is worse than no second pass at
  all, because it looks exactly like the real ones.
- A report is complete without it. Service down, model missing, timeout, nonsense JSON - the report
  is what it is today.

## What it is actually for

Not "find the fault" - the checks do that, and they do it deterministically. The model is for the
three things the checks are bad at:

1. **Tying the operator's words to the evidence.** "trolleys not moving when i hit start panel" is
   free text; matching it to an axis that never enabled is language work.
2. **Noticing a pattern nobody wrote a check for.** The checks only find what has been thought of.
   A second reader that says "the clamp came on and then the sequence went to step 0, which is
   backwards" is worth having - that is exactly what the AOR1694 case turned on.
3. **Drafting the customer-facing wording**, which a technician then edits.

Where it disagrees with a check, the check wins and the disagreement is printed. That is the same
rule already used for the machine inventory, and for the same reason.

## The contract

`POST /analyse`

```json
{ "serial": "AOR1694", "model": "RakingWallExtruderDG",
  "operator_reported": "...", "report": "...", "findings": {...}, "excerpts": ["..."] }
```

```json
{ "hypotheses": [
    { "summary": "The gun fired with no output commanded",
      "reasoning": "...",
      "confidence": "possible",
      "evidence": ["07:53:34.6431047,  InputChange, THNTD, ..."],
      "what_to_check": "The valve and the air side on the floating lower gun." } ],
  "model": "Qwen3-32B", "elapsed_ms": 4200 }
```

`GET /health` returns the loaded model and whether it is ready, so the app can grey the button
rather than time out.

Structured JSON rather than prose because the C# side already has a block system - hypotheses
become a `BulletsBlock`, evidence a `LogExtractBlock` - and because a shape can be validated where
prose cannot.

## Pieces of work

| | |
|---|---|
| `ai/service/` | Python: FastAPI, the model loader, the prompt, JSON-mode decoding |
| `ai/prompts/` | The system prompt and a per-machine-type addition |
| `Core/SecondPass/SecondPassClient.cs` | Typed HTTP client, timeout, health check |
| `Core/SecondPass/SecondPassRequest.cs` / `Response.cs` | The contract, mirrored in C# |
| `Core/SecondPass/EvidenceCheck.cs` | **Verifies every quoted line exists in the log** |
| `Core/SecondPass/SecondPassSection.cs` | Renders it into the report, clearly marked |
| `App` | A "Second opinion" button, a settings page for the URL and timeout |
| `ai/eval/` | The harness described below |

## The evaluation set already exists

The feedback packages are a labelled dataset and nobody planned it that way. Each one holds the
bundle, the report as it stood, a technician's verdict, and what was really wrong. The "missed it"
ones are exactly the cases worth measuring against.

So the harness is: run the second pass over every stored feedback package, and ask whether its
hypotheses contain what the technician said was actually wrong. That gives a number that means
something - **"it would have caught 4 of the 11 cases we know we missed"** - rather than a vague
impression that the output reads well.

That also sets the bar for shipping it. If it cannot beat the existing checks on cases the existing
checks failed, it is not earning its keep.

**One case is not a dataset.** There is one feedback package today. This needs a handful before any
number from it is worth quoting - which is an argument for using the feedback button on every case
that goes wrong, starting now, whatever happens to this plan.

## Order

1. **The contract and the C# client**, with a fake service in the tests. All of it testable here,
   and it settles the shape before any model is downloaded.
2. **The evidence check**, with tests. It is the guard everything else leans on, and it is pure
   string work.
3. **The Python service** against a small model - Qwen3-4B or similar - purely to prove the plumbing
   end to end. Correctness of the answers does not matter yet.
4. **The eval harness**, on whatever feedback packages exist.
5. **Only then** the real model, the hardware, and the prompt work. By that point there is a number
   to move.

Steps 1, 2 and 4 are the ones with lasting value. Steps 3 and 5 are replaceable - the model will be
superseded within the year, and the contract should outlive it.

## Risks worth naming

- **It reads plausibly and is wrong.** The most likely failure, and the reason for the evidence
  check and the labelling. A confident wrong answer that a technician acts on costs a site visit.
- **Nobody keeps it running.** A Python service on a Windows PC is one Windows update from being
  off. Hence: the report must be complete without it, and the app must say when it is off rather
  than silently producing less.
- **It becomes the thing people read.** If the second pass is easier to read than the real analysis,
  it will get read first. Mitigated by keeping it below the findings and visibly marked.
- **Scope.** This is a Python service, a model, a GPU, a deployment story and a prompt to maintain,
  against an analysis that already works. Worth doing for the three jobs above; not worth doing to
  replace what exists.

## Open questions

1. **What hardware?** The one thing that decides everything else.
2. **One PC or each support PC?** One machine running the service that others call is less to keep
   alive, but means log excerpts crossing the office network.
3. **Is drafting customer wording wanted at all**, or should it stay internal-only until it has
   earned trust?
4. **What is the bar?** I would suggest: it ships when it catches cases the current checks miss, on
   the feedback set, without inventing a single log line.
