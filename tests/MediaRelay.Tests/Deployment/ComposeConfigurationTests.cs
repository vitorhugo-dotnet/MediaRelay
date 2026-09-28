using System.Diagnostics;
using System.Text.Json;

namespace MediaRelay.Tests.Deployment;

public sealed class ComposeConfigurationTests
{
    [Fact]
    public async Task ProductionComposeParsesWithPersistentMinioAndPrivateConsole()
    {
        var repositoryRoot = FindRepositoryRoot();
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo("docker")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            }
        };
        process.StartInfo.ArgumentList.Add("compose");
        process.StartInfo.ArgumentList.Add("-f");
        process.StartInfo.ArgumentList.Add(Path.Combine(repositoryRoot, "deploy", "docker-compose.prod.yml"));
        process.StartInfo.ArgumentList.Add("config");
        process.StartInfo.ArgumentList.Add("--format");
        process.StartInfo.ArgumentList.Add("json");
        SetComposeTestEnvironment(process.StartInfo);

        process.Start();
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        Assert.True(process.ExitCode == 0, $"docker compose config failed: {stderr}");
        using var document = JsonDocument.Parse(stdout);
        var services = document.RootElement.GetProperty("services");
        var minio = services.GetProperty("minio");
        var app = services.GetProperty("app");

        Assert.Equal("media-relay-minio:community-9e49d5e", minio.GetProperty("image").GetString());
        Assert.Equal("deploy/minio-community.Dockerfile", minio.GetProperty("build").GetProperty("dockerfile").GetString());
        Assert.Contains(minio.GetProperty("volumes").EnumerateArray(), mount =>
            mount.GetProperty("type").GetString() == "volume" && mount.GetProperty("source").GetString() == "minio-data");
        Assert.DoesNotContain(minio.GetProperty("ports").EnumerateArray(), port =>
            port.GetProperty("target").GetInt32() == 9001);
        Assert.Contains(app.GetProperty("healthcheck").GetProperty("test").EnumerateArray(), value =>
            value.GetString()!.Contains("/health", StringComparison.Ordinal));
        Assert.Contains(document.RootElement.GetProperty("volumes").EnumerateObject(), volume => volume.Name == "minio-data");
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "MediaRelay.sln")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate MediaRelay.sln from the test output directory.");
    }

    private static void SetComposeTestEnvironment(ProcessStartInfo startInfo)
    {
        startInfo.Environment["IMAGE"] = "ghcr.io/vitorhugo-dotnet/media-relay:sha-0123456789abcdef0123456789abcdef01234567";
        startInfo.Environment["MINIO_ROOT_USER"] = "test-minio-user";
        startInfo.Environment["MINIO_ROOT_PASSWORD"] = "test-minio-password-long-enough";
        startInfo.Environment["MINIO_BUCKET"] = "media";
        startInfo.Environment["MINIO_PUBLIC_ENDPOINT"] = "media.example.test";
        startInfo.Environment["MINIO_ACCESS_KEY"] = "test-app-user";
        startInfo.Environment["MINIO_SECRET_KEY"] = "test-app-password-long-enough";
        startInfo.Environment["DISCORD_ENABLED"] = "false";
        startInfo.Environment["DISCORD_TOKEN"] = "test-discord-token";
        startInfo.Environment["DISCORD_APPLICATION_ID"] = "123456789012345678";
        startInfo.Environment["DISCORD_ALLOWED_GUILD_IDS"] = "";
        startInfo.Environment["PUBLIC_MEDIA_BASE_URL"] = "https://media.example.test";
        startInfo.Environment["PUBLIC_APP_BASE_URL"] = "https://upload.example.test";
        startInfo.Environment["UPLOAD_API_KEY"] = "test-upload-key";
    }
}
