using MediaRelay.Options;
using MediaRelay.Storage;
using MediaRelay.Discord;
using Microsoft.Extensions.Options;

namespace MediaRelay.Uploads;

public sealed class UploadService(
    UploadSessionService sessions,
    MediaValidator validator,
    ObjectIdGenerator objectIds,
    IMediaStorage storage,
    IOptions<UploadOptions> options,
    IMediaPublisher publisher,
    ILogger<UploadService> logger)
{
    public async Task<UploadOperationResult> PrepareAsync(PrepareUploadRequest request, CancellationToken ct)
    {
        var session = await sessions.FindAsync(request.SessionToken, ct);
        if (session is null) return UploadOperationResult.Error(401, "invalid_session", "The upload session is invalid or expired.");
        if (request.Size <= 0 || request.Size > options.Value.MaxUploadSize)
            return UploadOperationResult.Error(413, "size_exceeded", "The upload size is outside the allowed range.");

        ValidatedMedia media;
        try { media = validator.ValidateMetadata(request.FileName, request.ContentType); }
        catch (ArgumentException) { return UploadOperationResult.Error(415, "unsupported_media", "The file name and content type are not supported."); }

        var claim = await sessions.TryClaimPreparationAsync(request.SessionToken, ct);
        if (!claim.Succeeded) return UploadOperationResult.Error(claim.IsInProgress ? 409 : 409, "session_unavailable", "The upload session cannot be prepared.");

        var objectId = objectIds.Create(media.Extension);
        PresignedUpload authorization;
        try
        {
            authorization = await storage.CreateBrowserUploadAsync(objectId, media.ContentType, options.Value.MaxUploadSize, options.Value.PresignedUploadTtl, ct);
        }
        catch (Exception)
        {
            await sessions.ReleasePreparationAsync(request.SessionToken, CancellationToken.None);
            ct.ThrowIfCancellationRequested();
            return UploadOperationResult.Error(503, "storage_unavailable", "Upload storage is temporarily unavailable.");
        }

        var prepared = await sessions.RecordPreparedAsync(request.SessionToken, objectId, media, ct);
        if (!prepared.Succeeded) return UploadOperationResult.Error(409, "session_unavailable", "The upload session cannot be prepared.");
        return UploadOperationResult.Success(new PrepareUploadResponse(objectId, authorization.Url, authorization.Fields));
    }

    public async Task<UploadOperationResult> CompleteAsync(string token, CancellationToken ct)
    {
        var session = await sessions.FindAsync(token, ct);
        if (session is null) return UploadOperationResult.Error(401, "invalid_session", "The upload session is invalid or expired.");
        if (session.State != UploadSessionState.Completed)
        {
            if (session.State != UploadSessionState.Preparing || session.ObjectId is null || session.Media is null)
                return UploadOperationResult.Error(409, "not_prepared", "The upload session has not been prepared.");

            StoredObjectInfo? stored;
            try { stored = await storage.StatAsync(session.ObjectId, ct); }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "Upload storage verification failed for session {SessionId}", session.SessionId);
                return UploadOperationResult.Error(503, "storage_unavailable", "Upload storage is temporarily unavailable.");
            }
            if (stored is null) return UploadOperationResult.Error(409, "object_missing", "The uploaded object was not found.");
            if (stored.Size <= 0 || stored.Size > options.Value.MaxUploadSize)
                return UploadOperationResult.Error(413, "size_exceeded", "The stored object size is outside the allowed range.");

            var media = session.Media;
            if (!string.Equals(stored.ObjectId, session.ObjectId, StringComparison.Ordinal) ||
                !string.Equals(stored.ContentType, media.ContentType, StringComparison.OrdinalIgnoreCase))
                return UploadOperationResult.Error(422, "metadata_mismatch", "Stored object metadata does not match the prepared media type.");

            var transition = await sessions.RecordVerifiedCompletionAsync(token, stored.ObjectId, media, ct);
            if (!transition.Succeeded || transition.Session is null)
                return UploadOperationResult.Error(409, "completion_conflict", "The upload session could not be completed.");
            session = transition.Session;
        }

        var claim = await sessions.MarkPublicationPendingAsync(token, ct);
        if (!claim.Succeeded)
        {
            var latest = await sessions.FindAsync(token, ct) ?? session;
            if (claim.IsInProgress || latest.PublicationStatus == PublicationStatus.Succeeded)
                return Completed(latest);
            return Completed(latest);
        }

        var publicUrl = storage.GetPublicUrl(session.ObjectId!);
        bool published;
        try { published = await publisher.PublishAsync(session.ChannelId, publicUrl, ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Discord publication failed for guild {GuildId}, channel {ChannelId}, session {SessionId}", session.GuildId, session.ChannelId, session.SessionId);
            published = false;
        }

        if (published) await sessions.MarkPublicationSucceededAsync(token, CancellationToken.None);
        else
        {
            logger.LogWarning("Discord publication was not completed for guild {GuildId}, channel {ChannelId}, session {SessionId}", session.GuildId, session.ChannelId, session.SessionId);
            await sessions.MarkPublicationFailedAsync(token, CancellationToken.None);
        }
        var completed = await sessions.FindAsync(token, CancellationToken.None) ?? session;
        return Completed(completed);
    }

    private UploadOperationResult Completed(UploadSession session) => UploadOperationResult.Success(
        new CompleteUploadResponse(storage.GetPublicUrl(session.ObjectId!), session.PublicationStatus.ToString().ToLowerInvariant()));
}

public sealed record PrepareUploadRequest(string SessionToken, string FileName, string ContentType, long Size);
public sealed record CompleteUploadRequest(string SessionToken);
public sealed record PrepareUploadResponse(string ObjectId, string Url, IReadOnlyDictionary<string, string> Fields);
public sealed record CompleteUploadResponse(string Url, string PublicationState);
public sealed record UploadOperationResult(int StatusCode, object? Value, string? Code, string? Message)
{
    public static UploadOperationResult Success(object value) => new(200, value, null, null);
    public static UploadOperationResult Error(int status, string code, string message) => new(status, null, code, message);
}
