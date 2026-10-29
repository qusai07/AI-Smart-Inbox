# AI Evals

20 labeled synthetic documents (13 English, 7 Arabic) in [`samples/`](../samples/), with "today" pinned to 2027-02-16.

| Engine | Cost | Department | Category | Priority | Safe routing* | Report |
|---|---|---|---|---|---|---|
| Rules (`rules-v1`) | Free, offline | **95%** | 95% | 90% | 100% | [EVALS-rules.md](EVALS-rules.md) |
| Hybrid: rules + local `qwen2.5:3b` for 4 unsure docs | Free, local | 90% | 90% | 90% | **100%** | [EVALS-hybrid.md](EVALS-hybrid.md) |
| Claude `claude-sonnet-5-5` | Paid | not run yet | | | | |

\* *Safe routing* = the suggestion is correct, **or** its confidence is below 0.6 so a human reviews it. This is the number that matters for trust.

**Read these numbers with care.** The rule engine's vocabulary was written alongside these 20 samples, so its scores measure fit, not real-world accuracy. A held-out set of new letters is needed for an honest figure.

In Hybrid mode, the local model and the rules check each other: when they disagree, the document goes to human review. That is why Hybrid keeps 100% safe routing even where the small local model is wrong.

Re-run:

```bash
dotnet run --project tests/SmartInbox.AiEvals -- --provider Rules     # or Ollama, Hybrid, Claude
```
