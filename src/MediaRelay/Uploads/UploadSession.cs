namespace MediaRelay.Uploads;

public enum UploadSessionState
{
    Created,
    Preparing,
    Completed
}

public enum PublicationStatus
{
    NotStarted,
    Pending,
    Succeeded,
    Failed
}

public sealed record UploadSession(
    string TokenHash,
    string SessionId,
    ulong? GuildId,
    ulong ChannelId,
    ulong UserId,
    DateTimeOffset ExpiresAt,
    string? ObjectId,
    ValidatedMedia? Media,
    UploadSessionState State,
    PublicationStatus PublicationStatus);

public sealed record CreatedUploadSession(string Token, UploadSession Session);

public sealed record UploadTransitionResult(
    bool Succeeded,
    bool WasAlreadyApplied,
    UploadSession? Session,
    string? FailureReason = null,
    bool IsInProgress = false);
