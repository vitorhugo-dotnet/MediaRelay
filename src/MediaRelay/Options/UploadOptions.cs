using System.ComponentModel.DataAnnotations;

namespace MediaRelay.Options;

public sealed class UploadOptions
{
    public const string SectionName = "Upload";

    [Required]
    public string ApiKey { get; set; } = string.Empty;

    public long MaxUploadSize { get; set; } = 536870912;

    public int SessionTtlMinutes { get; set; } = 30;

    public int PresignedUploadTtlMinutes { get; set; } = 15;

    public TimeSpan SessionTtl => TimeSpan.FromMinutes(SessionTtlMinutes);

    public TimeSpan PresignedUploadTtl => TimeSpan.FromMinutes(PresignedUploadTtlMinutes);
}
