using System.ComponentModel.DataAnnotations;

namespace MediaRelay.Options;

public sealed class DiscordOptions : IValidatableObject
{
    public const string SectionName = "Discord";

    public string Token { get; set; } = string.Empty;

    public ulong ApplicationId { get; set; }

    public string AllowedGuildIds { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    public bool RegisterCommandsPerGuild { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!Enabled) yield break;
        if (string.IsNullOrWhiteSpace(Token))
            yield return new ValidationResult("Discord token is required when Discord is enabled.", [nameof(Token)]);
        if (ApplicationId == 0)
            yield return new ValidationResult("Discord application ID must be greater than zero when Discord is enabled.", [nameof(ApplicationId)]);
    }
}
