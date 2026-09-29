using System.Diagnostics;

namespace MediaRelay.Tests.Deployment;

public sealed class DeployScriptTests
{
    [Fact]
    public async Task UsesShaImageFromEnvFileAndRunsComposeWithoutPrintingCredentials()
    {
        const string uploadKey = "private-upload-key-for-test";
        var fixture = CreateFixture($"IMAGE=ghcr.io/vitorhugo-dotnet/media-relay:sha-{new string('a', 40)}\n" + ValidEnvironment(uploadKey));
        try
        {
            var result = await RunScript(fixture);

            Assert.Equal(0, result.ExitCode);
            Assert.All(File.ReadAllLines(fixture.DockerLog), call => Assert.StartsWith("compose ", call, StringComparison.Ordinal));
            Assert.Contains("sha-" + new string('a', 40), result.StandardOutput, StringComparison.Ordinal);
            Assert.DoesNotContain(uploadKey, result.StandardOutput + result.StandardError, StringComparison.Ordinal);
            var dockerCalls = File.ReadAllLines(fixture.DockerLog);
            Assert.Contains(dockerCalls, call => call.EndsWith(" pull app", StringComparison.Ordinal));
            Assert.Contains(dockerCalls, call => call.EndsWith(" up -d app", StringComparison.Ordinal));
            Assert.DoesNotContain(dockerCalls, call => call.Contains("minio", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(dockerCalls, call => call.Contains("--remove-orphans", StringComparison.Ordinal));
            Assert.Equal(3, dockerCalls.Length);
        }
        finally
        {
            Directory.Delete(fixture.Root, recursive: true);
        }
    }

    [Fact]
    public async Task ProcessEnvironmentShaImageOverridesImageFromEnvFile()
    {
        var image = $"ghcr.io/vitorhugo-dotnet/media-relay:sha-{new string('c', 40)}";
        var fixture = CreateFixture("IMAGE=media-relay:local\n" + ValidEnvironment("private-upload-key-for-test"));
        try
        {
            var result = await RunScript(fixture, image);

            Assert.Equal(0, result.ExitCode);
            Assert.Contains(image, result.StandardOutput, StringComparison.Ordinal);
            Assert.Equal(3, File.ReadAllLines(fixture.DockerLog).Length);
        }
        finally
        {
            Directory.Delete(fixture.Root, recursive: true);
        }
    }

    [Theory]
    [InlineData("bad-image")]
    [InlineData("ghcr.io/vitorhugo-dotnet/media-relay:latest")]
    public async Task RejectsMalformedProcessImageBeforeCallingCompose(string image)
    {
        var fixture = CreateFixture($"IMAGE=ghcr.io/vitorhugo-dotnet/media-relay:sha-{new string('d', 40)}\n" + ValidEnvironment("private-upload-key-for-test"));
        try
        {
            var result = await RunScript(fixture, image);

            Assert.Equal(2, result.ExitCode);
            Assert.Contains("IMAGE", result.StandardError, StringComparison.Ordinal);
            Assert.DoesNotContain(image, result.StandardError, StringComparison.Ordinal);
            Assert.False(File.Exists(fixture.DockerLog));
        }
        finally
        {
            Directory.Delete(fixture.Root, recursive: true);
        }
    }

    [Theory]
    [InlineData("MINIO_ROOT_USER", "   ", false)]
    [InlineData("MINIO_ENDPOINT", "   ", false)]
    [InlineData("MINIO_ROOT_PASSWORD", "replace-with-a-long-password", false)]
    [InlineData("UPLOAD_API_KEY", "replace-with-random-upload-api-key", false)]
    [InlineData("DISCORD_TOKEN", "   ", true)]
    [InlineData("DISCORD_APPLICATION_ID", "replace-with-discord-app-id", true)]
    public async Task RejectsBlankOrExampleRequiredValuesWithoutPrintingThem(string key, string value, bool discordEnabled)
    {
        var image = $"ghcr.io/vitorhugo-dotnet/media-relay:sha-{new string('e', 40)}";
        var fixture = CreateFixture($"IMAGE={image}\n" + ValidEnvironment("private-upload-key-for-test", discordEnabled, key, value));
        try
        {
            var result = await RunScript(fixture);

            Assert.Equal(2, result.ExitCode);
            Assert.Contains(key, result.StandardError, StringComparison.Ordinal);
            if (!string.IsNullOrEmpty(value))
                Assert.DoesNotContain(value, result.StandardError, StringComparison.Ordinal);
            Assert.False(File.Exists(fixture.DockerLog));
        }
        finally
        {
            Directory.Delete(fixture.Root, recursive: true);
        }
    }

    private static Fixture CreateFixture(string environment)
    {
        var root = Path.Combine(Path.GetTempPath(), "media-relay-deploy-test-" + Guid.NewGuid().ToString("N"));
        var deployDirectory = Directory.CreateDirectory(Path.Combine(root, "deploy")).FullName;
        File.Copy(Path.Combine(FindRepositoryRoot(), "deploy", "deploy.sh"), Path.Combine(deployDirectory, "deploy.sh"));
        File.WriteAllText(Path.Combine(deployDirectory, "docker-compose.prod.yml"), "services: {}\n");
        File.WriteAllText(Path.Combine(root, ".env"), environment);
        return new Fixture(root, Path.Combine(root, "docker-calls.log"));
    }

    private static async Task<ProcessResult> RunScript(Fixture fixture, string? image = null)
    {
        var startInfo = new ProcessStartInfo(FindBashExecutable())
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add("docker() { printf '%s\\n' \"$*\" >> \"$DOCKER_CALL_LOG\"; }; export -f docker; source \"$1\"");
        startInfo.ArgumentList.Add("deploy-test");
        startInfo.ArgumentList.Add(Path.Combine(fixture.Root, "deploy", "deploy.sh"));
        if (image is null)
            startInfo.Environment.Remove("IMAGE");
        else
            startInfo.Environment["IMAGE"] = image;
        startInfo.Environment["DOCKER_CALL_LOG"] = fixture.DockerLog;
        using var process = Process.Start(startInfo)!;
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return new ProcessResult(process.ExitCode, stdout, stderr);
    }

    private static string FindBashExecutable()
    {
        if (!OperatingSystem.IsWindows())
            return "bash";

        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Git", "bin", "bash.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Git", "bin", "bash.exe")
        };
        return candidates.FirstOrDefault(File.Exists) ?? "bash.exe";
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

    private static string ValidEnvironment(string uploadKey, bool discordEnabled = false, string? overrideKey = null, string? overrideValue = null)
    {
        var values = new Dictionary<string, string>
        {
            ["MINIO_ENDPOINT"] = "filestorage-minio:9000",
            ["MINIO_ROOT_USER"] = "admin-user",
            ["MINIO_ROOT_PASSWORD"] = "strong-password-not-printed",
            ["PUBLIC_APP_BASE_URL"] = "https://upload.example.test",
            ["PUBLIC_MEDIA_BASE_URL"] = "https://media.example.test",
            ["MINIO_PUBLIC_ENDPOINT"] = "media.example.test",
            ["UPLOAD_API_KEY"] = uploadKey,
            ["DISCORD_ENABLED"] = discordEnabled ? "true" : "false",
            ["DISCORD_TOKEN"] = "private-discord-token-for-test",
            ["DISCORD_APPLICATION_ID"] = "123456789012345678"
        };
        if (overrideKey is not null)
            values[overrideKey] = overrideValue!;
        return string.Join('\n', values.Select(pair => $"{pair.Key}={pair.Value}")) + "\n";
    }

    private sealed record Fixture(string Root, string DockerLog);
    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
}
