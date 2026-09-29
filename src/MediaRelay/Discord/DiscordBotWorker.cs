using MediaRelay.Options;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Options;

namespace MediaRelay.Discord;

public sealed class DiscordBotWorker(IServiceProvider services, IOptions<DiscordOptions> options, ILogger<DiscordBotWorker> logger) : IHostedService
{
    private DiscordBotService? _bot;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled) return;
        try
        {
            _bot = services.GetRequiredService<DiscordBotService>();
            await _bot.StartAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogCritical(exception, "Discord bot could not start");
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => _bot?.StopAsync(cancellationToken) ?? Task.CompletedTask;
}

public sealed class DiscordChannelTransport(DiscordBotService bot) : IDiscordChannelTransport
{
    public async Task<bool> HasRecentBotMessageAsync(ulong guildId, ulong channelId, string message, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var channel = GetChannel(guildId, channelId);
        var botUserId = bot.Client.CurrentUser.Id;
        await using var history = channel.GetMessagesAsync(50).GetAsyncEnumerator(ct);
        while (await history.MoveNextAsync())
        {
            foreach (var recent in history.Current)
                if (recent.Author.Id == botUserId && string.Equals(recent.Content, message, StringComparison.Ordinal)) return true;
        }
        return false;
    }

    public async Task SendMessageAsync(ulong guildId, ulong channelId, string message, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var channel = GetChannel(guildId, channelId);
        await channel.SendMessageAsync(message);
    }

    private SocketTextChannel GetChannel(ulong guildId, ulong channelId) => bot.Client.GetGuild(guildId)?.GetTextChannel(channelId)
        ?? throw new InvalidOperationException("The target Discord channel is unavailable.");
}
