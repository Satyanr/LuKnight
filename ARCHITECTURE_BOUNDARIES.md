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
