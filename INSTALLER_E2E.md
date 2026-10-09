# Installer End-to-End Acceptance

Installer E2E uses a randomized isolated Lu-Knight profile.

It does not use production:

- AppId
- install directory
- data directory
- mutex/event
- startup registry identity
- Credential Manager target

The acceptance sweep verifies:

fresh install → seed isolated profile → in-place update using `/UPDATE` +
`/TARGETPID` → settings/credential preservation → uninstall keeping data →
reinstall → uninstall removing data → clean reinstall.

The persistent evidence file is:

`artifacts/installer-e2e/installer-e2e-summary.json`

Raw process output, filesystem paths, credential values and the randomized token
are not written to the evidence file.

This test performs real per-user Windows installation and uninstallation and
must be run explicitly on Windows. It is not part of the default core
regression suite.
