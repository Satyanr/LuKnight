# Mouse capture regression (12 September 2026)

Mouse-down handlers were attached to CharacterControl but captured MainWindow.
Subsequent move/up events could bypass the character handlers, leaving clicks
unfinished and preventing drag from starting.

CharacterControl now owns mouse capture, release on mouse-up/reset/hide, and the
explicit lost-capture handler. A move with the left button released clears stale
input state, cancels a prepared grab or ends an active grab, releases UserDrag,
and releases capture without opening chat. Touch handling is unchanged.

Validation:

- `dotnet clean` and Release build: successful, no warnings/errors.
- `--assistant`: 353 checks passed, fake Gemini only.
- `--physics`: 62 throw/input/grounding checks passed.
- `--mouse-input`: 44 checks passed outside the sandbox, including real WPF
  capture ownership, routed click open/close, missing-up cleanup before/during
  grab, capture loss, reset and hide.

The mouse input harness synthesizes routed down/up events and explicitly starts
physics grabs. It suppresses capture-induced move events only during synthetic
mouse-down because the physical button is not held; cleanup is tested separately.
This is not an end-to-end physical pointer test. Computer Use could not connect
to its native pipe in this session. Physical drag while walking/chatting, pointer
following, throw feel, and Alt-Tab during a held drag still need manual verification.

```powershell
dotnet run --project tests/LuKnight.RenderChecks/LuKnight.RenderChecks.csproj -c Release -- --mouse-input
```
