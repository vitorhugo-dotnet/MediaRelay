using MediaRelay.Discord;

namespace MediaRelay.Tests.Discord;

public sealed class DiscordMediaPublisherTests
{
    [Fact]
    public async Task PublisherPostsCanonicalUrlToOriginalChannel()
    {
        var transport = new FakeDiscordChannelTransport();
        var publisher = new DiscordMediaPublisher(transport);

        var result = await publisher.PublishAsync(22, "https://app.test/u/abc", CancellationToken.None);

        Assert.True(result);
        Assert.Equal((22UL, "https://app.test/u/abc"), (transport.ChannelId, transport.Message));
    }

    [Fact]
    public async Task PublisherReturnsFalseWhenDiscordFails()
    {
        var publisher = new DiscordMediaPublisher(new FakeDiscordChannelTransport { Fail = true });

        Assert.False(await publisher.PublishAsync(22, "https://app.test/u/abc", CancellationToken.None));
    }

    [Fact]
    public async Task RetryDetectsAcceptedMessageAndDoesNotSendDuplicate()
    {
        var transport = new FakeDiscordChannelTransport { AcceptThenFail = true };
        var publisher = new DiscordMediaPublisher(transport);

        Assert.False(await publisher.PublishAsync(22, "https://app.test/u/abc", CancellationToken.None));
        Assert.True(await publisher.PublishAsync(22, "https://app.test/u/abc", CancellationToken.None));
        Assert.Equal(1, transport.SendCalls);
        Assert.Equal(2, transport.HistoryCalls);
    }

    [Fact]
    public async Task PublisherDoesNotSendWhenChannelHistoryCannotBeChecked()
    {
        var transport = new FakeDiscordChannelTransport { HistoryFails = true };

        Assert.False(await new DiscordMediaPublisher(transport).PublishAsync(22, "https://app.test/u/abc", CancellationToken.None));
        Assert.Equal(0, transport.SendCalls);
    }

    private sealed class FakeDiscordChannelTransport : IDiscordChannelTransport
    {
        public bool Fail { get; init; }
        public bool AcceptThenFail { get; set; }
        public bool HistoryFails { get; init; }
        public int SendCalls { get; private set; }
        public int HistoryCalls { get; private set; }
        private readonly HashSet<string> _acceptedMessages = [];
        public ulong ChannelId { get; private set; }
        public string? Message { get; private set; }
        public Task<bool> HasRecentBotMessageAsync(ulong channelId, string message, CancellationToken ct)
        {
            HistoryCalls++;
            if (HistoryFails) throw new InvalidOperationException("history unavailable");
            return Task.FromResult(_acceptedMessages.Contains(message));
        }
        public Task SendMessageAsync(ulong channelId, string message, CancellationToken ct)
        {
            SendCalls++;
            if (Fail) throw new InvalidOperationException("discord offline");
            ChannelId = channelId;
            Message = message;
            _acceptedMessages.Add(message);
            if (AcceptThenFail)
            {
                AcceptThenFail = false;
                throw new InvalidOperationException("connection lost after acceptance");
            }
            return Task.CompletedTask;
        }
    }
}
