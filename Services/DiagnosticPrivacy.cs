namespace LuKnight.Services;


public static class DiagnosticPrivacy
{
    public static string ExceptionTag(
        Exception exception)
    {
        ArgumentNullException.ThrowIfNull(
            exception);


        //
        // Deliberately do NOT use:
        //
        // exception.Message
        // exception.ToString()
        // exception.StackTrace
        // exception.InnerException
        //
        // Those may contain:
        // paths, filenames, provider payloads,
        // device names, query text or secrets.
        //

        return exception
            .GetType()
            .Name;
    }


    public static string TraceFailure(
        string operation,
        Exception exception)
    {
        string normalized =
            operation?.Trim() ??
            string.Empty;


        if (normalized.Length is < 1 or > 80 ||
            normalized.Any(
                char.IsControl))
        {
            normalized =
                "Operation";
        }


        return
            $"[Lu-Knight] {normalized} failed " +
            $"({ExceptionTag(exception)}).";
    }
}
