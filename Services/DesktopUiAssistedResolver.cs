namespace LuKnight.Services;

public sealed record DesktopUiAssistedResolution(
    DesktopUiControlResolution Resolution,
    bool UsedScreenEvidence);

public interface IDesktopUiAssistedResolver
{
    Task<DesktopUiAssistedResolution> ResolveAsync(
        DesktopWindowTarget window,
        DesktopUiControlResolution initial,
        CancellationToken cancellationToken = default);
}

public sealed class DesktopUiAssistedResolver
    : IDesktopUiAssistedResolver
{
    private const int MaxCandidates = 4;

    private static readonly TimeSpan Timeout =
        TimeSpan.FromSeconds(5);

    private readonly IDesktopUiScreenEvidenceService
        _screen;

    public DesktopUiAssistedResolver(
        IDesktopUiScreenEvidenceService screen)
    {
        _screen =
            screen ??
            throw new ArgumentNullException(
                nameof(screen));
    }

    public async Task<DesktopUiAssistedResolution>
        ResolveAsync(
            DesktopWindowTarget window,
            DesktopUiControlResolution initial,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            initial);

        cancellationToken.ThrowIfCancellationRequested();

        if (!initial.Ambiguous)
        {
            return new(
                initial,
                false);
        }

        DesktopUiNodeSnapshot[] candidates =
            initial.Alternatives
                .Take(MaxCandidates + 1)
                .ToArray();

        if (candidates.Length == 0 ||
            candidates.Length > MaxCandidates)
        {
            return new(
                initial,
                false);
        }

        using var timeout =
            CancellationTokenSource
                .CreateLinkedTokenSource(
                    cancellationToken);

        timeout.CancelAfter(
            Timeout);

        var mapped =
            new List<DesktopUiNodeSnapshot>();

        bool unsafeFailure =
            false;

        try
        {
            foreach (DesktopUiNodeSnapshot candidate
                     in candidates)
            {
                timeout.Token
                    .ThrowIfCancellationRequested();

                if (!DesktopUiScreenAssistPolicy
                        .ValidateNode(
                            candidate,
                            out _))
                {
                    // Policy rejection is NOT proof that
                    // another candidate is correct.
                    unsafeFailure =
                        true;

                    continue;
                }

                string fingerprint =
                    DesktopUiNodeIdentity
                        .Fingerprint(
                            candidate);

                DesktopUiScreenEvidenceResult result =
                    await _screen.CaptureAsync(
                        window,
                        candidate.Path,
                        fingerprint,
                        timeout.Token);

                DesktopUiScreenEvidence? evidence =
                    result.Evidence;

                try
                {
                    if (result.Success)
                    {
                        if (evidence is null ||
                            evidence.ControlPath !=
                                candidate.Path ||
                            !string.Equals(
                                evidence.ControlFingerprint,
                                fingerprint,
                                StringComparison.Ordinal))
                        {
                            unsafeFailure =
                                true;

                            continue;
                        }

                        mapped.Add(
                            candidate);

                        continue;
                    }

                    if (!result.CanEliminateCandidate)
                    {
                        unsafeFailure =
                            true;
                    }
                }
                finally
                {
                    if (evidence?.EncodedBytes is
                        { Length: > 0 } bytes)
                    {
                        Array.Clear(
                            bytes,
                            0,
                            bytes.Length);
                    }
                }
            }
            timeout.Token.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException)
            when (
                !cancellationToken
                    .IsCancellationRequested)
        {
            // Internal timeout:
            // preserve original ambiguity.
            return new(
                initial,
                false);
        }

        cancellationToken
            .ThrowIfCancellationRequested();

        // One visible candidate is only safe when
        // every rejected alternative was PROVEN
        // NotMapped.
        if (!unsafeFailure &&
            mapped.Count == 1)
        {
            DesktopUiNodeSnapshot match =
                mapped[0];

            return new(
                new DesktopUiControlResolution(
                    match,
                    [match]),
                true);
        }

        // Multiple mapped candidates can be narrowed,
        // but must remain ambiguous.
        if (!unsafeFailure &&
            mapped.Count > 1)
        {
            return new(
                new DesktopUiControlResolution(
                    null,
                    mapped),
                true);
        }

        // Zero matches OR any indeterminate/rejected
        // candidate => never guess.
        return new(
            initial,
            false);
    }
}
