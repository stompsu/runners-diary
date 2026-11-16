# Training plan prompts

Versioned inputs of the M9 training plan generation (ADR 0040). Everything except this README is copied next to the API binaries as
`prompts/training-plan/` (content items in `src/RunnersDiary.Infrastructure/RunnersDiary.Infrastructure.csproj`) and checked at startup.

| File | What |
|---|---|
| `manifest.json` | `current` prompt version; `planFactSheetVersion` (must equal `PlanFactSheet.CurrentVersion`); per version: file, SHA-256 and the `denyList` of the `forbiddenTopic` rule; supported `languages`. |
| `v<N>.md` | The agent instructions. Front matter `version: N`. **Immutable once released.** |

The model answers with the strict `training_plan` JSON schema (`TrainingPlanAgent.ResponseSchemaJson`, ADR 0040 §4). Every number it writes is
checked by `TrainingPlanValidator` against the envelope the app computed (ADR 0040 §5); the prompt explains those limits, it does not enforce them.

## Change the prompt (add v2)

1. Copy `v1.md` to `v2.md`, set `version: 2` in its front matter and edit it. Never edit a released `vN.md`: a test compares its hash with the manifest.
2. Add `"2": { "file": "v2.md", "sha256": "<hash>", "denyList": [...] }` to `manifest.json` and set `"current": 2`. The hash is the SHA-256 of the file with LF line endings:
   `python3 -c "import hashlib;print(hashlib.sha256(open('prompts/training-plan/v2.md','rb').read().replace(b'\r\n',b'\n')).hexdigest())"`
3. Run `dotnet test --project tests/RunnersDiary.Ai.Tests` (prompt manifest tests and the plan eval cases).

A change to the fact sheet or to the envelope constants bumps `planFactSheetVersion` (and `PlanFactSheet.CurrentVersion`) and needs a prompt review.

The demo example plan (`seed/demo/training-plan.json`) is not a prompt file; it is checked against the demo data for every "today" by `DemoTrainingPlanFixtureTests`.
