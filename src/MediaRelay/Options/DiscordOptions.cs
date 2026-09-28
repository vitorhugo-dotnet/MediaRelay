using System.ComponentModel.DataAnnotations;

namespace MediaRelay.Options;

public sealed class DiscordOptions
{
    public const string SectionName = "Discord";

    [Required]
    public string Token { get; set; } = string.Empty;

    [Range(1, ulong.MaxValue)]
    public ulong ApplicationId { get; set; }

    public string AllowedGuildIds { get; set; } = string.Empty;
}
