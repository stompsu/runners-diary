# Training Insights prompts

Versioned inputs of the M6 AI summary (ADR 0026, 0028). Everything except this README is copied next to the API binaries as
`prompts/training-insights/` (content items in `src/RunnersDiary.Infrastructure/RunnersDiary.Infrastructure.csproj`) and checked at startup.

| File | What |
|---|---|
| `manifest.json` | `current` prompt version; per version: file, SHA-256, `estimatedLoadThreshold`, `denyList` (validator rules); supported `languages` (`en`, `pl` since v3). |
| `v<N>.md` | The agent instructions. Front matter `version: N`. **Immutable once released.** |
| `templates/<lang>.json` | Deterministic texts for empty, wholly future and < 3-session periods (no model call, no quota). A template is one draft or a list of variants `{ "when": [predicates], "summary": draft }` (same predicates as demo fixtures); the first variant whose predicates hold wins and the last must have no `when`. Used for "so far"/"yet" only when `period.isPartial` and for "session" vs "sessions" by `dq.completedSessions`. |
| `demo/<lang>/<preset>.v<N>.json` | Hand-written example summaries for guest demo presets, guarded by `requires` predicates on the fact sheet. |

All texts use the placeholder format: numbers, dates and categories only as `{fact.key}` (ADR 0026 §2).

**Deny lists per language (v3, ADR 0043 §3):** `"denyList": { "en": [...], "pl": [...] }`. A flat array (v1, v2) is the English list. A Polish
text is checked against en ∪ pl with diacritics folded, so the `pl` entries are ASCII (`sciegn*` matches "ścięgno"); `pl` must stay equal to
`NarrativeLanguages.DefaultPolishDenyList` (a test compares them). Every manifest language needs validator rules (`NarrativeLanguages`) and,
for the current version, a deny list; loading fails otherwise.

**Polish texts** (templates, demo fixtures, prompt examples) follow `docs/i18n.md`: informal "ty", gender-neutral (no past tense about the
runner), and a placeholder only where the nominative fits (after a colon, in parentheses or quotes), never a counted noun after a count.

## Change the prompt (add v2)

1. Copy `v1.md` to `v2.md`, set `version: 2` in its front matter and edit it. Never edit a released `vN.md`: a test compares its hash with the manifest.
2. Add `"2": { "file": "v2.md", "sha256": "<hash>", ... }` to `manifest.json` and set `"current": 2`. The hash is the SHA-256 of the file with LF line endings:
   `python3 -c "import hashlib;print(hashlib.sha256(open('prompts/training-insights/v2.md','rb').read().replace(b'\r\n',b'\n')).hexdigest())"`
3. Run `dotnet test --project tests/RunnersDiary.Ai.Tests`. Cached summaries with the old prompt version turn stale (ADR 0028 §2).

## Add a language

Add `templates/<lang>.json` (all three templates), the demo fixtures under `demo/<lang>/`, the language to `manifest.json` `languages`, and a line in the next prompt version. The API accepts `lang` values from the manifest only. The validators need rules for the language (`Domain/Insights/Narrative/Languages/`), and the eval cases need drafts in `tests/RunnersDiary.Ai.Tests/Evals/cases/<case>/<lang>/` (Polish: M10 C2).

## Add or change a demo fixture

Every claim in the text must be backed by a `requires` predicate (`eq`, `lt`, `lte`, `gt`, `gte`, `exists`) on fact-sheet keys; the fixture is served only while all predicates hold for the guest's own data. `DemoSummaryFixtureTests` checks the pristine demo dataset for every "today" across a year; the API checks the fixture at startup.
