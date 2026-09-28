namespace MediaRelay.Discord;

public interface IMediaPublisher
{
    Task<bool> PublishAsync(ulong guildId, ulong channelId, string publicUrl, CancellationToken ct);
}

public interface IDiscordChannelTransport
{
    Task SendMessageAsync(ulong guildId, ulong channelId, string message, CancellationToken ct);
}
