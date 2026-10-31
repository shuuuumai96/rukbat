namespace Rukbat.Tests
{
    public sealed class NullableAssertionTests : TestBase
    {
        [Trait("Category", "Runtime")]
        [Trait("Category", "CrossVersion")]
        [Theory]
        [InlineData("powershell", "datetime.Timestamp", "datetime.parse('2000-01-01T00:00:00Z')")]
        [InlineData("pwsh", "datetime.Timestamp", "datetime.parse('2000-01-01T00:00:00Z')")]
        [InlineData("powershell", "datetime.Date", "datetime.parse_date('2000-01-01')")]
        [InlineData("pwsh", "datetime.Date", "datetime.parse_date('2000-01-01')")]
        [InlineData("powershell", "datetime.Duration", "datetime.seconds(1)")]
        [InlineData("pwsh", "datetime.Duration", "datetime.seconds(1)")]
        [InlineData("powershell", "datetime.Zone", "datetime.utc")]
        [InlineData("pwsh", "datetime.Zone", "datetime.utc")]
        public async Task DateTimeNullAssertionsMatchOrdinaryComparisons(string shell, string type, string initial)
        {
            var source = $$"""
                import datetime
                import test
                import text

                calls := 0

                fn next(): {{type}}? {
                    calls = calls + 1
                    return {{initial}}
                }

                value: {{type}}? = {{initial}}
                test.assert((value != null) == true)
                test.assert(value != null)
                test.assert(null != value)

                failures := 0

                try {
                    test.assert(value == null)
                } catch err {
                    test.assert(text.starts_with(err.message, "Assertion failed: value == null"))
                    test.assert(text.contains(err.message, "right = <null>"))
                    failures = failures + 1
                }

                try {
                    test.assert(null == value)
                } catch err {
                    test.assert(text.starts_with(err.message, "Assertion failed: null == value"))
                    test.assert(text.contains(err.message, "left = <null>"))
                    failures = failures + 1
                }

                test.assert(failures == 2)
                value = null
                test.assert(value == null)
                test.assert(null == value)
                test.assert((value == null) == true)
                test.assert(next() != null)
                test.assert(null != next())
                test.assert(calls == 2)
                print("passed")
                """;
            var output = Success(source);
            using var project = new Project();
            var result = shell == "powershell"
                ? await project.RunOutputWithWindowsPowerShell(output)
                : await project.RunOutput(output);
            Assert.True(result.ExitCode == 0, result.Error);
            Assert.Empty(result.Error);
            Assert.Equal("passed\n", result.Output);
        }
    }
}
