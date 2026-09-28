using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MediaRelay.Options;

namespace MediaRelay.Tests.Options;

public sealed class OptionsValidationTests
{
    [Fact]
    public async Task RequiredConfigurationBindsAndAppliesDefaults()
    {
        await using var factory = new ConfiguredApplicationFactory(ValidConfiguration());
        using var client = factory.CreateClient();

        var discord = factory.Services.GetRequiredService<IOptions<DiscordOptions>>().Value;
        var minio = factory.Services.GetRequiredService<IOptions<MinioOptions>>().Value;
        var publicUrls = factory.Services.GetRequiredService<IOptions<PublicUrlOptions>>().Value;
        var upload = factory.Services.GetRequiredService<IOptions<UploadOptions>>().Value;

        Assert.Equal("bot-token-example", discord.Token);
        Assert.False(discord.Enabled);
        Assert.Equal("media", minio.Bucket);
        Assert.Equal(536870912, upload.MaxUploadSize);
        Assert.Equal(TimeSpan.FromMinutes(30), upload.SessionTtl);
        Assert.Equal(TimeSpan.FromMinutes(15), upload.PresignedUploadTtl);
        Assert.Equal("https://media.example.test", publicUrls.MediaBaseUrl);
    }

    [Fact]
    public async Task MissingRequiredProductionValuesFailAtStartup()
    {
        await using var factory = new ConfiguredApplicationFactory(new Dictionary<string, string?>());

        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("Discord", exception.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DiscordDisabledAllowsMissingCredentials()
    {
        var values = ValidConfiguration();
        values.Remove("Discord:Token");
        values.Remove("Discord:ApplicationId");
        values["Discord:Enabled"] = "false";
        await using var factory = new ConfiguredApplicationFactory(values);

        using var client = factory.CreateClient();
        var discord = factory.Services.GetRequiredService<IOptions<DiscordOptions>>().Value;

        Assert.False(discord.Enabled);
        Assert.Empty(discord.Token);
        Assert.Equal(0UL, discord.ApplicationId);
    }

    [Theory]
    [InlineData("Discord:Token")]
    [InlineData("Discord:ApplicationId")]
    public async Task DiscordEnabledRequiresBothCredentials(string missingSetting)
    {
        var values = ValidConfiguration();
        values["Discord:Enabled"] = "true";
        values.Remove(missingSetting);
        await using var factory = new ConfiguredApplicationFactory(values);

        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("Discord", exception.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DiscordBotModeDefaultsToEnabled()
    {
        Assert.True(new DiscordOptions().Enabled);
    }

    private static Dictionary<string, string?> ValidConfiguration() => new()
    {
        ["Discord:Token"] = "bot-token-example",
        ["Discord:ApplicationId"] = "123456789",
        ["Discord:Enabled"] = "false",
        ["Minio:Endpoint"] = "minio.example.test:9000",
        ["Minio:PublicEndpoint"] = "media.example.test",
        ["Minio:AccessKey"] = "minio-access-example",
        ["Minio:SecretKey"] = "minio-secret-example",
        ["PublicUrls:MediaBaseUrl"] = "https://media.example.test",
        ["PublicUrls:AppBaseUrl"] = "https://upload.example.test",
        ["Upload:ApiKey"] = "upload-key-example"
    };

    private sealed class ConfiguredApplicationFactory(Dictionary<string, string?> values) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder) =>
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(values));
    }
}
