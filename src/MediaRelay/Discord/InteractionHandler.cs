using Discord.Interactions;
using Discord.WebSocket;

namespace MediaRelay.Discord;

public sealed class InteractionHandler(
    DiscordSocketClient client,
    InteractionService interactions,
    IServiceProvider services,
    ILogger<InteractionHandler> logger) : IDisposable
{
    public void Attach() => client.InteractionCreated += HandleAsync;

    private async Task HandleAsync(SocketInteraction interaction)
    {
        var context = new SocketInteractionContext(client, interaction);
        var result = await interactions.ExecuteCommandAsync(context, services);
        if (!result.IsSuccess)
            logger.LogWarning("Discord interaction failed with {Error}: {Reason}", result.Error, result.ErrorReason);
    }

    public void Dispose() => client.InteractionCreated -= HandleAsync;
}
