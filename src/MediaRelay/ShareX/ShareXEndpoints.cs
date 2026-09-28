using System.Security.Cryptography;
using MediaRelay.Options;
using MediaRelay.Storage;
using MediaRelay.Uploads;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;
using AuthenticationHeaderValue = System.Net.Http.Headers.AuthenticationHeaderValue;

namespace MediaRelay.ShareX;

public static class ShareXEndpoints
{
    private const long MultipartOverheadLimit = 64 * 1024;
    private const int SignatureLength = 12;
    private const int CopyBufferSize = 80 * 1024;

    public static IEndpointRouteBuilder MapShareXEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/sharex/upload", UploadAsync);
        return endpoints;
    }

    private static async Task<IResult> UploadAsync(
        HttpContext context,
        IOptions<UploadOptions> options,
        MediaValidator validator,
        ObjectIdGenerator objectIds,
        IMediaStorage storage,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("MediaRelay.ShareX.Upload");
        var apiKey = options.Value.ApiKey;
        if (!IsAuthorized(context.Request, apiKey))
            return Problem(StatusCodes.Status401Unauthorized, "unauthorized", "A valid bearer API key is required.");

        if (!TryGetBoundary(context.Request.ContentType, out var boundary))
            return Problem(StatusCodes.Status400BadRequest, "invalid_multipart", "A multipart/form-data request with a valid boundary is required.");

        var maxUploadSize = options.Value.MaxUploadSize;
        var maxRequestSize = maxUploadSize > long.MaxValue - MultipartOverheadLimit
            ? long.MaxValue
            : maxUploadSize + MultipartOverheadLimit;
        if (context.Request.ContentLength is long contentLength && contentLength > maxRequestSize)
            return Problem(StatusCodes.Status413PayloadTooLarge, "size_exceeded", "The request exceeds the maximum upload size.");

        // ASP.NET's default server limit is smaller than MediaRelay's configured 512 MiB cap.
        // Disable it only for this route; the bounded reader below remains authoritative.
        var sizeFeature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (sizeFeature is { IsReadOnly: false }) sizeFeature.MaxRequestBodySize = null;

        await using var boundedBody = new LimitedReadStream(context.Request.Body, maxRequestSize);
        var reader = new MultipartReader(boundary, boundedBody)
        {
            BodyLengthLimit = maxRequestSize,
            HeadersCountLimit = 16,
            HeadersLengthLimit = 16 * 1024
        };

        MultipartSection? section;
        try
        {
            section = await reader.ReadNextSectionAsync(context.RequestAborted);
        }
        catch (IOException)
        {
            return Problem(StatusCodes.Status413PayloadTooLarge, "size_exceeded", "The request exceeds the maximum upload size.");
        }
        catch (InvalidDataException)
        {
            return Problem(StatusCodes.Status400BadRequest, "invalid_multipart", "The multipart request is malformed.");
        }

        if (!TryCreateUploadRequest(section, out var upload))
            return Problem(StatusCodes.Status400BadRequest, "invalid_multipart", "The request must contain one file in the 'file' field.");

        ValidatedMedia media;
        try { media = validator.ValidateMetadata(upload.FileName, upload.ContentType); }
        catch (ArgumentException)
        {
            return Problem(StatusCodes.Status415UnsupportedMediaType, "unsupported_media", "The file name and content type are not a supported media pair.");
        }

        var tempPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            await using var temp = new FileStream(tempPath, new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.ReadWrite,
                Share = FileShare.Read,
                BufferSize = CopyBufferSize,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan | FileOptions.DeleteOnClose
            });

            CopyResult copyResult;
            try { copyResult = await CopyBoundedAsync(upload.Content, temp, maxUploadSize, context.RequestAborted); }
            catch (InvalidDataException)
            {
                return Problem(StatusCodes.Status400BadRequest, "invalid_multipart", "The multipart request is malformed.");
            }
            if (copyResult == CopyResult.TooLarge)
                return Problem(StatusCodes.Status413PayloadTooLarge, "size_exceeded", "The file exceeds the maximum upload size.");
            if (copyResult == CopyResult.Empty)
                return Problem(StatusCodes.Status415UnsupportedMediaType, "unsupported_media", "The uploaded file is empty or has an invalid media signature.");

            try
            {
                section = await reader.ReadNextSectionAsync(context.RequestAborted);
                if (section is not null)
                    return Problem(StatusCodes.Status400BadRequest, "invalid_multipart", "The request must contain exactly one file field.");
                await boundedBody.CopyToAsync(Stream.Null, context.RequestAborted);
            }
            catch (UploadTooLargeException)
            {
                return Problem(StatusCodes.Status413PayloadTooLarge, "size_exceeded", "The request exceeds the maximum upload size.");
            }
            catch (IOException)
            {
                return Problem(StatusCodes.Status413PayloadTooLarge, "size_exceeded", "The request exceeds the maximum upload size.");
            }
            catch (InvalidDataException)
            {
                return Problem(StatusCodes.Status400BadRequest, "invalid_multipart", "The multipart request is malformed.");
            }

            temp.Position = 0;
            var signature = new byte[Math.Min(SignatureLength, (int)Math.Min(temp.Length, SignatureLength))];
            var read = await temp.ReadAsync(signature, context.RequestAborted);
            try { validator.Validate(upload.FileName, upload.ContentType, signature.AsSpan(0, read)); }
            catch (ArgumentException)
            {
                return Problem(StatusCodes.Status415UnsupportedMediaType, "unsupported_media", "The file signature does not match its declared media type.");
            }

            var objectId = objectIds.Create(media.Extension);
            temp.Position = 0;
            try
            {
                await storage.UploadAsync(objectId, temp, media.ContentType, context.RequestAborted);
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                logger.LogWarning("ShareX storage upload failed for object {ObjectId}", objectId);
                return Problem(StatusCodes.Status503ServiceUnavailable, "storage_unavailable", "Upload storage is temporarily unavailable.");
            }

            logger.LogInformation("ShareX upload stored as object {ObjectId} with content type {ContentType}", objectId, media.ContentType);
            return Results.Json(new ShareXUploadResponse(storage.GetPublicUrl(objectId)));
        }
        catch (UploadTooLargeException)
        {
            return Problem(StatusCodes.Status413PayloadTooLarge, "size_exceeded", "The request exceeds the maximum upload size.");
        }
        finally
        {
            try { File.Delete(tempPath); }
            catch (IOException) { logger.LogWarning("Unable to remove temporary ShareX upload file"); }
            catch (UnauthorizedAccessException) { logger.LogWarning("Unable to remove temporary ShareX upload file"); }
        }
    }

    private static bool IsAuthorized(HttpRequest request, string configuredKey)
    {
        var suppliedKey = string.Empty;
        if (AuthenticationHeaderValue.TryParse(request.Headers.Authorization, out var authorization) &&
            string.Equals(authorization.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrEmpty(authorization.Parameter))
        {
            suppliedKey = authorization.Parameter;
        }

        var expectedHash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(configuredKey));
        var suppliedHash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(suppliedKey));
        return CryptographicOperations.FixedTimeEquals(expectedHash, suppliedHash) && suppliedKey.Length > 0;
    }

    private static bool TryGetBoundary(string? contentType, out string boundary)
    {
        boundary = string.Empty;
        if (!MediaTypeHeaderValue.TryParse(contentType, out var parsed) ||
            !string.Equals(parsed.MediaType.Value, "multipart/form-data", StringComparison.OrdinalIgnoreCase)) return false;
        var value = HeaderUtilities.RemoveQuotes(parsed.Boundary);
        if (value.Length is 0 or > 128) return false;
        boundary = value.Value!;
        return true;
    }

    private static bool TryCreateUploadRequest(MultipartSection? section, out ShareXUploadRequest upload)
    {
        upload = null!;
        if (section is null || !ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var disposition) ||
            !string.Equals(disposition.DispositionType.Value, "form-data", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(HeaderUtilities.RemoveQuotes(disposition.Name).Value, "file", StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(section.ContentType)) return false;

        var fileNameSegment = !StringSegment.IsNullOrEmpty(disposition.FileNameStar)
            ? disposition.FileNameStar
            : disposition.FileName;
        var fileName = HeaderUtilities.RemoveQuotes(fileNameSegment).Value;
        if (string.IsNullOrWhiteSpace(fileName)) return false;
        if (!MediaTypeHeaderValue.TryParse(section.ContentType, out var contentType)) return false;
        upload = new ShareXUploadRequest(fileName, contentType.MediaType.Value!, section.Body);
        return true;
    }

    private static async Task<CopyResult> CopyBoundedAsync(Stream source, Stream destination, long maxSize, CancellationToken ct)
    {
        var buffer = new byte[CopyBufferSize];
        long total = 0;
        while (true)
        {
            var remaining = maxSize - total;
            var requested = (int)(remaining >= buffer.Length ? buffer.Length : remaining + 1);
            var read = await source.ReadAsync(buffer.AsMemory(0, requested), ct);
            if (read == 0) break;
            if (read > maxSize - total) return CopyResult.TooLarge;
            await destination.WriteAsync(buffer.AsMemory(0, read), ct);
            total += read;
        }
        return total == 0 ? CopyResult.Empty : CopyResult.Copied;
    }

    private static IResult Problem(int status, string title, string detail) => Results.Problem(statusCode: status, title: title, detail: detail);

    private enum CopyResult { Copied, Empty, TooLarge }
    private sealed record ShareXUploadResponse(string Url);

    private sealed class UploadTooLargeException : IOException;

    private sealed class LimitedReadStream(Stream inner, long limit) : Stream
    {
        private long _read;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => ReadLimitedAsync(buffer, cancellationToken);
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => ReadLimitedAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        private async ValueTask<int> ReadLimitedAsync(Memory<byte> buffer, CancellationToken ct)
        {
            var remaining = limit - _read;
            if (remaining < 0) throw new UploadTooLargeException();
            var request = (int)Math.Min(buffer.Length, remaining == long.MaxValue ? long.MaxValue : remaining + 1);
            var read = await inner.ReadAsync(buffer[..request], ct);
            if (read > remaining) throw new UploadTooLargeException();
            _read += read;
            return read;
        }

        protected override void Dispose(bool disposing) { }
        public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
