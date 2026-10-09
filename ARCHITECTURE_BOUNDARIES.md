# Lu-Knight Architecture Boundaries

## Release regression evidence

Release regression evidence is privacy-minimized.

Persisted regression summaries may contain:

- commit hash
- runner identifier
- execution tier
- exit code / pass-fail state
- duration
- runtime/tool version

They must not persist raw test stdout/stderr, desktop window titles, process
names, file paths, application paths, UI Automation trees, screenshots,
clipboard content or provider payloads.

Tests that inspect ambient user desktop state, open real applications or use an
external AI provider require separate explicit opt-in switches.

A missing external CI/check status is never treated as a successful regression
run.

### Native fixture isolation

Native regression that invokes UI Automation, mouse, keyboard, planner,
workflow, or scheduler desktop actions must target a dedicated Lu-Knight test
fixture.

Fixture targeting requires both:

- the exact spawned fixture process ID
- the exact randomized fixture window title

Fixture processes are launched directly with shell execution disabled.

Native fixture tests must not use "current window", foreground-window discovery,
or arbitrary ambient application selection as an execution target.

Ambient read-only diagnostics, real installed-application tests and external AI
tests remain separate opt-in regression tiers.

A native fixture regression may move the mouse, focus its own test window, type
into its own test field, or invoke its own UIA controls. Therefore it still
requires an interactive Windows desktop session.

## Idle performance

High-frequency desktop loops must exist only while they have active work.

When the mascot is hidden:

- autonomous behavior timers stop
- movement timer stops
- physics render subscription is suspended

Character physics is not subscribed to `CompositionTarget.Rendering` while idle.
Rendering is enabled only while pointer velocity is being sampled for a possible
grab, the character is grabbed, or the character is falling.

Resuming from hidden state resets timing baselines so hidden duration never
becomes movement delta. Non-hidden behavior pauses retain attention/blink work.

Performance optimizations must not reduce permission, confirmation, target
revalidation, or other safety checks. Callback counts inferred from timer
intervals are not a physical CPU benchmark.

### Snapshot allocation

Window/action target freshness is a safety requirement.

Performance tuning must not cache a `DesktopWindowTarget` across preparation
and execution or bypass native target revalidation. Fingerprints are computed
from the current record fields, including after a record copy changes its title.

Native window enumeration already carries Z-order; consumers should not create
additional sorted snapshots when the same ordering is available.

Resolver scoring should avoid full intermediate LINQ projections when a
single-pass scan produces identical ambiguity and ranking semantics.

### Performance acceptance

Performance acceptance protects against structural regressions, not hardware
benchmark variance. Pure/local workloads may use deliberately generous liveness
and allocation limits to catch runaway behavior.

Native operations such as installed application discovery, Win32 window
enumeration, and application-awareness capture are measured and reported for
diagnostics. Their elapsed time is not a release PASS/FAIL requirement because
it depends on the Windows installation, disk, registry, shell extensions and
running applications.

Performance measurement must never print window titles, process names, file
paths, application paths or other private desktop metadata.

Safety checks remain mandatory even when they add work:

- fresh target capture
- fingerprint revalidation
- permission recheck
- confirmation lifecycle
- UIA identity revalidation

## Trust model

Gemini may interpret or generate responses.

Gemini is never a Windows executor.

Desktop execution must follow:

User / Workflow
→ AssistantController
→ IntentRouter
→ AssistantActionRouter.Prepare
→ Permission
→ Confirmation
→ AssistantActionRouter.Execute
→ Native Executor

## AssistantController

AssistantController is the policy and execution coordinator.

Only this layer may convert an approved prepared action into
an AssistantActionRouter execution request.

### Execution authorization

AssistantActionRouter preparation may be used independently for routing and
validation, but its execution endpoint is internal to the application assembly.

AppServices and AssistantController must not publicly expose the execution router.

UI receives only an opaque proposal ID.

Only AssistantController may translate a valid, unexpired proposal ID into
Standard or Strong execution authorization.

The `Confirmation` value on a prepared action is not itself a user-consent token
outside this boundary.

## Skills

Skills are not executors.

A skill may only expand into AssistantPlan steps / raw local commands.

Skills must not store or execute:

- HWND
- UI Automation paths
- fingerprints
- coordinates
- prepared actions
- native executors
- scripts
- arbitrary command lines

## Workflow Runtime

AssistantWorkflowRuntime is a pure local state machine.

It owns:

- frozen plan
- current step
- expiry
- runtime-variable resolution
- structured step history

It must not depend on:

- AssistantIntentRouter
- AssistantActionRouter
- IAssistantAction
- UI Automation
- mouse / keyboard
- Gemini
- OS execution APIs

## Scheduler

LocalSchedulerService owns time and persisted schedule state only.

A due schedule is not permission.

