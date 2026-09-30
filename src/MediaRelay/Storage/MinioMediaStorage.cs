using System.Text.Json;
using MediaRelay.Options;
using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel;
using Minio.DataModel.Args;
using Minio.Exceptions;

namespace MediaRelay.Storage;

public sealed class MinioMediaStorage : IMediaStorage
{
    private readonly IMinioClient _client;
    private readonly IMinioClient _publicClient;
    private readonly MinioOptions _minioOptions;
    private readonly Uri _mediaBaseUri;
    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private bool _initialized;

    public MinioMediaStorage(IOptions<MinioOptions> minioOptions, IOptions<PublicUrlOptions> publicUrlOptions)
    {
        _minioOptions = minioOptions.Value;
        _mediaBaseUri = new Uri(publicUrlOptions.Value.MediaBaseUrl.TrimEnd('/') + "/", UriKind.Absolute);
        _client = CreateClient(_minioOptions.Endpoint, _minioOptions.UseSsl, _minioOptions);
        _publicClient = CreateClient(_minioOptions.PublicEndpoint, _minioOptions.PublicUseSsl, _minioOptions);
    }

    public async Task InitializeAsync(CancellationToken ct)
    {
        if (_initialized) return;
        await _initializationLock.WaitAsync(ct);
        try
        {
            if (_initialized) return;
            await InitializeCoreAsync(ct);
            _initialized = true;
        }
        finally { _initializationLock.Release(); }
    }

    private async Task InitializeCoreAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!await _client.BucketExistsAsync(new BucketExistsArgs().WithBucket(_minioOptions.Bucket), ct))
        {
            await _client.MakeBucketAsync(new MakeBucketArgs().WithBucket(_minioOptions.Bucket), ct);
        }

        var policy = JsonSerializer.Serialize(new
        {
            Version = "2012-10-17",
            Statement = new[]
            {
                new
                {
                    Effect = "Allow",
                    Principal = new { AWS = new[] { "*" } },
                    Action = new[] { "s3:GetObject" },
                    Resource = new[] { $"arn:aws:s3:::{_minioOptions.Bucket}/*" }
                }
            }
        });
        await _client.SetPolicyAsync(new SetPolicyArgs().WithBucket(_minioOptions.Bucket).WithPolicy(policy), ct);
    }

    public async Task<PresignedUpload> CreateBrowserUploadAsync(string objectId, string contentType, long maxSize, TimeSpan ttl, CancellationToken ct)
    {
        await InitializeAsync(ct);
        ArgumentException.ThrowIfNullOrWhiteSpace(objectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxSize);
        if (ttl <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(ttl));
        ct.ThrowIfCancellationRequested();

        var policy = new PostPolicy();
        policy.SetBucket(_minioOptions.Bucket);
        policy.SetKey(objectId);
        policy.SetContentType(contentType);
        policy.SetContentRange(1, maxSize);
        policy.SetExpires(DateTime.UtcNow.Add(ttl));

        var (url, fields) = await _publicClient.PresignedPostPolicyAsync(policy);
        ct.ThrowIfCancellationRequested();
        var uploadFields = new Dictionary<string, string>(fields, StringComparer.OrdinalIgnoreCase)
        {
            ["Content-Type"] = contentType
        };
        return new PresignedUpload(url.AbsoluteUri, uploadFields);
    }

    public async Task UploadAsync(string objectId, Stream content, string contentType, CancellationToken ct)
    {
        await InitializeAsync(ct);
        ArgumentException.ThrowIfNullOrWhiteSpace(objectId);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);
        ct.ThrowIfCancellationRequested();

        var size = content.CanSeek ? content.Length - content.Position : -1;
        await _client.PutObjectAsync(new PutObjectArgs()
            .WithBucket(_minioOptions.Bucket)
            .WithObject(objectId)
            .WithStreamData(content)
            .WithObjectSize(size)
            .WithContentType(contentType), ct);
    }

    public async Task<StoredObjectInfo?> StatAsync(string objectId, CancellationToken ct)
    {
        await InitializeAsync(ct);
        ArgumentException.ThrowIfNullOrWhiteSpace(objectId);
        try
        {
            var stat = await _client.StatObjectAsync(new StatObjectArgs()
                .WithBucket(_minioOptions.Bucket)
                .WithObject(objectId), ct);
            return new StoredObjectInfo(objectId, stat.Size, stat.ContentType);
        }
        catch (ObjectNotFoundException)
        {
            return null;
        }
        catch (ErrorResponseException exception) when (exception.Response?.Code is "NoSuchKey" or "NoSuchObject")
        {
            return null;
        }
    }

    public string GetPublicUrl(string objectId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectId);
        return new Uri(_mediaBaseUri, Uri.EscapeDataString(objectId)).AbsoluteUri;
    }

    private static IMinioClient CreateClient(string endpoint, bool useSsl, MinioOptions options)
    {
        var builder = new MinioClient().WithEndpoint(endpoint).WithCredentials(options.AccessKey, options.SecretKey);
        if (useSsl)
        {
            builder = builder.WithSSL();
        }

        return builder.Build();
    }
}
