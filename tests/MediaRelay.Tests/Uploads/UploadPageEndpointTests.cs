using System.Net;
using MediaRelay.Discord;
using MediaRelay.Options;
using MediaRelay.Storage;
using MediaRelay.Uploads;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MediaRelay.Tests.Uploads;

public sealed class UploadPageEndpointTests
{
    [Fact]
    public async Task PageIsAvailableOnlyForLiveSession()
    {
        await using var factory = new PageFactory();
        using var client = factory.CreateClient();
        var session = await factory.Services.GetRequiredService<UploadSessionService>()
            .CreateAsync(1, 2, 3, CancellationToken.None);

        var page = await client.GetAsync($"/u/{session.Token}");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Equal("text/html", page.Content.Headers.ContentType?.MediaType);
        Assert.DoesNotContain(session.Token, await page.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/u/invalid-token")).StatusCode);
        factory.Clock.Advance(TimeSpan.FromMinutes(2));
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/u/{session.Token}")).StatusCode);
    }

    [Fact]
    public async Task StaticAssetsAreAvailableAndContainNoServerSecrets()
    {
        await using var factory = new PageFactory();
        using var client = factory.CreateClient();
        var html = await client.GetStringAsync("/upload.html");
        var javascript = await client.GetStringAsync("/upload.js");
        var css = await client.GetStringAsync("/upload.css");

        Assert.Contains("/api/uploads/prepare", javascript);
        Assert.Contains("/api/uploads/complete", javascript);
        Assert.Contains("xhr.open(\"POST\", authorization.url)", javascript);
        Assert.Contains("data.append(\"file\", file", javascript);
        Assert.Contains("if (!selected(file)) return;", javascript);
        Assert.Contains("droppedFile = null;\n    if (!selected(file)) return;", javascript);
        Assert.Contains("progress", javascript, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("upload.css", html);
        Assert.Contains("upload.js", html);
        foreach (var asset in new[] { html, javascript })
        {
            Assert.DoesNotContain("test-discord-token", asset);
            Assert.DoesNotContain("test-minio-secret", asset);
            Assert.DoesNotContain("test-upload-api-key", asset);
        }
    }

    private sealed class PageFactory : WebApplicationFactory<Program>
    {
        public TestClock Clock { get; } = new(DateTimeOffset.UtcNow);

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Discord:Token"] = "test-discord-token", ["Discord:ApplicationId"] = "123", ["Discord:Enabled"] = "false",
                ["Minio:Endpoint"] = "localhost:9000", ["Minio:PublicEndpoint"] = "localhost:9000",
                ["Minio:AccessKey"] = "test", ["Minio:SecretKey"] = "test-minio-secret",
                ["PublicUrls:MediaBaseUrl"] = "https://media.test", ["PublicUrls:AppBaseUrl"] = "https://app.test",
                ["Upload:ApiKey"] = "test-upload-api-key", ["Upload:MaxUploadSize"] = "10485760", ["Upload:SessionTtlMinutes"] = "1"
            }));
            builder.ConfigureTestServices(services => services.AddSingleton<TimeProvider>(Clock));
        }
    }

    private sealed class TestClock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan amount) => _now += amount;
    }
}
