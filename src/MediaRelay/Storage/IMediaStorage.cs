namespace MediaRelay.Storage;

public interface IMediaStorage
{
    Task<PresignedUpload> CreateBrowserUploadAsync(string objectId, string contentType, long maxSize, TimeSpan ttl, CancellationToken ct);
    Task UploadAsync(string objectId, Stream content, string contentType, CancellationToken ct);
    Task<StoredObjectInfo?> StatAsync(string objectId, CancellationToken ct);
    string GetPublicUrl(string objectId);
}

public sealed record PresignedUpload(string Url, IReadOnlyDictionary<string, string> Fields);

public sealed record StoredObjectInfo(string ObjectId, long Size, string ContentType);
