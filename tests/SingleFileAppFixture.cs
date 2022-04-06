using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using CliWrap;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace SingleFileAppDependencyContext.Tests;

public class SingleFileAppFixture : IAsyncLifetime
{
    private readonly IMessageSink _messageSink;
    private readonly string _rootPath;
    private IEnumerable<string> _publishDirectories;

    public SingleFileAppFixture(IMessageSink messageSink)
    {
        _messageSink = messageSink;
        _rootPath = Path.Combine(Path.GetTempPath(), nameof(SingleFileAppDependencyContext), Path.GetFileName(Path.GetTempFileName()));
        _publishDirectories = Enumerable.Empty<string>();
        messageSink.OnMessage(new DiagnosticMessage($"📁 {_rootPath}"));
    }

    public string GetAppPath(string framework, bool selfContained)
    {
        var publishDirectory = GetPublishDirectory(framework, selfContained);
        var exeName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? $"{nameof(SingleFileAppDependencyContext)}.exe" : nameof(SingleFileAppDependencyContext);
        return Path.Combine(publishDirectory, exeName);
    }

    async Task IAsyncLifetime.InitializeAsync()
    {
        var parameters =
            from framework in new[] { "netcoreapp3.1", "net5.0", "net6.0" }
            from selfContained in new[] { true, false }
            select (framework, selfContained);

        _publishDirectories = parameters.Select(e => GetPublishDirectory(e.framework, e.selfContained));
        var restore = Cli.Wrap("dotnet").WithArguments(new[] { "restore", "--nologo" });
        await ExecuteAsync(restore);

#if false
        // Completely parallel => fails with
        // > /usr/local/share/dotnet/sdk/6.0.201/Sdks/Microsoft.NET.Sdk/targets/Microsoft.NET.Publish.targets(898,5): error MSB4018:
        // > System.IO.IOException: The process cannot access the file 'SingleFileAppDependencyContext/src/bin/Release/net6.0/osx-x64/SingleFileAppDependencyContext.runtimeconfig.json' because it is being used by another process. [SingleFileAppDependencyContext/src/SingleFileAppDependencyContext.csproj]
        await Task.WhenAll(_publishDirectories.Select(PublishAsync));
#elif true
        // Parallelize all target frameworks
        foreach (var group in _publishDirectories.GroupBy(e => e.Split(Path.DirectorySeparatorChar).Last()))
        {
            await Task.WhenAll(group.Select(PublishAsync));
        }
#else
        // No parallelization at all (very slow)
        foreach (var publishDirectory in _publishDirectories)
        {
            await PublishAsync(publishDirectory);
        }
#endif
    }

    Task IAsyncLifetime.DisposeAsync()
    {
        Directory.Delete(_rootPath, recursive: true);
        return Task.CompletedTask;
    }

    private string GetPublishDirectory(string framework, bool selfContained)
    {
        return Path.Combine(_rootPath, framework, selfContained ? "self-contained" : "no-self-contained");
    }

    private static string GetSrcDirectory([CallerFilePath] string path = "")
    {
        return Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(path)!)!, "src");
    }

    private async Task PublishAsync(string publishDirectory)
    {
        var pathComponents = publishDirectory.Split(Path.DirectorySeparatorChar);

        var publish = Cli.Wrap("dotnet")
            .WithArguments(new[]
            {
                "publish",
                "--nologo",
                "--no-restore",
                "-c", "Release",
                $"--{pathComponents.Last()}",
                "-f", pathComponents.TakeLast(2).First(),
                "-o", publishDirectory
            });

        await ExecuteAsync(publish);
    }

    private async Task ExecuteAsync(Command command)
    {
        var workingDirectory = GetSrcDirectory();
        _messageSink.OnMessage(new DiagnosticMessage($"📁 {workingDirectory} 🛠 {command}"));

        var outputBuilder = new StringBuilder();
        var errorBuilder = new StringBuilder();
        var result = await command
            .WithValidation(CommandResultValidation.None)
            .WithStandardOutputPipe(PipeTarget.ToStringBuilder(outputBuilder))
            .WithStandardErrorPipe(PipeTarget.ToStringBuilder(errorBuilder))
            .WithWorkingDirectory(workingDirectory)
            .ExecuteAsync();

        var output = outputBuilder.ToString();
        var error = errorBuilder.ToString();
        var message = new StringBuilder(command.ToString());
        message.AppendLine();
        if (output.Length > 0)
        {
            if (error.Length > 0)
            {
                message.AppendLine("=== Output ===");
            }
            message.AppendLine(output);
        }
        if (error.Length > 0)
        {
            if (output.Length > 0)
            {
                message.AppendLine("=== Error ===");
            }
            message.AppendLine(error);
        }
        result.ExitCode.Should().Be(0, message.ToString());

        _messageSink.OnMessage(new DiagnosticMessage($"📁 {workingDirectory} ✅ {command}"));
    }
}