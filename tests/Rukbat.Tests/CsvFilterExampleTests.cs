using System.Text;

namespace Rukbat.Tests
{
    public class CsvFilterExampleTests : TestBase
    {
        private static string ExampleSource => File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory, "Fixtures", "examples", "csv-filter", "main.rukbat"));

        [Trait("Category", "Runtime")]
        [Trait("Category", "CrossVersion")]
        [Theory]
        [InlineData("powershell")]
        [InlineData("pwsh")]
        public async Task FiltersValidRowsAndRetainsOtherColumns(string shell)
        {
            using var project = new Project();
            const string input = "note,enabled,name,extra\r\nold,true,Taro,unknown\r\n\"first\r\nsecond\",false,Hanako,other\r\n";
            project.Write("input.csv", input);
            var output = Success(ExampleSource);
            var arguments = new[] { project.PathFor("input.csv"), project.PathFor("output.csv"), "utf-8" };

            var result = shell == "powershell"
                ? await project.RunOutputWithWindowsPowerShell(output, arguments)
                : await project.RunOutput(output, arguments);

            Assert.True(result.ExitCode == 0, result.Error);
            Assert.Equal("", result.Output);
            Assert.Equal("", result.Error);
            Assert.Equal(Encoding.UTF8.GetBytes("note,enabled,name,extra\r\nchecked,true,Taro,unknown\r\n"),
                File.ReadAllBytes(project.PathFor("output.csv")));
            Assert.Equal(input, File.ReadAllText(project.PathFor("input.csv")));
        }

        [Trait("Category", "Runtime")]
        [Trait("Category", "CrossVersion")]
        [Theory]
        [InlineData("powershell", "name,enabled,note\nTaro,maybe,old\n", 2)]
        [InlineData("pwsh", "name,enabled,note\nTaro,maybe,old\n", 2)]
        [InlineData("powershell", "name,enabled,note\nHanako,false,\"first\nsecond\"\nTaro,maybe,old\n", 3)]
        [InlineData("pwsh", "name,enabled,note\nHanako,false,\"first\nsecond\"\nTaro,maybe,old\n", 3)]
        public async Task ReportsInvalidCellContextBeforeReplacingOutput(string shell, string input, int recordNumber)
        {
            using var project = new Project();
            project.Write("input.csv", input);
            var previousOutput = Encoding.UTF8.GetBytes("Existing output must remain unchanged.\r\n");
            File.WriteAllBytes(project.PathFor("output.csv"), previousOutput);
            var output = Success(ExampleSource);
            var arguments = new[] { project.PathFor("input.csv"), project.PathFor("output.csv"), "utf-8" };

            var result = shell == "powershell"
                ? await project.RunOutputWithWindowsPowerShell(output, arguments)
                : await project.RunOutput(output, arguments);

            Assert.Equal(2, result.ExitCode);
            Assert.Equal("", result.Output);
            Assert.Contains($"CSV record {recordNumber}, column enabled: ", result.Error);
            Assert.Contains("text.parse_bool: ", result.Error);
            Assert.Contains("Input: \"maybe\"", result.Error);
            Assert.Equal(previousOutput, File.ReadAllBytes(project.PathFor("output.csv")));
            Assert.Equal(input, File.ReadAllText(project.PathFor("input.csv")));
        }
    }
}
