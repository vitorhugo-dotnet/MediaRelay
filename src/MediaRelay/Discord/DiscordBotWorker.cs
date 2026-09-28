using MediaRelay.Options;
using Microsoft.Extensions.Options;

namespace MediaRelay.Discord;

public sealed class DiscordBotWorker(DiscordBotService bot, IOptions<DiscordOptions> options, ILogger<DiscordBotWorker> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled) return;
        try { await bot.StartAsync(cancellationToken); }
        catch (Exception exception)
        {
            logger.LogCritical(exception, "Discord bot could not start");
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => bot.StopAsync(cancellationToken);
}

public sealed class DiscordChannelTransport(DiscordBotService bot) : IDiscordChannelTransport
{
    public async Task SendMessageAsync(ulong guildId, ulong channelId, string message, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var channel = bot.Client.GetGuild(guildId)?.GetTextChannel(channelId)
            ?? throw new InvalidOperationException("The target Discord channel is unavailable.");
        await channel.SendMessageAsync(message);
    }
}
