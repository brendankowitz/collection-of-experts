# Eval Runner

The eval runner measures **Retrieval@K** and **LLM-judge** quality for golden question sets against a live (or mock) AgentHost instance.

## Quick start

```bash
# Start AgentHost with mock provider (no LLM keys required)
cd src/AgentHost && dotnet run &

# Run evals against fhir-server golden set
dotnet run --project evals/runner -- \
  --golden-set evals/golden-sets/microsoft__fhir-server/qa.yaml \
  --mock-provider

# Exit code 0 = PASS, 2 = metric threshold failure
```

## Options

| Flag | Default | Description |
|------|---------|-------------|
| `--golden-set <path>` | required | Path to a `qa.yaml` golden set |
| `--base-url <url>` | `http://localhost:5000` | AgentHost base URL |
| `--mock-provider` | off | Use mock judge heuristic instead of live LLM judge |
| `--thresholds <path>` | `evals/thresholds.yaml` | Path to thresholds config |

## Thresholds

Edit `evals/thresholds.yaml` to adjust pass/fail gates:

```yaml
RetrievalAtK:
  K3: 0.40   # ≥40% of questions must find ≥1 expected file in top-3
  K5: 0.55
  K10: 0.70
LlmJudge:
  MinScore: 3       # responses must average ≥ 3/5
  MinPassRate: 0.70 # 70% of questions must score ≥ MinScore
```

## Report output

Reports are written to `evals/reports/<timestamp>/`:
- `report.md` — human-readable Markdown table
- `report.json` — machine-readable JSON with per-question detail

## Nightly CI

The `nightly-evals.yml` workflow runs automatically on a schedule. It opens a GitHub issue if metrics regress. See `.github/workflows/eval.yml`.

## Interpreting results

- **Retrieval@K**: fraction of golden questions where at least one expected file appears in the agent's response. Higher K = looser. Target ≥40% @ K3.
- **LLM judge score**: a 1–5 Likert rating from a secondary LLM call judging correctness and groundedness. Average must be ≥ 3.
- **Pass rate**: fraction of questions scoring ≥ MinScore.
