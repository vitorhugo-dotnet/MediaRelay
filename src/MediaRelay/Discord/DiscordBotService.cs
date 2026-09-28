using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using MediaRelay.Options;
using Microsoft.Extensions.Options;

namespace MediaRelay.Discord;

public sealed class DiscordBotService : IAsyncDisposable
{
    private readonly DiscordSocketClient _client;
    private readonly InteractionService _interactions;
    private readonly InteractionHandler _handler;
    private readonly DiscordApplicationIdentityVerifier _identityVerifier;
    private readonly IServiceProvider _services;
    private readonly DiscordOptions _options;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<DiscordBotService> _logger;

    public DiscordSocketClient Client => _client;
    public InteractionService Interactions => _interactions;

    public DiscordBotService(IServiceProvider services, InteractionHandler handler, DiscordSocketClient client,
        InteractionService interactions, IOptions<DiscordOptions> options,
        DiscordApplicationIdentityVerifier identityVerifier, IHostEnvironment environment, ILogger<DiscordBotService> logger)
    {
        _services = services;
        _handler = handler;
        _identityVerifier = identityVerifier;
        _options = options.Value;
        _environment = environment;
        _logger = logger;
        _client = client;
        _interactions = interactions;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        if (!_options.Enabled) return;
        await _interactions.AddModulesAsync(typeof(DiscordBotService).Assembly, _services);
        await _client.LoginAsync(TokenType.Bot, _options.Token);
        try { await _identityVerifier.VerifyAsync(_options.ApplicationId, ct); }
        catch
        {
            await _client.LogoutAsync();
            throw;
        }
        _handler.Attach();
        await _client.StartAsync();
        var guilds = ParseAllowedGuilds(_options.AllowedGuildIds).ToArray();
        if (_environment.IsDevelopment() || _options.RegisterCommandsPerGuild)
        {
            foreach (var guildId in guilds) await _interactions.RegisterCommandsToGuildAsync(guildId);
        }
        else
        {
            await _interactions.RegisterCommandsGloballyAsync();
        }
        _logger.LogInformation("Discord bot started with {AllowedGuildCount} allowed guilds", guilds.Length);
    }

    public async Task StopAsync(CancellationToken ct)
    {
        if (!_options.Enabled) return;
        await _client.StopAsync();
        await _client.LogoutAsync();
    }

    public async ValueTask DisposeAsync()
    {
        _handler.Dispose();
        await _client.DisposeAsync();
    }

    private static IEnumerable<ulong> ParseAllowedGuilds(string configured) => configured
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(value => ulong.TryParse(value, out var id) ? id : 0)
        .Where(id => id > 0);
}
