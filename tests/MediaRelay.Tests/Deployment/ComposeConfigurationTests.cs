using System.Diagnostics;
using System.Text.Json;

namespace MediaRelay.Tests.Deployment;

public sealed class ComposeConfigurationTests
{
    [Fact]
    public async Task DefaultComposeStartsAppAndLocalMinio()
    {
        var configuration = await LoadComposeConfiguration("docker-compose.yml");
        var services = configuration.RootElement.GetProperty("services");
        var app = services.GetProperty("app");
        var minio = services.GetProperty("minio");

        Assert.Equal("mediarelay-app", app.GetProperty("container_name").GetString());
        Assert.Equal("mediarelay-minio", minio.GetProperty("container_name").GetString());
        Assert.Equal("minio:9000", app.GetProperty("environment").GetProperty("MINIO_ENDPOINT").GetString());
        Assert.True(app.GetProperty("build").ValueKind != JsonValueKind.Undefined);
        Assert.True(minio.GetProperty("build").ValueKind != JsonValueKind.Undefined);
        Assert.Contains(app.GetProperty("depends_on").EnumerateObject(), dependency => dependency.Name == "minio");
    }

    [Fact]
    public async Task ProductionComposeUsesExternalMinioAndContainsOnlyApp()
    {
        var configuration = await LoadComposeConfiguration(Path.Combine("deploy", "docker-compose.prod.yml"));
        var services = configuration.RootElement.GetProperty("services");
        var app = services.GetProperty("app");

        Assert.Single(services.EnumerateObject());
        Assert.Equal("mediarelay-app", app.GetProperty("container_name").GetString());
        Assert.Equal("filestorage-minio:9000", app.GetProperty("environment").GetProperty("MINIO_ENDPOINT").GetString());
        Assert.Equal("ghcr.io/vitorhugo-dotnet/media-relay:sha-0123456789abcdef0123456789abcdef01234567", app.GetProperty("image").GetString());
        Assert.False(app.TryGetProperty("build", out _));
        Assert.False(app.TryGetProperty("depends_on", out _));
    }

    private static async Task<JsonDocument> LoadComposeConfiguration(string composeFile)
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
        process.StartInfo.ArgumentList.Add(Path.Combine(repositoryRoot, composeFile));
        process.StartInfo.ArgumentList.Add("config");
        process.StartInfo.ArgumentList.Add("--format");
        process.StartInfo.ArgumentList.Add("json");
        SetComposeTestEnvironment(process.StartInfo);

        process.Start();
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        Assert.True(process.ExitCode == 0, $"docker compose config failed: {stderr}");
        return JsonDocument.Parse(stdout);
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
        startInfo.Environment["MINIO_ENDPOINT"] = "filestorage-minio:9000";
        startInfo.Environment["MINIO_ROOT_USER"] = "test-minio-user";
        startInfo.Environment["MINIO_ROOT_PASSWORD"] = "test-minio-password-long-enough";
        startInfo.Environment["MINIO_BUCKET"] = "media";
        startInfo.Environment["MINIO_PUBLIC_ENDPOINT"] = "media.example.test";
        startInfo.Environment["MINIO_ACCESS_KEY"] = "test-app-user";
        startInfo.Environment["MINIO_SECRET_KEY"] = "test-app-password-long-enough";
        startInfo.Environment["DISCORD_ENABLED"] = "false";
        startInfo.Environment["DISCORD_TOKEN"] = "test-discord-token";
        startInfo.Environment.Remove("DISCORD_APPLICATION_ID");
        startInfo.Environment["DISCORD_ALLOWED_GUILD_IDS"] = "";
        startInfo.Environment["PUBLIC_MEDIA_BASE_URL"] = "https://media.example.test";
        startInfo.Environment["PUBLIC_APP_BASE_URL"] = "https://upload.example.test";
        startInfo.Environment["UPLOAD_API_KEY"] = "test-upload-key";
    }
}
