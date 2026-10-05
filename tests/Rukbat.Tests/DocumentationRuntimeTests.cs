using Rukbat;

namespace Rukbat.Tests;

[Trait("Category", "Runtime")]
[Trait("Category", "PowerShell51")]
[Trait("Category", "CrossVersion")]
public sealed class DocumentationRuntimeTests : TestBase
{
    [Fact]
    [Trait("Smoke", "PowerShell51")]
    public async Task DocumentationDoesNotChangeStandaloneExecutionOnEitherHost()
    {
        using var project = new Project();
        project.Write("main.rukbat", "//! Counts values.\n/// Adds one.\nfn increment(value: int): int { return value + 1 }\n/// Input value.\nvalue := 2\nprint(increment(value))\n");
        Assert.Equal(0, Cli.Run(["doc", project.PathFor("main.rukbat")], TextWriter.Null, TextWriter.Null));
        var result = project.Compile();
        Assert.True(result.Success, string.Join("\n", result.Diagnostics));
        File.Delete(project.PathFor("main.rukbat"));
        foreach (var run in new[] { await project.RunOutputWithWindowsPowerShell(result.Output!), await project.RunOutput(result.Output!) })
        {
            Assert.Equal(0, run.ExitCode);
            Assert.Equal("3\n", run.Output);
            Assert.Equal("", run.Error);
        }
    }
}
