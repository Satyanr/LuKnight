# 🌙 Lu-Knight

**Lu-Knight** is an interactive Windows desktop companion that combines an animated character, physics, autonomous behavior, AI conversation, local desktop awareness, and safe Windows automation.

Unlike a traditional chatbot, Lu-Knight actually lives on your desktop.

It can walk around your screen, react to the user, fall, jump, hang from windows, be dragged and thrown, talk through an AI assistant, inspect explicitly requested desktop context, and execute supported Windows commands through a **local-first deterministic action engine**.

> **Current status:** Active development
> **Platform:** Windows x64
> **Framework:** WPF / .NET 10
> **AI:** Google Gemini + local deterministic engine
> **Primary development language:** C#
> **Primary interaction language:** Indonesian / English

---

## ✨ What is Lu-Knight?

Lu-Knight is designed around three ideas:

### 🧍 A Character, Not Just a Window

Lu-Knight behaves like a small desktop character.

It can:

* walk around the desktop
* idle
* sleep
* jump
* fall with gravity
* land on desktop surfaces
* interact with application windows
* hang from windows
* climb
* react to clicks
* react to AI emotion
* be grabbed
* be dragged
* be thrown

The character and assistant systems are connected, allowing AI interactions to influence Lu-Knight's mood and behavior.

---

### 🧠 An AI Assistant

Lu-Knight contains an assistant system with:

* Gemini conversation
* personality system
* short-term conversation context
* long-term memory
* remember / forget behavior
* local fallback responses
* conversation concurrency protection
* character emotion integration
* desktop context providers
* intent routing
* local tool routing

Gemini is used when natural-language reasoning, analysis, vision, or generated responses are actually required.

It is **not** used as the direct Windows executor.

---

### 🖥️ A Local-First Windows Companion

Deterministic desktop commands are processed locally whenever possible.

Examples:

```text
buka notepad
tolong bukain chrome dong
jalankan photoshop
bukain spotify
coba buka discord
fokuskan ke vscode
pindah ke excel
```

Explorer commands are also supported:

```text
buka downloads
buka dokumen
buka desktop
cari invoice september
carikan LuKnight di documents
cari logo di pictures
```

Supported desktop commands do not require a Gemini request.

```text
User
  ↓
Local Intent Parser
  ↓
Permission / Validation
  ↓
Confirmation
  ↓
Windows Action Engine
```

For AI conversations:

```text
User
  ↓
Assistant Router
  ↓
Gemini
  ↓
Assistant Response
```

This separation is an important part of Lu-Knight's security architecture.

---

# 🚀 Features

## Character & Physics

* Animated desktop character
* Transparent WPF desktop window
* Click interaction
* Drag & drop
* Throw physics
* Gravity
* Falling
* Landing
* Pointer tracking
* Autonomous walking
* Sleep behavior
* Jumping
* Window interaction
* Hanging
* Climbing
* Mood system
* Reaction system
* AI-driven character emotion
* Mouse and touch input handling
* Physics ownership transitions between user drag and autonomous behavior

---

## AI Assistant

* Google Gemini integration
* Local fallback mode
* Configurable assistant personality
* Short-term context
* Long-term memory
* Explicit remember / forget operations
* Response-style preferences
* Language preferences
* Conversation concurrency protection
* Character emotion integration
* Model failover

Current Gemini failover chain:

```text
Gemini 3.8 Flash
      ↓
Gemini 3.7 Flash
      ↓
Gemini 3.6 Flash
      ↓
Gemini 3.5 Flash
      ↓
Gemini 3.5 Flash-Lite
      ↓
Local fallback
```

Automatic fallback is intentionally limited to supported model-availability conditions such as:

```text
HTTP 429
HTTP 503
HTTP 404
```

Authentication failures, invalid requests, safety responses, network errors, and cancellations do not silently switch models.

---

# 🖥️ Desktop Awareness

Lu-Knight can obtain limited desktop context when allowed.

## Application Awareness

Lu-Knight can detect visible applications and basic process categories.

It does **not** automatically read every window title.

---

## File Context

Explicit file requests can be used for supported text/code files.

Example:

```text
ringkas file: C:\Project\notes.txt
```

File access is designed to be:

* read-only
* explicitly requested
* size-limited
* protected against known secret/credential files

File content is treated as context, not as permission to execute instructions contained inside the file.

---

## Clipboard Context

Example:

```text
ringkas clipboard
```

Clipboard access is:

* explicit only
* not continuously monitored
* not background-captured

---

## System Context

Lu-Knight can provide limited information such as:

* battery status
* RAM
* Windows / OS information
* network availability
* uptime

