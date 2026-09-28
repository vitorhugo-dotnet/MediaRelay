using System.Security.Cryptography;
using System.Text;
using MediaRelay.Options;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace MediaRelay.Uploads;

public sealed class UploadSessionService(IMemoryCache cache, IOptions<UploadOptions> options, TimeProvider? timeProvider = null)
{
    private const string CacheKeyPrefix = "upload-session:";
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public Task<CreatedUploadSession> CreateAsync(ulong guildId, ulong channelId, ulong userId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var now = _timeProvider.GetUtcNow();
        var rawToken = ToBase64Url(RandomNumberGenerator.GetBytes(32));
        var tokenHash = HashToken(rawToken);
        var session = new UploadSession(
            tokenHash,
            Guid.NewGuid().ToString("N"),
            guildId,
            channelId,
            userId,
            now.Add(options.Value.SessionTtl),
            null,
            null,
            UploadSessionState.Created,
            PublicationStatus.NotStarted);
        var entry = new Entry(session);
        cache.Set(CacheKey(tokenHash), entry, new MemoryCacheEntryOptions
        {
            AbsoluteExpiration = session.ExpiresAt
        });
        return Task.FromResult(new CreatedUploadSession(rawToken, session));
    }

    public Task<UploadSession?> FindAsync(string token, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!IsRouteSafeToken(token)) return Task.FromResult<UploadSession?>(null);
        var hash = HashToken(token);
        if (!cache.TryGetValue<Entry>(CacheKey(hash), out var entry) || entry is null)
            return Task.FromResult<UploadSession?>(null);

        lock (entry.Gate)
        {
            if (entry.Session.ExpiresAt <= _timeProvider.GetUtcNow())
            {
                cache.Remove(CacheKey(hash));
                return Task.FromResult<UploadSession?>(null);
            }
            return Task.FromResult<UploadSession?>(entry.Session);
        }
    }

    public Task<UploadTransitionResult> TryClaimPreparationAsync(string token, CancellationToken ct) =>
        TransitionAsync(token, ct, session => session.State == UploadSessionState.Created
            ? Applied(session with { State = UploadSessionState.Preparing })
            : Failed(session, "Preparation has already been claimed or completed."));

    public Task<UploadTransitionResult> ReleasePreparationAsync(string token, CancellationToken ct) =>
        TransitionAsync(token, ct, session => session.State == UploadSessionState.Preparing && session.ObjectId is null && session.Media is null
            ? Applied(session with { State = UploadSessionState.Created })
            : Failed(session, "Preparation can no longer be released."));

    public Task<UploadTransitionResult> RecordPreparedAsync(string token, string objectId, ValidatedMedia media, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectId);
        ArgumentNullException.ThrowIfNull(media);
        return TransitionAsync(token, ct, session => session.State == UploadSessionState.Preparing && session.ObjectId is null
            ? Applied(session with { ObjectId = objectId, Media = media })
            : Failed(session, "Preparation is not available."));
    }

    public Task<UploadTransitionResult> RecordVerifiedCompletionAsync(
        string token, string objectId, ValidatedMedia media, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectId);
        ArgumentNullException.ThrowIfNull(media);
        return TransitionAsync(token, ct, session =>
        {
            if (session.State == UploadSessionState.Completed)
                return session.ObjectId == objectId && session.Media == media
                    ? AlreadyApplied(session)
                    : Failed(session, "A different completion has already been recorded.");
            if (session.State != UploadSessionState.Preparing)
                return Failed(session, "Preparation must be claimed before completion.");
            return Applied(session with
            {
                State = UploadSessionState.Completed,
                ObjectId = objectId,
                Media = media
            });
        });
    }

    public Task<UploadTransitionResult> MarkPublicationPendingAsync(string token, CancellationToken ct) =>
        TransitionAsync(token, ct, session =>
        {
            if (session.State != UploadSessionState.Completed)
                return Failed(session, "Verified completion is required before publication.");
            if (session.PublicationStatus == PublicationStatus.Pending)
                return InProgress(session);
            if (session.PublicationStatus == PublicationStatus.Succeeded)
                return Failed(session, "Publication has already succeeded.");
            return Applied(session with { PublicationStatus = PublicationStatus.Pending });
        });

    public Task<UploadTransitionResult> MarkPublicationSucceededAsync(string token, CancellationToken ct) =>
        TransitionAsync(token, ct, session =>
        {
            if (session.State != UploadSessionState.Completed)
                return Failed(session, "Verified completion is required before publication.");
            if (session.PublicationStatus == PublicationStatus.Succeeded)
                return AlreadyApplied(session);
            if (session.PublicationStatus != PublicationStatus.Pending)
                return Failed(session, "Publication must be pending before it can succeed.");
            return Applied(session with { PublicationStatus = PublicationStatus.Succeeded });
        });

    public Task<UploadTransitionResult> MarkPublicationFailedAsync(string token, CancellationToken ct) =>
        TransitionAsync(token, ct, session =>
        {
            if (session.State != UploadSessionState.Completed)
                return Failed(session, "Verified completion is required before publication.");
            if (session.PublicationStatus == PublicationStatus.Failed)
                return AlreadyApplied(session);
            if (session.PublicationStatus != PublicationStatus.Pending)
                return Failed(session, "Publication must be pending before it can fail.");
            return Applied(session with { PublicationStatus = PublicationStatus.Failed });
        });

    private Task<UploadTransitionResult> TransitionAsync(
        string token,
        CancellationToken ct,
        Func<UploadSession, UploadTransitionResult> transition)
    {
        ct.ThrowIfCancellationRequested();
        if (!IsRouteSafeToken(token))
            return Task.FromResult(new UploadTransitionResult(false, false, null, "Session was not found."));
        var hash = HashToken(token);
        if (!cache.TryGetValue<Entry>(CacheKey(hash), out var entry) || entry is null)
            return Task.FromResult(new UploadTransitionResult(false, false, null, "Session was not found."));

        lock (entry.Gate)
        {
            if (entry.Session.ExpiresAt <= _timeProvider.GetUtcNow())
            {
                cache.Remove(CacheKey(hash));
                return Task.FromResult(new UploadTransitionResult(false, false, null, "Session has expired."));
            }
            var result = transition(entry.Session);
            if (result.Succeeded && !result.WasAlreadyApplied)
            {
                entry.Session = result.Session!;
                cache.Set(CacheKey(hash), entry, new MemoryCacheEntryOptions
                {
                    AbsoluteExpiration = entry.Session.ExpiresAt
                });
            }
            return Task.FromResult(result with { Session = result.Succeeded ? entry.Session : result.Session });
        }
    }

    private static UploadTransitionResult Applied(UploadSession session) => new(true, false, session);
    private static UploadTransitionResult AlreadyApplied(UploadSession session) => new(true, true, session);
    private static UploadTransitionResult InProgress(UploadSession session) => new(false, false, session, IsInProgress: true);
    private static UploadTransitionResult Failed(UploadSession session, string reason) => new(false, false, session, reason);
    private static string CacheKey(string hash) => CacheKeyPrefix + hash;
    private static string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    private static bool IsRouteSafeToken(string? token) =>
        token is { Length: 43 } && token.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
    private static string ToBase64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private sealed class Entry(UploadSession session)
    {
        public object Gate { get; } = new();
        public UploadSession Session { get; set; } = session;
    }
}
