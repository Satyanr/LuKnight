# Phase 12E Regression Sign-off

Lu-Knight distinguishes between PASS, FAIL, NotRun, and MissingEvidence.
A feature is never considered tested merely because its automated harness exists.

## Automated evidence

Core and Native Fixture are required for Phase 12E automated acceptance.
Ambient Desktop, Interactive Desktop and External AI are explicit opt-in tiers.
If a selected tier fails or has missing evidence, automated acceptance fails.
Every declared runner in a selected tier must have exactly one passing result
with a zero exit code. Partial or duplicate runner evidence is MissingEvidence.
Evidence must belong to current HEAD; rerun the suite after committing changes.

```powershell
./tools/Run-Regression.ps1 -IncludeNativeFixtures
./tools/New-RegressionSignoff.ps1
```

The summary and sign-off JSON/Markdown files are local, ignored artifacts in
`artifacts/regression/`. They contain sanitized statuses and commit identity.
Inspect or share these files as evidence; a missing GitHub CI status is not PASS.
The sign-off generator exits zero for automated acceptance even when physical
checks remain NotRun. `phase12EComplete` requires both automated acceptance and
all four physical checks to Pass.

## Source identity

Release evidence is valid only for a committed, clean source tree.
The regression runner and sign-off generator both reject a dirty Git working
tree, including untracked files. The runner also verifies that the tree stays
clean and HEAD stays unchanged before writing its summary.
Generated files under `artifacts/` are ignored; render-check screenshots are
written to `artifacts/renderchecks/sprites` and do not affect this check.

After any source change:

1. Commit the change.
2. Rerun regression for the new HEAD.
3. Regenerate sign-off.

Evidence from an earlier commit or an uncommitted source state is invalid.
The core `--regression-signoff` runner executes both source contract checks and
the synthetic behavior tests. Child output remains console-only.

## Physical evidence

The following require explicit physical Windows validation. Their default state
is `NotRun`; only record Pass after actually completing the corresponding checks.

| Area | Physical checklist |
| --- | --- |
| Microphone / Push-to-Talk | Enable Voice Input; hold Push-to-Talk; confirm active mic indicator; speak; release; confirm indicator off, transcription appears, and microphone stays closed. |
| Speaker / TTS | Enable TTS; send a simple message; hear playback through the speaker; verify Stop/Cancel stops speech and speech does not resume after shutdown. |
| Multi-monitor | Drag mascot to another monitor; verify position; throw/fall without teleporting; cross edges without getting stuck; hide/show returns to a valid monitor and position. |
| Windows tray shell | Hide removes the window; Show restores mascot; Open Chat and Settings work; notification click opens only a card and does not execute a workflow; Exit closes the process. |

Synthetic tray events, fake speech/provider responses, and offscreen rendering
do not establish physical PASS. Automated opt-in real-application smoke and
Gemini provider statuses remain separate from the four physical statuses.

After all four physical checks actually pass:

```powershell
./tools/New-RegressionSignoff.ps1 -Microphone Pass -Speaker Pass -MultiMonitor Pass -TrayShell Pass
```

Use Fail for a failed physical check, or NotRun for an untested one.
With only automated PASS, the correct result is `phase12EComplete: false`.

## Deferred

Installer and updater end-to-end acceptance belong to Phase 12G.
NotRun, MissingEvidence, and a missing CI status are never PASS.
