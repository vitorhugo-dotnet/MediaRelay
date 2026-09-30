using System.ComponentModel.DataAnnotations;

namespace MediaRelay.Options;

public sealed class MinioOptions
{
    public const string SectionName = "Minio";

    [Required]
    public string Endpoint { get; set; } = string.Empty;

    [Required]
    public string PublicEndpoint { get; set; } = string.Empty;

    [Required]
    public string AccessKey { get; set; } = string.Empty;

    [Required]
    public string SecretKey { get; set; } = string.Empty;

    public string Bucket { get; set; } = "media";

    public bool UseSsl { get; set; }

    public bool PublicUseSsl { get; set; } = true;
}
