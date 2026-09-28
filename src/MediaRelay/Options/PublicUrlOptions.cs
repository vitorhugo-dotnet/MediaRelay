using System.ComponentModel.DataAnnotations;

namespace MediaRelay.Options;

public sealed class PublicUrlOptions
{
    public const string SectionName = "PublicUrls";

    [Required]
    public string MediaBaseUrl { get; set; } = string.Empty;

    [Required]
    public string AppBaseUrl { get; set; } = string.Empty;
}
