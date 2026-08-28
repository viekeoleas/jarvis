namespace Jarvis.Core;

public interface IConversationThreadDeleter
{
    Task DeleteThreadAsync(string threadId, CancellationToken cancellationToken);
}
