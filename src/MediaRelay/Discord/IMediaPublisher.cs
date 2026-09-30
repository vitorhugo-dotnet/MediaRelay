namespace MediaRelay.Discord;

public interface IMediaPublisher
{
    Task<bool> PublishAsync(ulong channelId, string publicUrl, CancellationToken ct);
}

public interface IDiscordChannelTransport
{
    Task<bool> HasRecentBotMessageAsync(ulong channelId, string message, CancellationToken ct);
    Task SendMessageAsync(ulong channelId, string message, CancellationToken ct);
}
