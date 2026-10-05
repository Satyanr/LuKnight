namespace LuKnight.Assistant;

public sealed class CompanionSuggestionGate
{
    public static readonly TimeSpan
        StabilityDelay =
            TimeSpan.FromSeconds(
                90);

    public static readonly TimeSpan
        GlobalCooldown =
            TimeSpan.FromMinutes(
                30);

    public static readonly TimeSpan
        SameKeyCooldown =
            TimeSpan.FromHours(
                4);

    public const int
        MaxSuggestionsPerSession =
            3;


    private readonly Dictionary<
        string,
        DateTimeOffset>
        _lastPresentedByKey =
            new(
                StringComparer.OrdinalIgnoreCase);


    private string?
        _observedKey;

    private DateTimeOffset?
        _observedSince;

    private DateTimeOffset?
        _lastPresentedAt;

    private int
        _presentedCount;


    public int PresentedCount =>
        _presentedCount;


    public CompanionSuggestionCandidate?
        Observe(
            CompanionSuggestionCandidate?
                candidate,
            DateTimeOffset now)
    {
        DateTimeOffset utc =
            now.ToUniversalTime();


        if (candidate is null)
        {
            ResetObservation();

            return null;
        }


        string key =
            candidate.Key?.Trim() ??
            string.Empty;


        if (key.Length is < 1 or > 80 ||
            key.Any(
                char.IsControl))
        {
            ResetObservation();

            return null;
        }


        if (!string.Equals(
                _observedKey,
                key,
                StringComparison.OrdinalIgnoreCase))
        {
            _observedKey =
                key;

            _observedSince =
                utc;

            return null;
        }


        if (_observedSince is null ||
            utc <
                _observedSince.Value)
        {
            // System clock moved backwards:
            // fail closed and restart dwell.
            _observedSince =
                utc;

            return null;
        }


        if (utc -
                _observedSince.Value <
            StabilityDelay)
        {
            return null;
        }


        if (_presentedCount >=
            MaxSuggestionsPerSession)
        {
            return null;
        }


        if (_lastPresentedAt is
                DateTimeOffset last &&
            utc <
                last +
                GlobalCooldown)
        {
            return null;
        }


        if (_lastPresentedByKey
                .TryGetValue(
                    key,
                    out DateTimeOffset
                        lastSameKey) &&
            utc <
                lastSameKey +
                SameKeyCooldown)
        {
            return null;
        }


        return candidate;
    }


    public bool MarkPresented(
        CompanionSuggestionCandidate
            candidate,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(
            candidate);


        // Re-check immediately before commit.
        CompanionSuggestionCandidate?
            allowed =
                Observe(
                    candidate,
                    now);


        if (allowed is null)
            return false;


        DateTimeOffset utc =
            now.ToUniversalTime();

        string key =
            candidate.Key.Trim();


        _lastPresentedAt =
            utc;

        _lastPresentedByKey[key] =
            utc;

        _presentedCount++;


        return true;
    }


    public void ResetObservation()
    {
        _observedKey =
            null;

        _observedSince =
            null;
    }
}
