namespace MediaRelay.Discord;

public sealed class DiscordMediaPublisher(IDiscordChannelTransport transport) : IMediaPublisher
{
    public async Task<bool> PublishAsync(ulong channelId, string publicUrl, CancellationToken ct)
    {
        try
        {
            if (await transport.HasRecentBotMessageAsync(channelId, publicUrl, ct)) return true;
            await transport.SendMessageAsync(channelId, publicUrl, ct);
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
