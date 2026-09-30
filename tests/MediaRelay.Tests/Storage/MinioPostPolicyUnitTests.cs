using System.Text;
using System.Text.Json;
using MediaRelay.Options;
using MediaRelay.Storage;

namespace MediaRelay.Tests.Storage;

public sealed class MinioPostPolicyUnitTests
{
    [Fact]
    public async Task BrowserPolicyUsesPublicEndpointAndConstrainsExactObjectSizeTypeAndExpiry()
    {
        var storage = new MinioMediaStorage(
            Microsoft.Extensions.Options.Options.Create(new MinioOptions
            {
                Endpoint = "internal.minio:9000",
                UseSsl = false,
                PublicEndpoint = "s3.example.test",
                PublicUseSsl = true,
                AccessKey = "test-access",
                SecretKey = "test-secret",
                Bucket = "test-media"
            }),
            Microsoft.Extensions.Options.Options.Create(new PublicUrlOptions { MediaBaseUrl = "https://cdn.example.test", AppBaseUrl = "https://app.example.test" }));

        var upload = await storage.CreateBrowserUploadAsync("opaque-id.png", "image/png", 1024, TimeSpan.FromMinutes(3), CancellationToken.None);
        using var policy = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(upload.Fields["policy"])));
        Assert.StartsWith("https://s3.example.test/", upload.Url, StringComparison.OrdinalIgnoreCase);
        Assert.False(upload.Url.StartsWith("http://s3.example.test/", StringComparison.OrdinalIgnoreCase));
        var conditions = policy.RootElement.GetProperty("conditions");

        Assert.Equal("test-media", conditions.EnumerateArray().First(condition => condition.ValueKind == JsonValueKind.Array && condition[0].GetString() == "eq" && condition[1].GetString() == "$bucket")[2].GetString());
        Assert.Contains(conditions.EnumerateArray(), condition => condition.ValueKind == JsonValueKind.Array && condition[0].GetString() == "content-length-range" && condition[1].GetString() == "1" && condition[2].GetString() == "1024");
        Assert.Contains(conditions.EnumerateArray(), condition => condition.ValueKind == JsonValueKind.Array && condition[0].GetString() == "eq" && condition[1].GetString() == "$key" && condition[2].GetString() == "opaque-id.png");
        Assert.Contains(conditions.EnumerateArray(), condition => condition.ValueKind == JsonValueKind.Array && condition[0].GetString() == "eq" && condition[1].GetString() == "$Content-Type" && condition[2].GetString() == "image/png");
        Assert.InRange(policy.RootElement.GetProperty("expiration").GetDateTimeOffset(), DateTimeOffset.UtcNow.AddMinutes(2), DateTimeOffset.UtcNow.AddMinutes(4));
    }
}



