using MediaRelay.Uploads;

namespace MediaRelay.Tests.Uploads;

public sealed class MediaValidatorTests
{
    public static TheoryData<string, string, byte[]> AllowedPairs => new()
    {
        { "photo.png", "image/png", [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A] },
        { "photo.jpg", "image/jpeg", [0xFF, 0xD8, 0xFF] },
        { "photo.jpeg", "image/jpeg", [0xFF, 0xD8, 0xFF] },
        { "photo.gif", "image/gif", "GIF89a"u8.ToArray() },
        { "photo.webp", "image/webp", "RIFF\x00\x00\x00\x00WEBP"u8.ToArray() },
        { "clip.mp4", "video/mp4", [0, 0, 0, 0x18, (byte)'f', (byte)'t', (byte)'y', (byte)'p'] }
    };

    [Theory]
    [MemberData(nameof(AllowedPairs))]
    public void ValidateAcceptsAllowedPairAndSignature(string fileName, string contentType, byte[] header)
    {
        var media = new MediaValidator().Validate(fileName, contentType, header);
        Assert.Equal(Path.GetExtension(fileName), media.Extension);
        Assert.Equal(contentType, media.ContentType);
    }

    [Theory]
    [InlineData("photo.png", "image/jpeg")]
    [InlineData("photo.jpg", "image/png")]
    [InlineData("clip.mp4", "image/mp4")]
    public void ValidateRejectsMismatchedPair(string fileName, string contentType) =>
        Assert.Throws<ArgumentException>(() => new MediaValidator().Validate(fileName, contentType, [0xFF, 0xD8, 0xFF]));

    [Theory]
    [InlineData("archive.zip", "application/zip")]
    [InlineData("photo.PNG", "image/png")]
    [InlineData("photo.png.exe", "image/png")]
    public void ValidateRejectsUnsupportedTypes(string fileName, string contentType) =>
        Assert.Throws<ArgumentException>(() => new MediaValidator().Validate(fileName, contentType, []));

    [Theory]
    [InlineData("photo.png", "image/png", new byte[] { 0x89, 0x50 })]
    [InlineData("photo.jpg", "image/jpeg", new byte[] { 0xFF, 0xD8, 0x00 })]
    [InlineData("photo.gif", "image/gif", new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x62 })]
    [InlineData("photo.webp", "image/webp", new byte[] { 0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0, 0x4E, 0x4F, 0x50, 0x45 })]
    [InlineData("clip.mp4", "video/mp4", new byte[] { 0, 0, 0, 0x18, 0x62, 0x61, 0x64, 0x21 })]
    public void ValidateRejectsTruncatedOrIncorrectSignatures(string fileName, string contentType, byte[] header) =>
        Assert.Throws<ArgumentException>(() => new MediaValidator().Validate(fileName, contentType, header));

    [Theory]
    [MemberData(nameof(AllowedPairs))]
    public void ValidateMetadataAcceptsAllowedPair(string fileName, string contentType, byte[] _)
    {
        var media = new MediaValidator().ValidateMetadata(fileName, contentType);
        Assert.Equal(Path.GetExtension(fileName), media.Extension);
        Assert.Equal(contentType, media.ContentType);
    }

    [Theory]
    [InlineData("photo.png", "image/jpeg")]
    [InlineData("photo.jpg", "image/png")]
    [InlineData("archive.zip", "application/zip")]
    public void ValidateMetadataRejectsUnsupportedOrMismatchedPair(string fileName, string contentType) =>
        Assert.Throws<ArgumentException>(() => new MediaValidator().ValidateMetadata(fileName, contentType));
}