Scheduler must never execute actions directly.

Scheduled workflows enter through AssistantController and keep all
normal permission and confirmation requirements.

## Proactive Companion

LocalCompanionAdvisor and CompanionSuggestionGate are advisory only.

They may produce a suggestion candidate from explicitly enabled broad
application context.

They must not:

- read screen contents
- read window titles
- read clipboard
- read files
- use microphone
- use location
- call Gemini automatically
- invoke skills automatically
- execute desktop actions

"Use prompt" creates a draft only.

The user must explicitly press Send.

## Capability Registry

AssistantCapabilityRegistry is metadata only.

It must never contain:

- executor objects
- prepared actions
- callbacks
- HWND
- UIA identity
- executable code

## Native executors

Native Windows executors are implementation details behind validated
assistant actions.

UI, scheduler, proactive companion and skill definitions must not call
them directly.

A PreparedAssistantAction is not sufficient authority to execute.

Execution requires the AssistantController confirmation lifecycle in addition to:

- current desktop permission
- current action risk
- valid proposal identity
- proposal expiry
- current workflow identity, when applicable
- strong two-stage confirmation for sensitive actions

## Notification Coordinator

AssistantNotificationCoordinator may coordinate local presentation state for:

- due reminders
- companion suggestion dwell/rate limiting
- stale suggestion invalidation

It is not an execution coordinator.

It must not depend on:

- AssistantController
- AssistantActionRouter
- prepared actions
- Gemini
- native desktop executors

A presented reminder remains pending until Run or Dismiss acknowledgement.

Presentation cooldown must not be treated as acknowledgement.

An unacknowledged reminder has priority over proactive companion suggestions.

## Request-scoped context

Explicit context sources are one-shot:

- file
- clipboard
- screen
- system status

The source reference may be sent to the AI only for the request that explicitly
requested it.

The request and reference-derived assistant response may remain visible in the
local session transcript, but must not enter future short-term provider context.

Failed context requests are also excluded from future provider context.

Deterministic desktop commands and their local responses are not conversation
context.

Workflow runtime outputs, including window/process metadata, remain local to the
workflow state and must not enter Gemini conversation history.

## Secrets and diagnostics

API keys must not be persisted in AppSettings, schedule files, skill files,
memory files, logs, or transcripts.

Gemini credentials are stored through Windows Credential Manager, with an
environment-variable fallback only when explicitly configured outside the app.

User-facing errors and diagnostics must not expose raw exception messages,
stack traces, local filesystem paths, provider response bodies, desktop
metadata, or credential material.

Diagnostic output may identify only a coarse operation and exception type.

Schedule and skill persistence errors must use stable application-owned
messages rather than filesystem exception text.

## Ambient data access

Ambient desktop data access is opt-in.

Fresh installations keep the following disabled:

- application awareness
- file context
- clipboard context
- system context
- screen context
- microphone input
- desktop actions
- proactive companion

Configured AssistantToolRouter and AssistantContextSourceRouter instances are
private implementation details of AssistantController.

AppServices and UI code must not receive direct access to live tool execution or
context-capture routers.

Explicit context capture enters through a user request routed by
AssistantController and is still subject to its current setting gate.

## Crash recovery

Persistent application state uses committed generations.

A `.tmp` file is never considered committed state and must never be restored
automatically after restart.

When the primary state is unreadable, Lu-Knight may recover only from a
validated `.bak` representing the previous committed generation.

Before replacing an invalid primary with a validated backup, the invalid file
is preserved for forensic/manual recovery when possible.

Files created by a newer schema version are never downgraded or overwritten by
an older application.

Pending assistant actions, confirmation IDs, prepared actions, workflow runtime
sessions, UIA identities, HWNDs and native execution state are session-only and
must never be crash-restored.

## Shutdown lifecycle

Shutdown is a security and stability boundary. Once shutdown begins:

- AssistantController accepts no new requests.
- Pending confirmations and workflow runtime state are invalidated.
- Active controller work receives cancellation.
- Native action execution must not begin after the shutdown barrier.
- Voice capture stops, transcription is cancelled, and text-to-speech stops.
- Proactive and scheduler polling stops.
- UI event handlers no longer submit work or restart microphone capture.

Shutdown must be idempotent. Resource cleanup must not depend on async work
finishing before the WPF dispatcher exits. Async operations retain ownership of
their local cancellation sources until completion.

No pending authorization may survive shutdown or be replayed after restart.

## Startup module isolation

Optional local modules must not prevent the core mascot and local assistant from
starting. Recoverable failures in desktop application discovery, individual user
skills, local schedule entries, startup registration repair, tray integration,
automatic update checks, and reminder or companion notification polling must
degrade only the affected feature.

