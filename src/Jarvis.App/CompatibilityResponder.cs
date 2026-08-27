using Jarvis.Core;

namespace Jarvis.App;

internal sealed class CompatibilityResponder : IAssistantResponder
{
    public Task<string> RespondAsync(string request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult($"At your service. Compatibility tracer received: {request}");
    }
}
