using Discord.WebSocket;

namespace MediaRelay.Discord;

public interface IDiscordApplicationInfoProvider
{
    Task<ulong> GetApplicationIdAsync(CancellationToken ct);
}

public sealed class DiscordApplicationInfoProvider(DiscordSocketClient client) : IDiscordApplicationInfoProvider
{
    public async Task<ulong> GetApplicationIdAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var application = await client.Rest.GetApplicationInfoAsync();
        ct.ThrowIfCancellationRequested();
        return application.Id;
    }
}

public sealed class DiscordApplicationIdentityVerifier(IDiscordApplicationInfoProvider provider)
{
    public async Task VerifyAsync(ulong configuredApplicationId, CancellationToken ct)
    {
        var authenticatedApplicationId = await provider.GetApplicationIdAsync(ct);
        if (authenticatedApplicationId != configuredApplicationId)
            throw new InvalidOperationException($"Authenticated Discord application ID {authenticatedApplicationId} does not match configured application ID {configuredApplicationId}.");
    }
}
