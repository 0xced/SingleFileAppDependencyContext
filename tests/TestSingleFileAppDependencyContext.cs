using System;
using System.Text;
using System.Threading.Tasks;
using CliWrap;
using FluentAssertions;
using Xunit;

namespace SingleFileAppDependencyContext.Tests;

public class TestSingleFileAppDependencyContext : IClassFixture<SingleFileAppFixture>
{
    private readonly SingleFileAppFixture _fixture;

    public TestSingleFileAppDependencyContext(SingleFileAppFixture fixture) => _fixture = fixture;

    [Theory]
    [CombinatorialData]
    public async Task SingleFileAppDependencyContext(
        [CombinatorialValues("FindBundleSignature", "CaptureAppHostLogs", "FindBundleHeaderOffsetSymbol")] string method,
        [CombinatorialValues("netcoreapp3.1", "net5.0", "net6.0")] string framework,
        bool selfContained)
    {
        var appPath = _fixture.GetAppPath(framework, selfContained);

        var outputBuilder = new StringBuilder();
        var errorBuilder = new StringBuilder();
        var app = Cli.Wrap(appPath)
            .WithValidation(CommandResultValidation.None)
            .WithArguments(method)
            .WithStandardOutputPipe(PipeTarget.ToStringBuilder(outputBuilder))
            .WithStandardErrorPipe(PipeTarget.ToStringBuilder(errorBuilder));

        var result = await app.ExecuteAsync();
        var output = outputBuilder.ToString();
        var error = errorBuilder.ToString();

        result.ExitCode.Should().Be(0, $"❌ {app}{Environment.NewLine}=== Output ==={Environment.NewLine}{output}{Environment.NewLine}=== Error ==={Environment.NewLine}{error}");

        error.Should().BeEmpty();
        output.Should().NotBeEmpty();

        if (framework == "netcoreapp3.1")
        {
            output.Should().StartWith("=== Detected non single-file app => using DependencyContext.Default ===");
        }
        else
        {
            output.Should().StartWith($"=== Using {method} ===");
        }
    }
}