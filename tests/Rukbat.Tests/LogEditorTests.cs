using System.Text.Json;

namespace Rukbat.Tests
{
    public sealed partial class LanguageServerTests
    {
        [Fact]
        public void LogCompletionIncludesSixOperationsFourConstantsAndTwoTypes()
        {
            using var project = new Project();
            var uri = UriFor(project);
            var result = Result(EditorSession(project, Open(uri, "import log\nlog."), Feature(uri, "completion", 1, 4)));
            var items = result.EnumerateArray().ToArray();
            foreach (var name in new[] { "create", "debug", "info", "warn", "error", "enabled", "level_debug", "level_info", "level_warn", "level_error", "Level", "Logger" })
            {
                Assert.Single(items.Where(item => item.GetProperty("label").GetString() == name));
            }

            foreach (var item in items.Where(item => item.GetProperty("kind").GetInt32() == 3))
            {
                Assert.NotEmpty(item.GetProperty("documentation").GetProperty("value").GetString()!);
            }

            Assert.DoesNotContain(items, item => item.GetProperty("label").GetString() is "trace" or "fatal" or "warning" or "write");
        }

        [Fact]
        public void LogHoverAndConfigurationFieldsUseCheckedTypes()
        {
            using var project = new Project();
            var uri = UriFor(project);
            const string source = "import log\nlogger: log.Logger = log.create()\nlevel: log.Level = log.level_info\nlog.info(logger, 'message')\nlogger.stderr_level = null\n";
            var messages = EditorSession(project, Open(uri, source), Feature(uri, "hover", 1, 13, "loggerType"),
                Feature(uri, "hover", 2, 12, "levelType"), Feature(uri, "hover", 2, 25, "constant"),
                Feature(uri, "hover", 3, 5, "function"), Feature(uri, "hover", 4, 10, "field"));
            Assert.Contains("struct log.Logger", Result(messages, "loggerType").ToString());
            Assert.Contains("log.Level", Result(messages, "levelType").ToString());
            Assert.Contains("log.level_info: log.Level", Result(messages, "constant").ToString());
            Assert.Contains("Read-only", Result(messages, "constant").ToString());
            Assert.Contains("log.info(logger: log.Logger, message: string): void", Result(messages, "function").ToString());
            Assert.Contains("stderr_level: log.Level?", Result(messages, "field").ToString());
            Assert.Empty(LastDiagnostics(messages, uri).GetProperty("diagnostics").EnumerateArray());

            var completions = Result(EditorSession(project, Open(uri, source + "logger."), Feature(uri, "completion", 5, 7)));
            foreach (var detail in new[] { "stderr_level: log.Level?", "file: string?", "file_level: log.Level" })
            {
                Assert.Contains(completions.EnumerateArray(), item => item.GetProperty("detail").GetString() == detail);
            }
        }

        [Theory]
        [InlineData("log.create(", "log.create(): log.Logger", 0)]
        [InlineData("log.debug(logger, ", "log.debug(logger: log.Logger, message: string): void", 1)]
        [InlineData("log.info(logger, ", "log.info(logger: log.Logger, message: string): void", 1)]
        [InlineData("log.warn(logger, ", "log.warn(logger: log.Logger, message: string): void", 1)]
        [InlineData("log.error(logger, ", "log.error(logger: log.Logger, message: string): void", 1)]
        [InlineData("log.enabled(logger, ", "log.enabled(logger: log.Logger, level: log.Level): bool", 1)]
        public void LogSignatureHelpUsesCanonicalDocumentation(string call, string signature, int activeParameter)
        {
            using var project = new Project();
            var uri = UriFor(project);
            var source = "import log\nlogger := log.create()\n" + call;
            var result = Result(EditorSession(project, Open(uri, source), Feature(uri, "signatureHelp", 2, call.Length)));
            Assert.NotEqual(JsonValueKind.Null, result.ValueKind);
            Assert.Equal(signature, result.GetProperty("signatures")[0].GetProperty("label").GetString());
            if (call == "log.create(")
            {
                Assert.False(result.TryGetProperty("activeParameter", out _));
            }
            else
            {
                Assert.Equal(activeParameter, result.GetProperty("activeParameter").GetInt32());
            }
        }
    }

    [Trait("Category", "Unit")]
    public sealed class LogDebugMetadataTests : TestBase
    {
        [Fact]
        public void DebuggerReceivesOrdinaryLoggerFieldsAndValidatedLevelLabels()
        {
            using var project = new Project();
            project.Write("main.rukbat", "import log\nlogger := log.create()\nlevel := log.level_debug\nlog.enabled(logger, level)\n");
            var compiled = new Compiler().CompileFile(project.PathFor("main.rukbat"), debug: true);
            Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));
            var map = compiled.DebugMap!;
            var logger = Assert.Single(map.Types.Values.Where(value => value.Name == "log.Logger"));
            Assert.Equal(new[] { "stderr_level", "file", "file_level" }, logger.Fields!.Select(field => field.Name));
            var level = Assert.Single(map.Types.Values.Where(value => value.Name == "log.Level"));
            Assert.Null(level.Fields);
            Assert.Equal("log.level_debug", level.DisplayValues!["debug"]);
            Assert.Equal("log.level_info", level.DisplayValues["info"]);
            Assert.Equal("log.level_warn", level.DisplayValues["warn"]);
            Assert.Equal("log.level_error", level.DisplayValues["error"]);
            Assert.Equal(4, level.DisplayValues.Count);
            var threshold = map.Types[logger.Fields![0].Type];
            Assert.Equal("log.Level?", threshold.Name);
            Assert.Same(level, map.Types[threshold.Nullable!]);
        }
    }
}
