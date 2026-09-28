using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using MediaRelay.Options;
using MediaRelay.Storage;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace MediaRelay.Tests.ShareX;

public sealed class ShareXEndpointTests
{
    private const string ApiKey = "sharex-test-secret-value";
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x01, 0x02, 0x03, 0x04];

    [Fact]
    public async Task UploadRejectsMissingAndIncorrectBearerKeys()
    {
        await using var factory = new ShareXFactory();
        using var client = factory.CreateClient();

        using var missing = await client.PostAsync("/api/sharex/upload", CreateForm("image.png", "image/png", Png));
        Assert.Equal(HttpStatusCode.Unauthorized, missing.StatusCode);
        using var wrongRequest = CreateRequest("wrong-key", "image.png", "image/png", Png);
        using var wrong = await client.SendAsync(wrongRequest);
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.DoesNotContain(ApiKey, await wrong.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.DoesNotContain(ApiKey, factory.Logs.ToString(), StringComparison.Ordinal);
        Assert.Equal(0, factory.Storage.UploadCalls);
    }

    [Theory]
    [InlineData("image.txt", "image/png")]
    [InlineData("image.png", "text/plain")]
    public async Task UploadRejectsUnsupportedMediaMetadata(string fileName, string contentType)
    {
        await using var factory = new ShareXFactory();
        using var client = factory.CreateClient();
        using var request = CreateRequest(ApiKey, fileName, contentType, Png);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Equal(0, factory.Storage.UploadCalls);
    }

    [Fact]
    public async Task UploadRejectsInvalidSignature()
    {
        await using var factory = new ShareXFactory();
        using var client = factory.CreateClient();
        using var request = CreateRequest(ApiKey, "image.png", "image/png", Encoding.ASCII.GetBytes("not a png"));

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Equal(0, factory.Storage.UploadCalls);
    }

    [Fact]
    public async Task UploadRejectsOversizedStreamWithoutCallingStorage()
    {
        await using var factory = new ShareXFactory();
        using var client = factory.CreateClient();
        var payload = new byte[101];
        Png.CopyTo(payload, 0);
        using var request = CreateRequest(ApiKey, "image.png", "image/png", payload);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal(0, factory.Storage.UploadCalls);
    }

    [Fact]
    public async Task UploadStreamsValidatedFileAndReturnsShareXUrlContractWithoutSecrets()
    {
        await using var factory = new ShareXFactory();
        using var client = factory.CreateClient();
        using var request = CreateRequest(ApiKey, "capture.png", "image/png", Png);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadFromJsonAsync<ShareXResponse>();
        Assert.NotNull(body);
        Assert.Equal(factory.Storage.GetPublicUrl(factory.Storage.LastObjectId!), body.Url);
        Assert.StartsWith("https://media.test/", body.Url, StringComparison.Ordinal);
        Assert.EndsWith(".png", factory.Storage.LastObjectId, StringComparison.Ordinal);
        Assert.Equal("image/png", factory.Storage.LastContentType);
        Assert.Equal(Png, factory.Storage.LastBytes);
        Assert.Equal(Png.Length, factory.Storage.LastBytes!.Length);
        Assert.DoesNotContain(ApiKey, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.DoesNotContain(ApiKey, factory.Logs.ToString(), StringComparison.Ordinal);
    }

    private static MultipartFormDataContent CreateForm(string fileName, string contentType, byte[] bytes)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(file, "file", fileName);
        return form;
    }

    private static HttpRequestMessage CreateRequest(string key, string fileName, string contentType, byte[] bytes)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/sharex/upload") { Content = CreateForm(fileName, contentType, bytes) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        return request;
    }

    private sealed record ShareXResponse(string Url);

    private sealed class ShareXFactory : WebApplicationFactory<Program>
    {
        public FakeStorage Storage { get; } = new();
        public CapturingLoggerProvider Logs { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureLogging(logging => logging.AddProvider(Logs));
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Discord:Token"] = "test", ["Discord:ApplicationId"] = "123", ["Discord:Enabled"] = "false",
                ["Minio:Endpoint"] = "localhost:9000", ["Minio:PublicEndpoint"] = "localhost:9000",
                ["Minio:AccessKey"] = "test", ["Minio:SecretKey"] = "test",
                ["PublicUrls:MediaBaseUrl"] = "https://media.test", ["PublicUrls:AppBaseUrl"] = "https://app.test",
                ["Upload:ApiKey"] = ApiKey, ["Upload:MaxUploadSize"] = "100"
            }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IMediaStorage>();
                services.AddSingleton<IMediaStorage>(Storage);
            });
        }
    }

    private sealed class FakeStorage : IMediaStorage
    {
        public int UploadCalls { get; private set; }
        public string? LastObjectId { get; private set; }
        public string? LastContentType { get; private set; }
        public byte[]? LastBytes { get; private set; }

        public Task<PresignedUpload> CreateBrowserUploadAsync(string objectId, string contentType, long maxSize, TimeSpan ttl, CancellationToken ct) => throw new NotSupportedException();
        public async Task UploadAsync(string objectId, Stream content, string contentType, CancellationToken ct)
        {
            UploadCalls++;
            LastObjectId = objectId;
            LastContentType = contentType;
            using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, ct);
            LastBytes = buffer.ToArray();
        }
        public Task<StoredObjectInfo?> StatAsync(string objectId, CancellationToken ct) => throw new NotSupportedException();
        public string GetPublicUrl(string objectId) => $"https://media.test/{objectId}";
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly StringBuilder _messages = new();
        public override string ToString() => _messages.ToString();
        public ILogger CreateLogger(string categoryName) => new CapturingLogger(_messages);
        public void Dispose() { }

        private sealed class CapturingLogger(StringBuilder messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                lock (messages) messages.AppendLine(formatter(state, exception));
            }
        }
    }
}