Sensitive identifiers such as the following are intentionally excluded from normal system context:

* username
* IP address
* MAC address
* Wi-Fi SSID
* hardware serial numbers

---

## Screen Context

Example:

```text
lihat layar saya
```

Lu-Knight can perform a one-shot screenshot of the primary display when explicitly requested.

The screenshot can be passed to the AI for visual understanding.

Screen capture is not intended to operate as continuous surveillance.

---

# ⚡ Local Desktop Command Engine

Lu-Knight includes a local desktop command engine designed to execute supported Windows actions without sending the command to Gemini.

Features include:

* natural Indonesian command parsing
* application aliases
* fuzzy application matching
* Start Menu discovery
* Windows App Paths discovery
* Microsoft Store / AppsFolder discovery
* packaged application support
* application index warm-up
* Explorer navigation
* Explorer Search
* app focusing
* ambiguous-app detection
* restricted executable filtering

Examples:

```text
eh tolong bukain photoshop dong
coba buka vscode
fokuskan ke chrome
tolong buka folder dokumen
bukain desktop dong
carikan logo di pictures
cari Laporan Q3-2026.pdf di documents
buka explorer dan cari LuKnight
```

See:

[`LOCAL_DESKTOP_COMMAND_ENGINE.md`](LOCAL_DESKTOP_COMMAND_ENGINE.md)

---

# 🔐 Desktop Action Security

Windows actions do not execute immediately from arbitrary AI output.

The intended flow is:

```text
Assistant
    ↓
Intent
    ↓
Action Proposal
    ↓
Security Validation
    ↓
User Confirmation
    ↓
Local Executor
```

Current protections include:

* confirmation before supported actions
* single-use action proposals
* executable validation
* arbitrary `.exe` execution blocked
* command shell access restricted
* PowerShell restricted
* CMD restricted
* Regedit restricted
* script hosts restricted
* user command arguments cannot become arbitrary executable arguments
* Gemini cannot directly execute Windows commands

Currently unsupported / intentionally restricted:

* arbitrary shell commands
* arbitrary executable paths
* automatic credential entry
* unrestricted file modification
* application kill / force-close commands

Future destructive actions will require a higher permission level.

---

# 🎙️ Voice Interaction

Voice support is currently under active development.

### Available

* Push-to-Talk microphone capture
* WAV audio capture
* 16 kHz
* 16-bit
* mono
* audio held in memory
* automatic capture timeout

### In development

```text
Microphone
   ↓
Speech-to-Text
   ↓
AssistantInputSource.Voice
   ↓
IntentRouter
   ├── Local Desktop Action
   └── Gemini
   ↓
Assistant Response
   ↓
Text-to-Speech
   ↓
Lu-Knight Speaking Animation
```

The goal is for voice and text to share the exact same assistant and permission system.

Voice commands must **not bypass desktop-action confirmation**.

Continuous always-on microphone monitoring is not the current design goal.

---

# 🛡️ Privacy Philosophy

Lu-Knight follows an explicit-access and local-first approach.

Whenever practical:

```text
Local task
→ Local processing
```

instead of:

```text
Local task
→ Cloud AI
```

Sensitive capabilities are intended to remain separately configurable.

This includes:

* Application Awareness
* File Context
* Clipboard Context
* System Context
* Screen Context
* Voice
* Desktop Actions
* future UI Automation
* future Proactive Behavior

The final v1.0 goal is for sensitive capabilities to default to **OFF** until explicitly enabled.

---

# 🔑 Gemini API Key

The Gemini API key can be configured through:

```text
Settings
→ AI & Chat
```

The key is stored in **Windows Credential Manager** using:

```text
LuKnight/GeminiApiKey
```

It is not stored directly inside the normal JSON settings file.

For development, `GEMINI_API_KEY` can also be used as a fallback environment variable.

The application can continue running without Gemini; AI-dependent features will become unavailable or use the supported local fallback behavior.

---

# 📦 Installation

There are two ways to run Lu-Knight.

## Option 1 — Installer

When a packaged GitHub Release is available, download:

```text
LuKnightSetup.exe
```

Run the installer normally.

Lu-Knight uses a **per-user installation**, so administrator privileges are not normally required.

Default installation location:

```text
%LocalAppData%\Programs\LuKnight
```

The installer can create:

* Start Menu shortcut
* optional Desktop shortcut
* uninstall entry
* optional launch after installation

The packaged release is self-contained, so users installing the packaged application do not need to separately install the .NET runtime.

---

## Option 2 — Build from Source

### Requirements

Development requires:

* Windows 10 / Windows 11
* x64 environment
* Git
* .NET 10 SDK

