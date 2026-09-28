using System.Net;
using System.Net.Http.Json;
using MediaRelay.Storage;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MediaRelay.Tests.Health;

public sealed class HealthEndpointTests
{
    [Fact]
    public async Task HealthReturnsGenericHealthyResponseWithoutCredentials()
    {
        await using var factory = new HealthFactory();
        using var client = factory.CreateClient();
        var response = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("{\"status\":\"healthy\"}", await response.Content.ReadAsStringAsync());
    }

    private sealed class HealthFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Discord:Token"] = "test", ["Discord:ApplicationId"] = "123", ["Discord:Enabled"] = "false",
                ["Minio:Endpoint"] = "localhost:9000", ["Minio:PublicEndpoint"] = "localhost:9000",
                ["Minio:AccessKey"] = "test", ["Minio:SecretKey"] = "test",
                ["PublicUrls:MediaBaseUrl"] = "https://media.test", ["PublicUrls:AppBaseUrl"] = "https://app.test",
                ["Upload:ApiKey"] = "test"
            }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IMediaStorage>();
                services.AddSingleton<IMediaStorage>(new UnusedStorage());
            });
        }
    }

    private sealed class UnusedStorage : IMediaStorage
    {
        public Task<PresignedUpload> CreateBrowserUploadAsync(string objectId, string contentType, long maxSize, TimeSpan ttl, CancellationToken ct) => throw new NotSupportedException();
        public Task UploadAsync(string objectId, Stream content, string contentType, CancellationToken ct) => throw new NotSupportedException();
        public Task<StoredObjectInfo?> StatAsync(string objectId, CancellationToken ct) => throw new NotSupportedException();
        public string GetPublicUrl(string objectId) => throw new NotSupportedException();
    }
}
