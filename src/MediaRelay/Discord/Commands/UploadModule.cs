using Discord.Interactions;
using Discord;
using Discord.WebSocket;
using MediaRelay.Options;
using MediaRelay.Uploads;
using Microsoft.Extensions.Options;

namespace MediaRelay.Discord.Commands;

public interface IUploadInteractionResponder
{
    Task RespondWithUploadLinkAsync(string url, string token, bool ephemeral, CancellationToken ct);
    Task RespondRejectedAsync(string message, bool ephemeral, CancellationToken ct);
}

public sealed class UploadModule(UploadSessionService sessions, IOptions<PublicUrlOptions> publicUrls, IOptions<DiscordOptions> discordOptions)
    : InteractionModuleBase<SocketInteractionContext>
{
    [SlashCommand("upload", "Create a private media upload link")]
    public async Task UploadAsync()
    {
        var responder = new DiscordInteractionResponder(Context);
        await HandleUploadAsync(Context.Guild.Id, Context.Channel.Id, Context.User.Id, responder, CancellationToken.None);
    }

    public async Task HandleUploadAsync(ulong guildId, ulong channelId, ulong userId, IUploadInteractionResponder responder, CancellationToken ct)
    {
        if (!IsAllowedGuild(guildId))
        {
            await responder.RespondRejectedAsync("Uploads are not enabled in this server.", true, ct);
            return;
        }

        var created = await sessions.CreateAsync(guildId, channelId, userId, ct);
        var baseUrl = publicUrls.Value.AppBaseUrl.TrimEnd('/');
        await responder.RespondWithUploadLinkAsync($"{baseUrl}/u/{created.Token}", created.Token, true, ct);
    }

    private bool IsAllowedGuild(ulong guildId) => discordOptions.Value.AllowedGuildIds
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Any(value => ulong.TryParse(value, out var allowed) && allowed == guildId);

    private sealed class DiscordInteractionResponder(SocketInteractionContext context) : IUploadInteractionResponder
    {
        public Task RespondWithUploadLinkAsync(string url, string token, bool ephemeral, CancellationToken ct) =>
            context.Interaction.RespondAsync("Your upload link is ready.", components: new ComponentBuilder()
                .WithButton("Upload media", url: url, style: ButtonStyle.Link).Build(), ephemeral: ephemeral);

        public Task RespondRejectedAsync(string message, bool ephemeral, CancellationToken ct) =>
            context.Interaction.RespondAsync(message, ephemeral: ephemeral);
    }
}