Clone the repository:

```powershell
git clone https://github.com/Satyanr/LuKnight.git
cd LuKnight
```

Restore dependencies:

```powershell
dotnet restore
```

Build:

```powershell
dotnet build -c Release
```

Run:

```powershell
dotnet run --project LuKnight.csproj
```

---

# 🛠️ Building the Installer

Installer packaging uses Inno Setup.

Requirements:

* .NET 10 SDK
* Inno Setup 6
* PowerShell

Example:

```powershell
./tools/Build-Release.ps1 `
    -Version 1.0.0 `
    -Iscc "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
```

Release artifacts are generated separately from the source tree.

Expected packaged outputs include:

```text
artifacts/
├── publish/
│   └── LuKnight.exe
│
└── release/
    ├── LuKnightSetup.exe
    ├── update.json
    └── checksum.sha256
```

See:

[`PHASE6_RELEASE.md`](PHASE6_RELEASE.md)

---

# 🔄 Updates

Lu-Knight contains an update system based on GitHub Releases.

The updater supports:

* periodic update checks
* manual update checks
* installer download
* SHA-256 verification
* package size validation
* temporary `.part` downloads
* install and restart
* update cancellation
* protection while character/AI activity is still running

Updates are not intended to silently install without user interaction.

---

# 💾 Configuration

Application settings are stored under:

```text
%LocalAppData%\LuKnight\settings.json
```

Configuration includes areas such as:

* General
* Character behavior
* AI / Chat
* character position
* monitor state
* Settings window position
* update state

Settings use recovery mechanisms designed to avoid permanently breaking the application when the JSON file becomes corrupt.

The Gemini API key is stored separately in Windows Credential Manager.

---

# 🖱️ System Tray

Lu-Knight supports system tray operation.

The tray is used for functionality such as:

* showing Lu-Knight
* hiding Lu-Knight
* opening Settings
* update notifications
* exiting the application

Lu-Knight also supports single-instance behavior so multiple accidental launches do not create multiple desktop companions.

---

# 🚦 Development Roadmap

## Completed

| Phase      | Description                  | Status            |
| ---------- | ---------------------------- | ----------------- |
| Phase 1    | Character Foundation         | ✅                 |
| Phase 2    | Physics                      | ✅                 |
| Phase 3    | Autonomous Behavior          | ✅                 |
| Phase 4    | Advanced Desktop Behavior    | ✅                 |
| Phase 5    | Visual / Sprite System       | ✅                 |
| Phase 6    | Productization               | ✅                 |
| Phase 7    | Assistant Core               | ✅                 |
| Phase 8    | Desktop Capabilities         | ✅                 |
| Phase 8D.5 | Local Desktop Command Engine | ✅ Mostly complete |

---

## Phase 9 — Voice & Natural Interaction

| Module                    | Status  |
| ------------------------- | ------- |
| Push-to-Talk Capture      | ✅       |
| Speech-to-Text            | 🔨 Next |
| Voice → Assistant Routing | ⬜       |
| Text-to-Speech            | ⬜       |
| Voice UX / Privacy        | ⬜       |

---

## Phase 10 — Computer Interaction / UI Automation

Planned capabilities include:

* Window targeting
* Windows UI Automation
* element-based clicking
* safe keyboard input
* controlled scrolling
* screen-assisted interaction
* permission levels
* multi-step desktop tasks

The intended architecture remains:

```text
Planner
   ↓
Validated Steps
   ↓
Permission System
   ↓
Local Windows Executor
```

Gemini will not become an unrestricted Windows executor.

---

## Phase 11 — Automation / Skills / Proactive Companion

Planned:

* Local Skill System
* Workflow Engine
* User-defined commands
* Local scheduler
* Reminders
* Context-aware companion behavior
* Rate-limited proactive suggestions
* Capability registry
* Personalization
* application preferences
* custom aliases

Example:

```text
"mulai kerja"
```

could become:

```text
Open VS Code
→ Open Chrome
→ Open Project Folder
→ Focus VS Code
```

without requiring Gemini.

---

## Phase 12 — Production Hardening / v1.0

Final development focuses on:

* architecture cleanup
* performance
* long-running stability
* security audit
* privacy review
* crash recovery
* full test suite
* installer finalization
* updater hardening
* first-run experience
* UI polish
* documentation
* Release Candidate
* v1.0

---

# 🧪 Testing

Lu-Knight includes automated checks for multiple subsystems.

Typical development checks:

```powershell
dotnet clean
dotnet build -c Release
```

Assistant checks:

```powershell
dotnet run `
  --project tests/LuKnight.RenderChecks/LuKnight.RenderChecks.csproj `
  -c Release `
  -- --assistant
