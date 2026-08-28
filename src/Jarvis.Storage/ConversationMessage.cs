namespace Jarvis.Storage;

public sealed record ConversationMessage(
    string ConversationId,
    string Role,
    string Text,
    DateTimeOffset CreatedUtc,
    DateTimeOffset ExpiresUtc);
