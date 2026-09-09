using System.Threading;
using System.Threading.Tasks;

namespace LuKnight.Services;

public interface IChatService
{
    string DisplayName { get; }

    Task<string> SendMessageAsync(
        string message,
        CancellationToken cancellationToken = default);
}
