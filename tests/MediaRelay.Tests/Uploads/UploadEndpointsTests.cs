using System.Net;
using System.Net.Http.Json;
using MediaRelay.Options;
using MediaRelay.Discord;
using MediaRelay.Storage;
using MediaRelay.Uploads;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MediaRelay.Tests.Uploads;

public sealed class UploadEndpointsTests
{
    [Fact]
    public async Task PrepareRejectsInvalidTokenUnsupportedMediaAndOversizedFile()
    {
        await using var factory = new UploadFactory();
        using var client = factory.CreateClient();

        var invalid = await client.PostAsJsonAsync("/api/uploads/prepare", new PrepareUploadRequest("bad", "a.png", "image/png", 10));
        Assert.Equal(HttpStatusCode.Unauthorized, invalid.StatusCode);
        var missing = await client.PostAsJsonAsync("/api/uploads/prepare", new PrepareUploadRequest("", "a.png", "image/png", 10));
        Assert.Equal(HttpStatusCode.Unauthorized, missing.StatusCode);

        var created = await CreateSession(factory);
        var unsupported = await client.PostAsJsonAsync("/api/uploads/prepare", new PrepareUploadRequest(created.Token, "a.zip", "application/zip", 10));
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, unsupported.StatusCode);

        var oversized = await client.PostAsJsonAsync("/api/uploads/prepare", new PrepareUploadRequest(created.Token, "a.png", "image/png", 200));
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, oversized.StatusCode);
    }

    [Fact]
    public async Task PrepareReturnsPresignedAuthorizationAndStorageFailureIsUnavailable()
    {
        await using var factory = new UploadFactory();
        using var client = factory.CreateClient();
        var session = await CreateSession(factory);
        var response = await client.PostAsJsonAsync("/api/uploads/prepare", new PrepareUploadRequest(session.Token, "a.png", "image/png", 10));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PrepareUploadResponse>();
        Assert.NotNull(body);
        Assert.EndsWith(".png", body.ObjectId);
        Assert.Equal("https://storage.test/post", body.Url);
        Assert.Equal("signed", body.Fields["policy"]);

        var failingSession = await CreateSession(factory);
        factory.Storage.FailPrepare = true;
        var failed = await client.PostAsJsonAsync("/api/uploads/prepare", new PrepareUploadRequest(failingSession.Token, "a.png", "image/png", 10));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
    }

    [Fact]
    public async Task PrepareCanRetrySameSessionAfterPresignFailure()
    {
        await using var factory = new UploadFactory();
        using var client = factory.CreateClient();
        var session = await CreateSession(factory);
        factory.Storage.FailPrepare = true;
        var failed = await client.PostAsJsonAsync("/api/uploads/prepare", new PrepareUploadRequest(session.Token, "a.png", "image/png", 10));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);

        factory.Storage.FailPrepare = false;
        var retry = await client.PostAsJsonAsync("/api/uploads/prepare", new PrepareUploadRequest(session.Token, "a.png", "image/png", 10));
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
    }

    [Fact]
    public async Task PrepareRejectsExpiredSessionThroughRoute()
    {
        await using var factory = new UploadFactory();
        using var client = factory.CreateClient();
        var session = await CreateSession(factory);
        factory.Clock.Advance(TimeSpan.FromMinutes(2));

        var response = await client.PostAsJsonAsync("/api/uploads/prepare", new PrepareUploadRequest(session.Token, "a.png", "image/png", 10));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CompleteVerifiesObjectAndDuplicateReturnsSameCompletedState()
    {
        await using var factory = new UploadFactory();
        using var client = factory.CreateClient();
        var session = await CreateSession(factory);
        await client.PostAsJsonAsync("/api/uploads/prepare", new PrepareUploadRequest(session.Token, "a.png", "image/png", 10));

        factory.Storage.Stored = null;
        var missing = await client.PostAsJsonAsync("/api/uploads/complete", new CompleteUploadRequest(session.Token));
        Assert.Equal(HttpStatusCode.Conflict, missing.StatusCode);

        factory.Storage.Stored = new StoredObjectInfo(factory.Storage.LastObjectId!, 10, "image/jpeg");
        var mismatch = await client.PostAsJsonAsync("/api/uploads/complete", new CompleteUploadRequest(session.Token));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, mismatch.StatusCode);

        factory.Storage.Stored = new StoredObjectInfo(factory.Storage.LastObjectId!, 10, "image/png");
        var completed = await client.PostAsJsonAsync("/api/uploads/complete", new CompleteUploadRequest(session.Token));
        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
        var first = await completed.Content.ReadFromJsonAsync<CompleteUploadResponse>();
        Assert.NotNull(first);
        Assert.Equal("succeeded", first.PublicationState);

        var duplicate = await client.PostAsJsonAsync("/api/uploads/complete", new CompleteUploadRequest(session.Token));
        var second = await duplicate.Content.ReadFromJsonAsync<CompleteUploadResponse>();
        Assert.Equal(HttpStatusCode.OK, duplicate.StatusCode);
        Assert.Equal(first, second);
        Assert.Equal(3, factory.Storage.StatCalls);
    }

    [Fact]
    public async Task CompletePublishesOnceRetriesFailureAndDoesNotDeleteMedia()
    {
        await using var factory = new UploadFactory();
        using var client = factory.CreateClient();
        var session = await CreateSession(factory);
        await client.PostAsJsonAsync("/api/uploads/prepare", new PrepareUploadRequest(session.Token, "a.png", "image/png", 10));
        factory.Storage.Stored = new StoredObjectInfo(factory.Storage.LastObjectId!, 10, "image/png");
        factory.Publisher.Fail = true;

        var failed = await client.PostAsJsonAsync("/api/uploads/complete", new CompleteUploadRequest(session.Token));
        Assert.Equal(HttpStatusCode.OK, failed.StatusCode);
        Assert.Equal("failed", (await failed.Content.ReadFromJsonAsync<CompleteUploadResponse>())!.PublicationState);
        Assert.NotNull(factory.Storage.Stored);
        Assert.Equal(1, factory.Publisher.Calls);

        factory.Publisher.Fail = false;
        var retried = await client.PostAsJsonAsync("/api/uploads/complete", new CompleteUploadRequest(session.Token));
        Assert.Equal("succeeded", (await retried.Content.ReadFromJsonAsync<CompleteUploadResponse>())!.PublicationState);
        var duplicate = await client.PostAsJsonAsync("/api/uploads/complete", new CompleteUploadRequest(session.Token));
        Assert.Equal("succeeded", (await duplicate.Content.ReadFromJsonAsync<CompleteUploadResponse>())!.PublicationState);
        Assert.Equal(2, factory.Publisher.Calls);
        Assert.Equal((1UL, 2UL, $"https://media.test/{factory.Storage.LastObjectId}"), factory.Publisher.LastCall);
    }

    private static Task<CreatedUploadSession> CreateSession(UploadFactory factory) =>
        factory.Services.GetRequiredService<UploadSessionService>().CreateAsync(1, 2, 3, CancellationToken.None);

    private sealed class UploadFactory : WebApplicationFactory<Program>
    {
        public FakeMediaStorage Storage { get; } = new();
        public TestTimeProvider Clock { get; } = new(DateTimeOffset.UtcNow);

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Discord:Token"] = "test", ["Discord:ApplicationId"] = "123",
                ["Discord:Enabled"] = "false",
                ["Minio:Endpoint"] = "localhost:9000", ["Minio:PublicEndpoint"] = "localhost:9000",
                ["Minio:AccessKey"] = "test", ["Minio:SecretKey"] = "test",
                ["PublicUrls:MediaBaseUrl"] = "https://media.test", ["PublicUrls:AppBaseUrl"] = "https://app.test",
                ["Upload:ApiKey"] = "test", ["Upload:MaxUploadSize"] = "100", ["Upload:SessionTtlMinutes"] = "1"
            }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IMediaStorage>();
                services.AddSingleton<IMediaStorage>(Storage);
                services.RemoveAll<IMediaPublisher>();
                services.AddSingleton<IMediaPublisher>(Publisher);
                services.AddSingleton<TimeProvider>(Clock);
            });
        }
        public FakeMediaPublisher Publisher { get; } = new();
    }

    private sealed class FakeMediaPublisher : IMediaPublisher
    {
        public bool Fail { get; set; }
        public int Calls { get; private set; }
        public (ulong GuildId, ulong ChannelId, string Url)? LastCall { get; private set; }
        public Task<bool> PublishAsync(ulong guildId, ulong channelId, string publicUrl, CancellationToken ct)
        {
            Calls++;
            LastCall = (guildId, channelId, publicUrl);
            return Task.FromResult(!Fail);
        }
    }

    private sealed class TestTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan amount) => _now += amount;
    }

    private sealed class FakeMediaStorage : IMediaStorage
    {
        public bool FailPrepare { get; set; }
        public StoredObjectInfo? Stored { get; set; }
        public string? LastObjectId { get; private set; }
        public int StatCalls { get; private set; }

        public Task<PresignedUpload> CreateBrowserUploadAsync(string objectId, string contentType, long maxSize, TimeSpan ttl, CancellationToken ct)
        {
            LastObjectId = objectId;
            if (FailPrepare) throw new IOException("storage offline");
            return Task.FromResult(new PresignedUpload("https://storage.test/post", new Dictionary<string, string> { ["policy"] = "signed" }));
        }
        public Task UploadAsync(string objectId, Stream content, string contentType, CancellationToken ct) => Task.CompletedTask;
        public Task<StoredObjectInfo?> StatAsync(string objectId, CancellationToken ct)
        {
            StatCalls++;
            return Task.FromResult(Stored);
        }
        public string GetPublicUrl(string objectId) => $"https://media.test/{objectId}";
    }
}
