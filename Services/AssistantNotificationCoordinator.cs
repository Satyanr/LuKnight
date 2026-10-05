using LuKnight.Assistant;
using LuKnight.Models;

namespace LuKnight.Services;


public sealed record ScheduledReminderNotification(
    Guid ScheduleId,
    string DisplayName,
    DateTimeOffset ObservedAtUtc);


public sealed record CompanionSuggestionNotification(
    string Key,
    string Message,
    DateTimeOffset ObservedAtUtc);


public sealed class AssistantNotificationCoordinator
{
    private readonly LocalSchedulerService
        _scheduler;

    private readonly LocalCompanionAdvisor
        _advisor;

    private readonly AssistantCapabilityRegistry
        _capabilities;

    private readonly Func<CompanionSettings>
        _preferences;

    private readonly Func<bool>
        _applicationContextEnabled;

    private readonly Func<bool>
        _assistantBusy;

    private readonly Func<AssistantRuntimeContext>
        _captureContext;

    private readonly CompanionSuggestionGate
        _companionGate;


    private readonly HashSet<Guid>
        _reminderNotifiedThisSession =
            [];


    private readonly Dictionary<
        string,
        CompanionSuggestionCandidate>
        _pendingCompanionSuggestions =
            new(
                StringComparer.OrdinalIgnoreCase);


    private CompanionSuggestionCandidate?
        _readyCompanion;


    public event Action<
        ScheduledReminderNotification>?
        ReminderDue;


    public event Action<
        CompanionSuggestionNotification>?
        CompanionSuggestionReady;


    public event Action?
        CompanionNotificationCancellationRequested;


    public AssistantNotificationCoordinator(
        LocalSchedulerService scheduler,
        LocalCompanionAdvisor advisor,
        AssistantCapabilityRegistry capabilities,
        Func<CompanionSettings> preferences,
        Func<bool> applicationContextEnabled,
        Func<bool> assistantBusy,
        Func<AssistantRuntimeContext> captureContext,
        CompanionSuggestionGate? companionGate = null)
    {
        _scheduler =
            scheduler ??
            throw new ArgumentNullException(
                nameof(scheduler));

        _advisor =
            advisor ??
            throw new ArgumentNullException(
                nameof(advisor));

        _capabilities =
            capabilities ??
            throw new ArgumentNullException(
                nameof(capabilities));

        _preferences =
            preferences ??
            throw new ArgumentNullException(
                nameof(preferences));

        _applicationContextEnabled =
            applicationContextEnabled ??
            throw new ArgumentNullException(
                nameof(applicationContextEnabled));

        _assistantBusy =
            assistantBusy ??
            throw new ArgumentNullException(
                nameof(assistantBusy));

        _captureContext =
            captureContext ??
            throw new ArgumentNullException(
                nameof(captureContext));

        _companionGate =
            companionGate ??
            new CompanionSuggestionGate();
    }


    public void Poll(
        DateTimeOffset now)
    {
        DateTimeOffset utc =
            now.ToUniversalTime();


        //
        // Reminder presentation has higher priority.
        //
        // Event invocation is synchronous, therefore
        // UI may MarkReminderPresented immediately
        // before companion evaluation continues.
        //

        ScheduledSkill? due =
            _scheduler
                .GetReminderCandidates(
                    utc)
                .FirstOrDefault(
                    item =>
                        !_reminderNotifiedThisSession
                            .Contains(
                                item.Id));


        if (due is not null)
        {
            ReminderDue?
                .Invoke(
                    new ScheduledReminderNotification(
                        due.Id,
                        due.DisplayName,
                        utc));
        }


        CompanionSettings preferences =
            _preferences();


        //
        // Explicit opt-in + existing application
        // awareness permission remain mandatory.
        //

        if (!preferences.Enabled ||
            !_applicationContextEnabled())
        {
            _companionGate
                .ResetObservation();

            _readyCompanion =
                null;

            _pendingCompanionSuggestions
                .Clear();


            CompanionNotificationCancellationRequested?
                .Invoke();

            return;
        }


        //
        // Proactive help must stay quiet during
        // assistant activity or actionable reminders.
        //

        if (_assistantBusy() ||
            _scheduler
                .GetReminderCandidates(
                    utc)
                .Count >
            0)
        {
            _companionGate
                .ResetObservation();

            _readyCompanion =
                null;

            return;
        }


        CompanionSuggestionCandidate?
            candidate =
                _advisor.Evaluate(
                    _captureContext(),
                    preferences,
                    _capabilities);


        CompanionSuggestionCandidate?
            ready =
                _companionGate
                    .Observe(
                        candidate,
                        utc);


        if (ready is null)
        {
            _readyCompanion =
                null;

            return;
        }


        _readyCompanion =
            ready;


        CompanionSuggestionReady?
            .Invoke(
                new CompanionSuggestionNotification(
                    ready.Key,
                    ready.Message,
                    utc));
    }


    public bool MarkReminderPresented(
        Guid scheduleId,
        DateTimeOffset presentedAt,
        out string error)
    {
        if (scheduleId ==
            Guid.Empty)
        {
            error =
                "Schedule ID tidak valid.";

            return false;
        }


        //
        // Preserve current anti-spam behavior:
        // once UI presentation succeeds, do not
        // show the same reminder again in this
        // process even if persistence fails.
        //

        _reminderNotifiedThisSession
            .Add(
                scheduleId);


        return _scheduler
            .MarkPresented(
                scheduleId,
                presentedAt,
                out error);
    }


    public bool MarkCompanionPresented(
        string key,
        DateTimeOffset presentedAt)
    {
        string normalized =
            key?.Trim() ??
            string.Empty;


        CompanionSuggestionCandidate?
            ready =
                _readyCompanion;


        if (ready is null ||
            !string.Equals(
                ready.Key,
                normalized,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }


        if (!_companionGate
                .MarkPresented(
                    ready,
                    presentedAt))
        {
            return false;
        }


        _pendingCompanionSuggestions[
            ready.Key] =
                ready;


        _readyCompanion =
            null;


        return true;
    }


    public bool TryConsumeCompanion(
        string key,
        out CompanionSuggestionCandidate candidate)
    {
        string normalized =
            key?.Trim() ??
            string.Empty;


        if (!_pendingCompanionSuggestions
                .Remove(
                    normalized,
                    out CompanionSuggestionCandidate?
                        stored))
        {
            candidate =
                default!;

            return false;
        }


        //
        // Live revalidation:
        // preferences/context may have changed
        // after notification but before click.
        //

        CompanionSuggestionCandidate?
            current =
                _advisor.Evaluate(
                    _captureContext(),
                    _preferences(),
                    _capabilities);


        if (current is null ||
            !string.Equals(
                current.Key,
                stored.Key,
                StringComparison.OrdinalIgnoreCase))
        {
            candidate =
                default!;

            return false;
        }


        candidate =
            stored;

        return true;
    }
}