```

Physics checks:

```powershell
dotnet run `
  --project tests/LuKnight.RenderChecks/LuKnight.RenderChecks.csproj `
  -c Release `
  -- --physics
```

Mouse input checks:

```powershell
dotnet run `
  --project tests/LuKnight.RenderChecks/LuKnight.RenderChecks.csproj `
  -c Release `
  -- --mouse-input
```

Local desktop command live smoke tests are intentionally separate because they can open real Windows applications.

```powershell
dotnet run `
  --project tests/LuKnight.RenderChecks/LuKnight.RenderChecks.csproj `
  -c Release `
  -- --desktop-commands-live
```

---

# 🏗️ Architecture

High-level project structure:

```text
LuKnight
│
├── Character
├── Physics
├── Behaviors
├── Visuals
├── Views
├── ViewModels
│
├── Assistant
│   ├── Conversation
│   ├── Personality
│   ├── Memory
│   ├── Context
│   ├── Intent
│   └── Tools
│
├── Services
│   ├── Desktop Awareness
│   ├── File Context
│   ├── Clipboard
│   ├── System Context
│   ├── Screen Context
│   ├── Desktop Commands
│   └── Voice
│
├── Models
├── Assets
├── installer
├── tools
└── tests
```

One of the long-term architecture goals is to keep window/UI classes from becoming responsible for assistant, automation, physics, and platform logic at the same time.

---

# 🧭 Core Design Principles

### Local First

If an action can be safely resolved deterministically on the local machine, it should not require Gemini.

### Explicit Permission

Sensitive actions should require explicit capability permission and, when appropriate, user confirmation.

### AI Is Not the Executor

Gemini can understand or plan.

The local validated action engine executes Windows operations.

### Privacy by Design

Desktop context should not be collected continuously without a clear reason.

### Character First

Lu-Knight is not intended to become a normal chat window with a mascot attached.

Physics, animation, interaction, emotion, and autonomous behavior remain core parts of the project.

---

# ⚠️ Current Development Notes

Lu-Knight is still under active development.

Some features are incomplete or may change before the final v1.0 release.

In particular:

* Speech-to-Text is still under development
* Text-to-Speech is not complete
* Windows UI Automation is planned for Phase 10
* Workflow / Skill automation is planned for Phase 11
* full production security/privacy audit is planned for Phase 12
* application signing is not yet part of the current packaged build
* physical desktop behavior still requires manual regression testing in addition to automated tests

Do not treat the current development build as a fully hardened security boundary.

---

# 📚 Additional Documentation

More technical documentation is available inside this repository:

* [`ASSISTANT_CORE.md`](ASSISTANT_CORE.md)
* [`PERSONALITY_SYSTEM.md`](PERSONALITY_SYSTEM.md)
* [`LOCAL_DESKTOP_COMMAND_ENGINE.md`](LOCAL_DESKTOP_COMMAND_ENGINE.md)
* [`PHASE8_REVIEW.md`](PHASE8_REVIEW.md)
* [`INPUT_REGRESSION.md`](INPUT_REGRESSION.md)
* [`PHASE6_RELEASE.md`](PHASE6_RELEASE.md)
* [`BEHAVIOR_SETTINGS.md`](BEHAVIOR_SETTINGS.md)
* [`SETTINGS_WINDOW.md`](SETTINGS_WINDOW.md)
* [`SYSTEM_TRAY.md`](SYSTEM_TRAY.md)
* [`STARTUP.md`](STARTUP.md)
* [`SPRITE_GUIDE.md`](SPRITE_GUIDE.md)

---

# 🤝 Contributing

Lu-Knight is currently undergoing rapid architectural development.

If you want to contribute:

1. Fork the repository.
2. Create a feature branch.
3. Keep Windows actions local-first where possible.
4. Do not allow AI output to directly execute arbitrary Windows commands.
5. Add tests for new routing, action, physics, or security behavior.
6. Open a Pull Request describing the change.

Please avoid changes that bypass the permission or confirmation model.

---

# 📄 License

A public software license has **not yet been declared** in this repository.

Before distributing Lu-Knight as an open-source project, a `LICENSE` file should be added defining the permitted use, modification, and redistribution terms.

---

# 🌙 Lu-Knight

Lu-Knight aims to become more than a desktop chatbot.

The goal is a character that can:

**live on your desktop, understand you, react to you, help you, and safely interact with your computer — without giving the AI unrestricted control of Windows.**

```text
Character
   +
Physics
   +
Assistant
   +
Local Desktop Intelligence
   +
Voice
   +
Safe Automation
   =
Lu-Knight
```
