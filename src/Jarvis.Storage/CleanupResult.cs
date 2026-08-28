namespace Jarvis.Storage;

public sealed record CleanupResult(
    int LocalTextRecordsDeleted,
    int ThreadTombstonesDeleted,
    int ThreadDeletionsPending);
