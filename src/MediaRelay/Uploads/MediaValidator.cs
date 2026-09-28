namespace MediaRelay.Uploads;

public sealed class MediaValidator
{
    private static readonly IReadOnlyDictionary<string, string> AllowedTypes = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif",
        [".webp"] = "image/webp",
        [".mp4"] = "video/mp4"
    };

    public ValidatedMedia Validate(string fileName, string declaredContentType, ReadOnlySpan<byte> header)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(declaredContentType);

        var extension = Path.GetExtension(fileName);
        if (!AllowedTypes.TryGetValue(extension, out var expectedContentType) ||
            !string.Equals(expectedContentType, declaredContentType, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("File extension and content type must be an allowed media pair.");
        }

        if (!HasValidSignature(extension, header))
        {
            throw new ArgumentException("File signature does not match the declared media type.", nameof(header));
        }

        return new ValidatedMedia(extension, expectedContentType);
    }

    private static bool HasValidSignature(string extension, ReadOnlySpan<byte> header) => extension switch
    {
        ".png" => header.Length >= 8 && header[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }),
        ".jpg" or ".jpeg" => header.Length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF,
        ".gif" => header.Length >= 6 && (header[..6].SequenceEqual("GIF87a"u8) || header[..6].SequenceEqual("GIF89a"u8)),
        ".webp" => header.Length >= 12 && header[..4].SequenceEqual("RIFF"u8) && header[8..12].SequenceEqual("WEBP"u8),
        ".mp4" => header.Length >= 8 && header[4..8].SequenceEqual("ftyp"u8),
        _ => false
    };
}
