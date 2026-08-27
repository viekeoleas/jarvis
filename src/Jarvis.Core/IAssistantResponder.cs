namespace Jarvis.Core;

public interface IAssistantResponder
{
    Task<string> RespondAsync(string request, CancellationToken cancellationToken);
}
