using System.Text;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using MediaRelay.Options;
using MediaRelay.Storage;
using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel.Args;

namespace MediaRelay.Tests.Storage;

public sealed class MinioMediaStorageTests : IAsyncLifetime
{
    private const string AccessKey = "media-relay-test";
    private const string SecretKey = "media-relay-test-secret";
    private readonly IContainer _container = new ContainerBuilder("media-relay-minio:community-9e49d5e")
        .WithEnvironment("MINIO_ROOT_USER", AccessKey)
        .WithEnvironment("MINIO_ROOT_PASSWORD", SecretKey)
        .WithCommand("server", "/data")
        .WithPortBinding(9000, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(request => request.ForPort(9000).ForPath("/minio/health/ready")))
        .Build();

    private MinioMediaStorage _storage = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        var endpoint = $"localhost:{_container.GetMappedPublicPort(9000)}";
        _storage = new MinioMediaStorage(
            Microsoft.Extensions.Options.Options.Create(new MinioOptions { Endpoint = endpoint, PublicEndpoint = endpoint, PublicUseSsl = false, AccessKey = AccessKey, SecretKey = SecretKey, Bucket = "test-media" }),
            Microsoft.Extensions.Options.Options.Create(new PublicUrlOptions { MediaBaseUrl = "https://media.example.test", AppBaseUrl = "https://app.example.test" }));
        await _storage.InitializeAsync(CancellationToken.None);
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();

    [Fact]
    public async Task InitializesBucketWithAnonymousReadOnlyAccess()
    {
        var client = CreateClient($"localhost:{_container.GetMappedPublicPort(9000)}");
        var policy = await client.GetPolicyAsync(new GetPolicyArgs().WithBucket("test-media"));
        using var document = JsonDocument.Parse(policy);
        var statements = document.RootElement.GetProperty("Statement");
        Assert.Contains(statements.EnumerateArray(), statement => statement.GetProperty("Effect").GetString() == "Allow" && statement.GetProperty("Action").EnumerateArray().Any(action => action.GetString() == "s3:GetObject"));
        Assert.DoesNotContain(statements.EnumerateArray(), statement => statement.GetProperty("Effect").GetString() == "Allow" && statement.GetProperty("Action").EnumerateArray().Any(action => action.GetString() is "s3:PutObject" or "s3:ListBucket" or "s3:DeleteObject"));
    }

    [Fact]
    public async Task UploadsAndStatsObjectWithContentTypeAndCanonicalPublicUrl()
    {
        const string objectId = "opaque-id.png";
        var bytes = Encoding.UTF8.GetBytes("small image payload");
        await using var content = new MemoryStream(bytes);
        await _storage.UploadAsync(objectId, content, "image/png", CancellationToken.None);
        var stored = await _storage.StatAsync(objectId, CancellationToken.None);
        Assert.NotNull(stored);
        Assert.Equal(objectId, stored.ObjectId);
        Assert.Equal(bytes.Length, stored.Size);
        Assert.Equal("image/png", stored.ContentType);
        Assert.Equal("https://media.example.test/opaque-id.png", _storage.GetPublicUrl(objectId));
        Assert.DoesNotContain("test-media", _storage.GetPublicUrl(objectId));
        Assert.DoesNotContain("localhost", _storage.GetPublicUrl(objectId));
    }

    [Fact]
    public async Task BrowserUploadPolicyBindsExactObjectTypeSizeAndExpiration()
    {
        var upload = await _storage.CreateBrowserUploadAsync("opaque-id.png", "image/png", 1024, TimeSpan.FromMinutes(3), CancellationToken.None);
        var fields = upload.Fields;
        Assert.NotEmpty(upload.Url);
        Assert.Equal("opaque-id.png", fields["key"]);
        Assert.True(fields.TryGetValue("Content-Type", out var returnedContentType), $"returned fields: {string.Join(", ", fields.Keys)}");
        Assert.Equal("image/png", returnedContentType);
        Assert.Contains("policy", fields.Keys);
        var policyJson = Encoding.UTF8.GetString(Convert.FromBase64String(fields["policy"]));
        using var policy = JsonDocument.Parse(policyJson);
        Assert.Equal("test-media", policy.RootElement.GetProperty("conditions").EnumerateArray().First(condition => condition.ValueKind == JsonValueKind.Array && condition[0].GetString() == "eq" && condition[1].GetString() == "$bucket")[2].GetString());
        var expires = policy.RootElement.GetProperty("expiration").GetDateTimeOffset();
        Assert.InRange(expires, DateTimeOffset.UtcNow.AddMinutes(2), DateTimeOffset.UtcNow.AddMinutes(4));
        var conditions = policy.RootElement.GetProperty("conditions");
        Assert.Contains(conditions.EnumerateArray(), condition => condition.ValueKind == JsonValueKind.Array && condition[0].GetString() == "content-length-range" && condition[1].GetString() == "1" && condition[2].GetString() == "1024");
        Assert.Contains(conditions.EnumerateArray(), condition => condition.ValueKind == JsonValueKind.Array && condition[0].GetString() == "eq" && condition[1].GetString() == "$key" && condition[2].GetString() == "opaque-id.png");
        Assert.Contains(conditions.EnumerateArray(), condition => condition.ValueKind == JsonValueKind.Array && condition[0].GetString() == "eq" && condition[1].GetString() == "$Content-Type" && condition[2].GetString() == "image/png");
        Assert.DoesNotContain("SecretKey", string.Join(" ", fields.Keys), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(SecretKey, string.Join(" ", fields.Values));

        using var form = new MultipartFormDataContent();
        foreach (var field in fields)
            form.Add(new StringContent(field.Value), field.Key);
        using var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes("small browser upload"));
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        form.Add(fileContent, "file", "opaque-id.png");
        using var response = await new HttpClient().PostAsync(upload.Url, form);
        Assert.Equal(System.Net.HttpStatusCode.NoContent, response.StatusCode);
        var uploaded = await _storage.StatAsync("opaque-id.png", CancellationToken.None);
        Assert.NotNull(uploaded);
        Assert.Equal("image/png", uploaded.ContentType);
    }

    [Fact]
    public async Task StatReturnsNullForMissingObject()
    {
        var stored = await _storage.StatAsync("not-present.png", CancellationToken.None);
        Assert.Null(stored);
    }

    private static IMinioClient CreateClient(string endpoint) => new MinioClient().WithEndpoint(endpoint).WithCredentials(AccessKey, SecretKey).Build();
}


