using MediaRelay.Discord;

namespace MediaRelay.Tests.Discord;

public sealed class DiscordMediaPublisherTests
{
    [Fact]
    public async Task PublisherPostsCanonicalUrlToOriginalChannel()
    {
        var transport = new FakeDiscordChannelTransport();
        var publisher = new DiscordMediaPublisher(transport);

        var result = await publisher.PublishAsync(11, 22, "https://app.test/u/abc", CancellationToken.None);

        Assert.True(result);
        Assert.Equal((11UL, 22UL, "https://app.test/u/abc"), (transport.GuildId, transport.ChannelId, transport.Message));
    }

    [Fact]
    public async Task PublisherReturnsFalseWhenDiscordFails()
    {
        var publisher = new DiscordMediaPublisher(new FakeDiscordChannelTransport { Fail = true });

        Assert.False(await publisher.PublishAsync(11, 22, "https://app.test/u/abc", CancellationToken.None));
    }

    private sealed class FakeDiscordChannelTransport : IDiscordChannelTransport
    {
        public bool Fail { get; init; }
        public ulong GuildId { get; private set; }
        public ulong ChannelId { get; private set; }
        public string? Message { get; private set; }
        public Task SendMessageAsync(ulong guildId, ulong channelId, string message, CancellationToken ct)
        {
            if (Fail) throw new InvalidOperationException("discord offline");
            GuildId = guildId;
            ChannelId = channelId;
            Message = message;
            return Task.CompletedTask;
        }
    }
}
