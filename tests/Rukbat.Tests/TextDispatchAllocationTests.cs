using System.Globalization;
using System.Text;

namespace Rukbat.Tests;

public class TextDispatchAllocationTests : TestBase
{
    private const int LargeInputLength = 1024 * 1024 - 1;
    // Allow normal/debug dispatch and required result/boundary storage, while
    // rejecting the several additional full-string copies made by host binding.
    private const long MaximumFixedDispatchAllocationBytes = 1024 * 1024;
    private const long MaximumConversionAllocationBytes = 3 * 1024 * 1024;
    private const long MaximumBoundaryAllocationBytes = 9 * 1024 * 1024;

    [Trait("Category", "Runtime")]
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GeneratedTextCallsAvoidCopyingLargeArguments(bool debug)
    {
        var cases = new[]
        {
            new AllocationCase("length", "text.length(lower)", $"actual == {LargeInputLength}", MaximumFixedDispatchAllocationBytes),
            new AllocationCase("slice", "text.slice(lower, 0, 1)", "actual == 'x'", MaximumBoundaryAllocationBytes),
            new AllocationCase("index_of_search", "text.index_of('', lower)", "actual == null", MaximumFixedDispatchAllocationBytes),
            new AllocationCase("index_of_value", "text.index_of(lower, 'x')", "actual != null", MaximumBoundaryAllocationBytes),
            new AllocationCase("upper", "text.upper(lower)", "actual == upper", MaximumConversionAllocationBytes),
            new AllocationCase("lower", "text.lower(upper)", "actual == lower", MaximumConversionAllocationBytes)
        };
        using var project = new Project();
        var source = new StringBuilder($$"""
            import text; import test
            lower := powershell<string>('Invoke-Expression', "'x' * {{LargeInputLength}}")
            upper := powershell<string>('Invoke-Expression', "'X' * {{LargeInputLength}}")
            """);
        foreach (var allocationCase in cases)
        {
            // Warm the same generated callsite. Counters use short commands, so the
            // measurement harness never passes a large string into a native method.
            source.AppendLine($$"""

                if true {
                    attempt := 0
                    allocated := 0
                    while attempt < 3 {
                        before := powershell<int>('Invoke-Expression', '[GC]::GetAllocatedBytesForCurrentThread()')
                        actual := {{allocationCase.Expression}}
                        after := powershell<int>('Invoke-Expression', '[GC]::GetAllocatedBytesForCurrentThread()')
                        allocated = after - before
                        test.assert({{allocationCase.Condition}})
                        attempt = attempt + 1
                    }
                    print('{{allocationCase.Name}}')
                    print(allocated)
                }
                """);
        }
        project.Write("main.rukbat", source.ToString());
        var compiled = new Compiler().CompileFile(project.PathFor("main.rukbat"), debug);
        Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));

        var result = await project.RunOutput(compiled.Output!);

        Assert.True(result.ExitCode == 0, result.Error);
        Assert.Empty(result.Error);
        var lines = result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(cases.Length * 2, lines.Length);
        Assert.All(cases.Select((allocationCase, index) => (allocationCase, index)), item =>
        {
            Assert.Equal(item.allocationCase.Name, lines[item.index * 2]);
            var allocated = long.Parse(lines[item.index * 2 + 1], CultureInfo.InvariantCulture);
            Assert.True(allocated >= 0 && allocated <= item.allocationCase.MaximumBytes,
                $"{item.allocationCase.Name} allocated {allocated} bytes; budget is {item.allocationCase.MaximumBytes} bytes (debug: {debug}).");
        });
    }

    private sealed record AllocationCase(string Name, string Expression, string Condition, long MaximumBytes);
}
