using MediaRelay.Discord.Commands;
using MediaRelay.Options;
using MediaRelay.Uploads;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace MediaRelay.Tests.Discord;

public sealed class UploadModuleTests
{
    [Fact]
    public async Task UploadRespondsEphemerallyWithUploaderLinkAndOriginalDiscordIds()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var sessions = new UploadSessionService(cache, Microsoft.Extensions.Options.Options.Create(new UploadOptions()));
        var module = new UploadModule(sessions, Microsoft.Extensions.Options.Options.Create(new PublicUrlOptions { AppBaseUrl = "https://app.test/base" }), Microsoft.Extensions.Options.Options.Create(new DiscordOptions { AllowedGuildIds = "1" }));
        var responder = new FakeResponder();

        await module.HandleUploadAsync(1, 2, 3, responder, CancellationToken.None);

        Assert.True(responder.Ephemeral);
        Assert.StartsWith("https://app.test/base/u/", responder.Url);
        var session = await sessions.FindAsync(responder.Token!, CancellationToken.None);
        Assert.NotNull(session);
        Assert.Equal((1UL, 2UL, 3UL), (session.GuildId, session.ChannelId, session.UserId));
    }

    [Fact]
    public async Task UploadRejectsGuildOutsideAllowlistWithoutCreatingSession()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var sessions = new UploadSessionService(cache, Microsoft.Extensions.Options.Options.Create(new UploadOptions()));
        var module = new UploadModule(sessions, Microsoft.Extensions.Options.Options.Create(new PublicUrlOptions { AppBaseUrl = "https://app.test" }), Microsoft.Extensions.Options.Options.Create(new DiscordOptions { AllowedGuildIds = "1" }));
        var responder = new FakeResponder();

        await module.HandleUploadAsync(2, 3, 4, responder, CancellationToken.None);

        Assert.True(responder.Ephemeral);
        Assert.Null(responder.Token);
        Assert.Null(await sessions.FindAsync("", CancellationToken.None));
    }

    [Fact]
    public async Task UploadAllowsAnyGuildWhenAllowlistIsEmpty()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var sessions = new UploadSessionService(cache, Microsoft.Extensions.Options.Options.Create(new UploadOptions()));
        var module = new UploadModule(sessions, Microsoft.Extensions.Options.Options.Create(new PublicUrlOptions { AppBaseUrl = "https://app.test" }), Microsoft.Extensions.Options.Options.Create(new DiscordOptions { AllowedGuildIds = "" }));
        var responder = new FakeResponder();

        await module.HandleUploadAsync(27, 3, 4, responder, CancellationToken.None);

        Assert.True(responder.Ephemeral);
        Assert.NotNull(responder.Token);
        var session = await sessions.FindAsync(responder.Token!, CancellationToken.None);
        Assert.NotNull(session);
        Assert.Equal(27UL, session.GuildId);
    }

    private sealed class FakeResponder : IUploadInteractionResponder
    {
        public bool Ephemeral { get; private set; }
        public string? Url { get; private set; }
        public string? Token { get; private set; }
        public Task RespondWithUploadLinkAsync(string url, string token, bool ephemeral, CancellationToken ct)
        {
            Url = url;
            Token = token;
            Ephemeral = ephemeral;
            return Task.CompletedTask;
        }
        public Task RespondRejectedAsync(string message, bool ephemeral, CancellationToken ct)
        {
            Ephemeral = ephemeral;
            return Task.CompletedTask;
        }
    }
}
