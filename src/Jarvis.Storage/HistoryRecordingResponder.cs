using Jarvis.Core;

namespace Jarvis.Storage;

public sealed class HistoryRecordingResponder(
    IAssistantResponder inner,
    ConversationHistoryStore history,
    Func<string?> currentThreadId) : IAssistantResponder
{
    public async Task<string> RespondAsync(string request, CancellationToken cancellationToken)
    {
        var response = await inner.RespondAsync(request, cancellationToken).ConfigureAwait(false);
        await history.AppendTurnAsync(
            currentThreadId(),
            request,
            response,
            cancellationToken).ConfigureAwait(false);
        return response;
    }
}
