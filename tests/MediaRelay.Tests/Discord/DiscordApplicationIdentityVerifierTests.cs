using MediaRelay.Discord;

namespace MediaRelay.Tests.Discord;

public sealed class DiscordApplicationIdentityVerifierTests
{
    [Fact]
    public async Task VerifyAcceptsConfiguredApplicationId()
    {
        var verifier = new DiscordApplicationIdentityVerifier(new FakeProvider(123));

        await verifier.VerifyAsync(123, CancellationToken.None);
    }

    [Fact]
    public async Task VerifyRejectsAuthenticatedApplicationIdMismatch()
    {
        var verifier = new DiscordApplicationIdentityVerifier(new FakeProvider(456));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => verifier.VerifyAsync(123, CancellationToken.None));

        Assert.Contains("does not match", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FakeProvider(ulong applicationId) : IDiscordApplicationInfoProvider
    {
        public Task<ulong> GetApplicationIdAsync(CancellationToken ct) => Task.FromResult(applicationId);
    }
}
