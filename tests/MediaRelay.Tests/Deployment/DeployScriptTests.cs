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
            Assert.Equal(3, File.ReadAllLines(fixture.DockerLog).Length);
        }
        finally
        {
            Directory.Delete(fixture.Root, recursive: true);
        }
    }

    [Theory]
    [InlineData("   ")]
    [InlineData("replace-with-random-upload-api-key")]
    public async Task RejectsBlankOrExampleRequiredValueWithoutPrintingIt(string value)
    {
        var fixture = CreateFixture($"IMAGE=ghcr.io/vitorhugo-dotnet/media-relay:sha-{new string('b', 40)}\n" + ValidEnvironment(value));
        try
        {
            var result = await RunScript(fixture);

            Assert.Equal(2, result.ExitCode);
            Assert.Contains("UPLOAD_API_KEY", result.StandardError, StringComparison.Ordinal);
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

    private static async Task<ProcessResult> RunScript(Fixture fixture)
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
        startInfo.Environment.Remove("IMAGE");
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

    private static string ValidEnvironment(string uploadKey) =>
        "MINIO_ROOT_USER=admin-user\n" +
        "MINIO_ROOT_PASSWORD=strong-password-not-printed\n" +
        "PUBLIC_APP_BASE_URL=https://upload.example.test\n" +
        "PUBLIC_MEDIA_BASE_URL=https://media.example.test\n" +
        "MINIO_PUBLIC_ENDPOINT=media.example.test\n" +
        $"UPLOAD_API_KEY={uploadKey}\n" +
        "DISCORD_ENABLED=false\n";

    private sealed record Fixture(string Root, string DockerLog);
    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
}