Desktop application discovery commits a new catalog snapshot only after a full
successful refresh. A failed refresh retains the previous known-good snapshot;
if no snapshot exists, only the built-in safe application catalog is used.
Failed refreshes are rate-limited along with successful refreshes.

Optional startup diagnostics remain local and are never added to Gemini context,
conversation history or long-term memory. AppServices exposes at most one issue
per module without raw exception messages or filenames. Background discovery has
no execution authority and does not delay shutdown.

Programming errors in core composition are not silently converted into degraded
startup state.

## Long-run bounded state

Long-running Lu-Knight sessions must keep local runtime state bounded.

Current invariants include:

- session transcript: maximum 100 turns
- provider context: maximum 20 eligible turns
- long-term memory: maximum 200 entries
- local schedules: maximum 128
- proactive companion: maximum 3 presentations per session
- forensic invalid-state backups: maximum 3 generations per state file

Forensic retention is best-effort when filesystem access prevents deletion.
Committed primary and backup generations are outside forensic pruning.

Repeated cancellation, reminder recovery, window recreation, and persistent
state rotation must not create pending authorization, duplicate native
execution, stale temporary files, or unbounded local artifacts.

Stress acceptance validates bounded state and lifecycle behavior; it is not a
performance benchmark.

Settings commands stop accepting work when their model is disposed, and the
application shutdown barrier cancels Settings-owned operations before closing
windows. Settings cleanup is idempotent even when cancellation callbacks fail.
Settings diagnostics use the same exception-type-only privacy formatter as the
assistant and application lifecycle.

The visible chat history retains at most 100 message bubbles. Pending reminder
cards remain available when message history is trimmed or cleared. Forensic
snapshot retention orders snapshots by capture time, independently of the
primary file's original modification timestamp. The current capture is protected
even when older snapshot timestamps are in the future. Voice completion settles
on disposal or WAV finalization failure, and stale native callbacks cannot
complete a later recording session.

## First-run safety

A fresh Lu-Knight installation starts with sensitive ambient capabilities
disabled.

The first-run experience is explanatory only. Completing onboarding must not
enable:

- application awareness
- file context
- clipboard context
- system context
- screen context
- voice input
- desktop actions
- proactive companion

Existing settings schemas are migrated with onboarding already completed so an
application upgrade does not unexpectedly reopen first-run.

Closing first-run without completing it leaves onboarding incomplete and causes
it to be offered again on a later startup.

First-run completion is persistent configuration only; it is not permission for
any desktop action.

## Settings UX safety

Settings must reflect effective capability state rather than only persisted
intent.

Disabling Application awareness also disables Proactive companion. Re-enabling
Application awareness must not automatically restore Proactive companion.

Settings changes are auto-saved; the UI must not imply that a separate Save
action is required.

Where Gemini is active, the UI must disclose that explicitly enabled context
used for a request may be sent to the configured AI provider.

Enabling Desktop actions does not authorize a specific action. Permission,
target revalidation and confirmation remain required independently.

## User-facing claims

User-facing documentation must describe implemented behavior rather than roadmap
intent.

Documentation must not claim:

- an untested physical integration is PASS
- installer/updater end-to-end is complete before Phase 12G evidence
- a sensitive capability is enabled by default when fresh-install policy keeps it OFF
- Gemini directly executes Windows actions
- onboarding completion grants desktop permission

Settings privacy and action controls must expose useful accessibility names.

Keyboard-only users must be able to enter Settings navigation, move through
normal controls with standard WPF keyboard navigation, and close Settings
without requiring a pointer.

## Release artifact integrity

A release artifact must be traceable to one clean Git commit.

Packaging rejects:

- dirty tracked files
- untracked source files
- a HEAD that differs from the requested release commit
- a HEAD that changes during packaging

Release output includes machine-readable provenance binding the installer,
application binary, update manifest, version and source commit.

`update.json`, `checksum.sha256`, and release provenance must agree on the
installer SHA-256 and size.

Authenticode status is recorded during packaging. A valid signature is not
claimed until the explicit Phase 12G signing gate requires and verifies it.

The GitHub release workflow creates a draft release only. Publication remains a
separate reviewed action until final RC/v1.0 acceptance.

## Installer E2E isolation

Physical installer regression must never use Lu-Knight's production application
identity or persistent-data namespace.

An installer E2E profile uses a randomized test token and distinct:

- installer AppId
- installation directory
- Start Menu group
- startup Run value
- startup preference registry key
- process mutex
- show-request event
- settings/memory/schedule/skill/update/model directory
- Gemini credential target

The E2E profile token is restricted to a fixed 32-hex identifier and cannot
redirect Lu-Knight to an arbitrary filesystem path.

The installer E2E probe is available only when the process already runs inside
an E2E runtime profile.

Production installer defaults remain unchanged.

A physical E2E installer may delete only its own test-profile data and
credential.
