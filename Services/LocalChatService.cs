using System.Threading;
using System.Threading.Tasks;

namespace LuKnight.Services;

public sealed class LocalChatService : IChatService
{
    public string DisplayName => "Local mode";

    public Task<string> SendMessageAsync(
        string message,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string reply =
            $"Aku menerima pesan: \"{message}\". " +
            "Gemini belum aktif, jadi aku sedang memakai layanan chat lokal.";

        return Task.FromResult(reply);
    }
}
