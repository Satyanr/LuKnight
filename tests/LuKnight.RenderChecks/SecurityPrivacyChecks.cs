using System.IO;
using System.Net.Http;
using System.Text.Json;
using LuKnight.Assistant;
using LuKnight.Models;
using LuKnight.Services;


internal static partial class Program
{
    private sealed class
        SecretCredential :
            ICredentialService
    {
        public const string Secret =
            "AIza_PRIVATE_API_KEY_SENTINEL_928173";


        public string? Read() =>
            Secret;


        public void Write(
            string key)
        {
        }


        public void Remove()
        {
        }
    }


    private sealed class MessageAccessTrapException : Exception
    {
        public override string Message => throw new InvalidOperationException("Exception Message must not be read.");
        public override string ToString() => throw new InvalidOperationException("Exception ToString must not be read.");
    }

    private static void
        CheckSecretDiagnostics()
    {
        var chat =
            new ChatCoordinator(
                new SecretCredential(),
                new ChatSettings
                {
                    Provider =
                        ChatProvider.Gemini
                },
                () =>
                    null);


        Require(
            chat.HasKey,
            "Credential fixture was not loaded.");


        Require(
            !chat.CredentialStatus.Contains(
                SecretCredential.Secret,
                StringComparison.Ordinal) &&
            !chat.Status.Contains(
                SecretCredential.Secret,
                StringComparison.Ordinal) &&
            !chat.DisplayName.Contains(
                SecretCredential.Secret,
                StringComparison.Ordinal),
            "API key appeared in chat status metadata.");

        const string privatePath =
            @"C:\Users\PrivateUser\Clients\SECRET_PROJECT\credentials.json";


        var failure =
            new IOException(
                $"Could not access {privatePath}");


        string diagnostic =
            DiagnosticPrivacy.TraceFailure(
                "Security test",
                failure);


        Require(
            diagnostic.Contains(
                nameof(IOException),
                StringComparison.Ordinal) &&
            !diagnostic.Contains(
                privatePath,
                StringComparison.OrdinalIgnoreCase) &&
            !diagnostic.Contains(
                "SECRET_PROJECT",
                StringComparison.OrdinalIgnoreCase) &&
            !diagnostic.Contains(
                failure.Message,
                StringComparison.Ordinal),
            "Diagnostic formatter leaked exception text.");

        string root =
            Path.Combine(
                Path.GetTempPath(),
                "LuKnight-private-path-" +
                Guid.NewGuid()
                    .ToString("N"));


        Directory.CreateDirectory(
            root);


        try
        {
            string blocker =
                Path.Combine(
                    root,
                    "PRIVATE_SCHEDULE_PARENT");


            File.WriteAllText(
                blocker,
                "block-directory");


            string privateSchedulePath =
                Path.Combine(
                    blocker,
                    "SECRET_SCHEDULE.json");


            var store =
                new LocalScheduleStore(
                    privateSchedulePath);


            string corruptSchedulePath = Path.Combine(root, "SECRET_SCHEDULE.json");
            File.WriteAllText(corruptSchedulePath, "{ invalid " + SecretCredential.Secret);
            LocalScheduleLoadResult corrupt = new LocalScheduleStore(corruptSchedulePath).Load();
            Require(corrupt.Issues.Count == 1 &&
                !corrupt.Issues[0].Message.Contains(root, StringComparison.OrdinalIgnoreCase) &&
                !corrupt.Issues[0].Message.Contains(SecretCredential.Secret, StringComparison.Ordinal),
                "Schedule load issue leaked exception path or content.");

            bool saved =
                store.Save(
                    Array.Empty<
                        ScheduledSkill>(),
                    out string scheduleError);


            Require(
                !saved,
                "Invalid schedule destination unexpectedly saved.");


            Require(
                !scheduleError.Contains(
                    root,
                    StringComparison.OrdinalIgnoreCase) &&
                !scheduleError.Contains(
                    "SECRET_SCHEDULE",
                    StringComparison.OrdinalIgnoreCase) &&
                !scheduleError.Contains(
                    "PRIVATE_SCHEDULE_PARENT",
                    StringComparison.OrdinalIgnoreCase),
                "Schedule persistence error leaked local path.");
        }
        finally
        {
            Directory.Delete(
                root,
                recursive:
                    true);
        }

        string skillRoot =
            Path.Combine(
                Path.GetTempPath(),
                "LuKnight-private-skills-" +
                Guid.NewGuid()
                    .ToString("N"));


        Directory.CreateDirectory(
            skillRoot);


        try
        {
            string skillPath =
                Path.Combine(
                    skillRoot,
                    "SECRET_CLIENT_SKILL.json");


            File.WriteAllText(
                skillPath,
                "{ invalid json");


            UserSkillLoadResult loaded =
                new UserSkillStore(
                    skillRoot)
                .Load();


            Require(
                loaded.Issues.Count ==
                    1,
                "Invalid skill fixture did not produce issue.");


            string message =
                loaded.Issues[0]
                    .Message;


            Require(
                !message.Contains(
                    skillRoot,
                    StringComparison.OrdinalIgnoreCase) &&
                !message.Contains(
                    "{ invalid json",
                    StringComparison.Ordinal),
                "Skill load issue leaked file path/content.");
        }
        finally
        {
            Directory.Delete(
                skillRoot,
                recursive:
                    true);
        }

        string[] sensitiveNames =
        [
            "apikey",
            "api_key",
            "geminikey",
            "credential",
            "password",
            "secret"
        ];


        foreach (var property
                 in typeof(AppSettings)
                     .GetProperties())
        {
            string normalized =
                property.Name
                    .Replace(
                        "_",
                        string.Empty)
                    .ToLowerInvariant();


            Require(
                !sensitiveNames.Any(
                    value =>
                        normalized.Contains(
                            value.Replace(
                                "_",
                                string.Empty),
                            StringComparison.Ordinal)),
                $"AppSettings unexpectedly persists sensitive property '{property.Name}'.");
        }

        foreach (var property
                 in typeof(ChatSettings)
                     .GetProperties())
        {
            string normalized =
                property.Name
                    .Replace(
                        "_",
                        string.Empty)
                    .ToLowerInvariant();


            Require(
                !normalized.Contains(
                    "apikey",
                    StringComparison.Ordinal) &&
                !normalized.Contains(
                    "password",
                    StringComparison.Ordinal) &&
                !normalized.Contains(
                    "secret",
                    StringComparison.Ordinal),
                $"ChatSettings persists sensitive property '{property.Name}'.");
        }
        Require(DiagnosticPrivacy.ExceptionTag(new MessageAccessTrapException()) == nameof(MessageAccessTrapException),
            "Exception tagging read private exception details.");
        Require(DiagnosticPrivacy.TraceFailure(" Security test ", new MessageAccessTrapException()) ==
            "[Lu-Knight] Security test failed (MessageAccessTrapException).", "Trace formatter read private exception details.");
        foreach (string operation in new[] { "", "private\noperation", new string('x', 81) })
            Require(DiagnosticPrivacy.TraceFailure(operation, failure) == "[Lu-Knight] Operation failed (IOException).",
                "Malformed diagnostic operation was not normalized.");
        var nested = new InvalidOperationException(SecretCredential.Secret, failure);
        Require(DiagnosticPrivacy.TraceFailure("Nested failure", nested) == "[Lu-Knight] Nested failure failed (InvalidOperationException).",
            "Nested exception details entered diagnostics.");

        // Both coordinator failure paths must sanitize implementation-owned exception messages.
        foreach (Exception providerFailure in new Exception[]
        {
            new InvalidOperationException(SecretCredential.Secret + privatePath),
            new HttpRequestException(SecretCredential.Secret + privatePath),
            new TimeoutException(SecretCredential.Secret + privatePath),
            new JsonException(SecretCredential.Secret + privatePath)
        })
        {
            var requests = new List<(bool KeyHeader, bool PrivateUrl)>();
            using var handler = new FakeHttp((request, _) =>
            {
                requests.Add((request.Headers.TryGetValues("x-goog-api-key", out var keys) && keys.Single() == SecretCredential.Secret,
                    !request.RequestUri!.ToString().Contains(SecretCredential.Secret, StringComparison.Ordinal)));
                return Task.FromException<HttpResponseMessage>(providerFailure);
            });
            using var client = new HttpClient(handler);
            var failingChat = new ChatCoordinator(new SecretCredential(), new ChatSettings { Provider = ChatProvider.Gemini },
                () => null, client);
            Require(!failingChat.TestConnection().GetAwaiter().GetResult(), "Provider failure unexpectedly passed connection test.");
            Require(!failingChat.Status.Contains(SecretCredential.Secret, StringComparison.Ordinal) &&
                !failingChat.Status.Contains(privatePath, StringComparison.Ordinal), "Connection status leaked implementation exception.");
            string reply = failingChat.SendMessageAsync("Public request").GetAwaiter().GetResult();
            Require(!failingChat.LastReplyWasGemini && !failingChat.Status.Contains(SecretCredential.Secret, StringComparison.Ordinal) &&
                !failingChat.Status.Contains(privatePath, StringComparison.Ordinal) && !reply.Contains(SecretCredential.Secret, StringComparison.Ordinal),
                "Provider fallback leaked exception or credential.");
            // Assert outside the handler: its exceptions are deliberately caught by the coordinator.
            Require(requests.Count > 0, "Provider error fixture did not observe an outgoing request.");
            foreach (var request in requests)
            {
                Require(request.KeyHeader, "Gemini key was not confined to the authentication header.");
                Require(request.PrivateUrl, "Gemini key appeared in request URL.");
            }
        }


    }
}
