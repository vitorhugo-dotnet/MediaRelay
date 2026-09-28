using MediaRelay.Options;
using MediaRelay.Uploads;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace MediaRelay.Tests.Uploads;

public sealed class UploadSessionServiceTests
{
    [Fact]
    public async Task CreateReturnsRouteSafeRawTokenOnceAndStoresOnlyItsHash()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = CreateService(cache);

        var created = await service.CreateAsync(1, 2, 3, CancellationToken.None);
        var found = await service.FindAsync(created.Token, CancellationToken.None);

        Assert.Matches("^[A-Za-z0-9_-]{43}$", created.Token);
        Assert.Equal(1UL, created.Session.GuildId);
        Assert.Equal(2UL, created.Session.ChannelId);
        Assert.Equal(3UL, created.Session.UserId);
        Assert.NotNull(found);
        Assert.NotEqual(created.Token, found.TokenHash);
        Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(created.Token))), found.TokenHash);
        Assert.InRange(created.Session.ExpiresAt - DateTimeOffset.UtcNow, TimeSpan.FromMinutes(30) - TimeSpan.FromSeconds(2), TimeSpan.FromMinutes(30) + TimeSpan.FromSeconds(2));
        Assert.Null(await service.FindAsync("../" + created.Token, CancellationToken.None));
    }

    [Fact]
    public async Task PreparationCanOnlyBeClaimedOnceAndCompletionIsIdempotent()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = CreateService(cache);
        var created = await service.CreateAsync(1, 2, 3, CancellationToken.None);

        var claims = await Task.WhenAll(Enumerable.Range(0, 16)
            .Select(_ => service.TryClaimPreparationAsync(created.Token, CancellationToken.None)));
        Assert.Single(claims, result => result.Succeeded);

        var media = new ValidatedMedia(".png", "image/png");
        var first = await service.RecordVerifiedCompletionAsync(created.Token, "object-id", media, CancellationToken.None);
        var repeated = await service.RecordVerifiedCompletionAsync(created.Token, "object-id", media, CancellationToken.None);

        Assert.True(first.Succeeded);
        Assert.True(repeated.Succeeded);
        Assert.True(repeated.WasAlreadyApplied);
        Assert.Equal("object-id", repeated.Session!.ObjectId);
        Assert.Equal(media, repeated.Session.Media);
    }

    [Fact]
    public async Task FindRejectsExpiredSession()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var clock = new TestTimeProvider(DateTimeOffset.UtcNow);
        var service = new UploadSessionService(cache, Microsoft.Extensions.Options.Options.Create(new UploadOptions()), clock);
        var created = await service.CreateAsync(1, 2, 3, CancellationToken.None);

        clock.Advance(TimeSpan.FromMinutes(31));

        Assert.Null(await service.FindAsync(created.Token, CancellationToken.None));
        Assert.False((await service.TryClaimPreparationAsync(created.Token, CancellationToken.None)).Succeeded);
    }

    [Fact]
    public async Task PublicationSuccessIsIdempotentAndConflictingCompletionFails()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = CreateService(cache);
        var created = await service.CreateAsync(1, 2, 3, CancellationToken.None);

        Assert.False((await service.RecordVerifiedCompletionAsync(created.Token, "x", new(".png", "image/png"), CancellationToken.None)).Succeeded);
        Assert.False((await service.MarkPublicationSucceededAsync(created.Token, CancellationToken.None)).Succeeded);
        Assert.True((await service.TryClaimPreparationAsync(created.Token, CancellationToken.None)).Succeeded);
        Assert.True((await service.RecordVerifiedCompletionAsync(created.Token, "x", new(".png", "image/png"), CancellationToken.None)).Succeeded);
        Assert.False((await service.RecordVerifiedCompletionAsync(created.Token, "y", new(".jpg", "image/jpeg"), CancellationToken.None)).Succeeded);
        Assert.True((await service.MarkPublicationPendingAsync(created.Token, CancellationToken.None)).Succeeded);
        Assert.True((await service.MarkPublicationSucceededAsync(created.Token, CancellationToken.None)).Succeeded);
        var retry = await service.MarkPublicationSucceededAsync(created.Token, CancellationToken.None);
        Assert.True(retry.Succeeded);
        Assert.True(retry.WasAlreadyApplied);
        Assert.False((await service.MarkPublicationFailedAsync(created.Token, CancellationToken.None)).Succeeded);
    }

    [Fact]
    public async Task FailedPublicationCanBeRetriedUsingTheRecordedCompletion()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = CreateService(cache);
        var created = await service.CreateAsync(1, 2, 3, CancellationToken.None);
        await service.TryClaimPreparationAsync(created.Token, CancellationToken.None);
        await service.RecordVerifiedCompletionAsync(created.Token, "object-id", new(".png", "image/png"), CancellationToken.None);
        await service.MarkPublicationPendingAsync(created.Token, CancellationToken.None);

        Assert.True((await service.MarkPublicationFailedAsync(created.Token, CancellationToken.None)).Succeeded);
        var retry = await service.MarkPublicationPendingAsync(created.Token, CancellationToken.None);
        Assert.True(retry.Succeeded);
        Assert.Equal("object-id", retry.Session!.ObjectId);
        Assert.Equal(new ValidatedMedia(".png", "image/png"), retry.Session.Media);
        Assert.True((await service.MarkPublicationSucceededAsync(created.Token, CancellationToken.None)).Succeeded);
    }

    [Fact]
    public async Task PublicationPendingCanOnlyBeClaimedByOneConcurrentCaller()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = CreateService(cache);
        var created = await service.CreateAsync(1, 2, 3, CancellationToken.None);
        await service.TryClaimPreparationAsync(created.Token, CancellationToken.None);
        await service.RecordVerifiedCompletionAsync(created.Token, "object-id", new(".png", "image/png"), CancellationToken.None);

        var claims = await Task.WhenAll(Enumerable.Range(0, 16)
            .Select(_ => service.MarkPublicationPendingAsync(created.Token, CancellationToken.None)));

        Assert.Single(claims, result => result.Succeeded);
        Assert.Equal(15, claims.Count(result => result.IsInProgress && !result.Succeeded));
    }

    private static UploadSessionService CreateService(IMemoryCache cache) =>
        new(cache, Microsoft.Extensions.Options.Options.Create(new UploadOptions()));

    private sealed class TestTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan amount) => _now += amount;
    }
}
