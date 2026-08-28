using Jarvis.Core;

namespace Jarvis.Storage;

public sealed class RetentionCleanupService(
    ConversationHistoryStore history,
    IConversationThreadDeleter threadDeleter)
{
    public async Task<CleanupResult> RunAsync(CancellationToken cancellationToken)
    {
        var (conversations, deletedText) = await history
            .PruneExpiredTextAsync(cancellationToken)
            .ConfigureAwait(false);
        var deletedTombstones = 0;
        var pending = 0;
        foreach (var conversation in conversations)
        {
            try
            {
                if (conversation.CodexThreadId is not null)
                {
                    await threadDeleter.DeleteThreadAsync(
                        conversation.CodexThreadId,
                        cancellationToken).ConfigureAwait(false);
                }

                await history.CompleteThreadDeletionAsync(conversation.Id, cancellationToken)
                    .ConfigureAwait(false);
                deletedTombstones++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                await history.MarkThreadDeletionFailedAsync(conversation.Id, cancellationToken)
                    .ConfigureAwait(false);
                pending++;
            }
        }

        return new CleanupResult(deletedText, deletedTombstones, pending);
    }
}
