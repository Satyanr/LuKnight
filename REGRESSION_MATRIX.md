# Lu-Knight Release Regression Matrix

The release regression suite is intentionally divided by side-effect level.

Run the suite from an interactive Windows desktop session. Core includes WPF
mouse capture and native cursor checks against Lu-Knight's own windows; a
restricted sandbox can prevent these checks from succeeding. Core does not
intentionally interact with unrelated user applications.

| Tier | Default | Purpose |
| --- | --- | --- |
| Core | Yes | Deterministic/local regression with no intentional interaction with arbitrary user applications |
| Native Fixture | Opt-in | Real Windows UIA/mouse/keyboard/tray operations against dedicated Lu-Knight fixtures |
| Ambient Desktop | Opt-in | Read-only inspection of currently visible user desktop windows |
| Interactive Desktop | Opt-in | Opens supported installed applications and Explorer |
| External AI | Opt-in | Uses configured Gemini credentials/network/quota |

The persisted regression report contains only test name, tier, exit status,
duration, commit and tool version.

Raw test output is never persisted by the release runner because live desktop
diagnostics may display private local window metadata.

A GitHub commit with no CI status is not evidence that the regression suite
passed.

Native fixture success does not replace physical microphone, speaker,
multi-monitor, tray-shell or installer testing.

Native Fixture tests may temporarily move the mouse, change keyboard focus,
open a dedicated Lu-Knight fixture window, and exercise Windows tray/UIA APIs.

They are isolated by randomized fixture title plus spawned-process PID and must
not target unrelated user applications. The source-only `--native-fixture-audit`
runs in the core tier to check these boundaries.

Avoid using the mouse/keyboard while this tier is running because those tests
intentionally verify native input fallback against the fixture.
