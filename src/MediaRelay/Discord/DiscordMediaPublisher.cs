namespace MediaRelay.Discord;

public sealed class DiscordMediaPublisher(IDiscordChannelTransport transport) : IMediaPublisher
{
    public async Task<bool> PublishAsync(ulong guildId, ulong channelId, string publicUrl, CancellationToken ct)
    {
        try
        {
            await transport.SendMessageAsync(guildId, channelId, publicUrl, ct);
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }
}
