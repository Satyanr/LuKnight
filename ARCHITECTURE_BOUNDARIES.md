# Lu-Knight Architecture Boundaries

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
