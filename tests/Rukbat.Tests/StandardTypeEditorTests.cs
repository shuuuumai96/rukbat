namespace Rukbat.Tests
{
    public sealed partial class LanguageServerTests
    {
        [Theory]
        [InlineData("fs", "Lines")]
        [InlineData("re", "Pattern")]
        [InlineData("encoding", "Encoding")]
        [InlineData("hash", "Algorithm")]
        [InlineData("log", "Level")]
        [InlineData("http", "Method")]
        [InlineData("http", "Body")]
        [InlineData("datetime", "Timestamp")]
        [InlineData("datetime", "Date")]
        [InlineData("datetime", "Duration")]
        [InlineData("datetime", "Zone")]
        public void StandardScalarTypesHaveConsistentCompletionAndHover(string module, string name)
        {
            using var project = new Project();
            var uri = UriFor(project);
            var source = $"import {module}\n{module}.\n";
            var completion = Result(EditorSession(project, Open(uri, source),
                Feature(uri, "completion", 1, module.Length + 1)));
            var item = Assert.Single(completion.EnumerateArray().Where(item =>
                item.GetProperty("label").GetString() == name));
            Assert.Equal(module + "." + name, item.GetProperty("detail").GetString());

            source = $"import {module}\nfn accept(value: {module}.{name}?[]): void {{}}\n";
            var hover = Result(EditorSession(project, Open(uri, source),
                Feature(uri, "hover", 1, "fn accept(value: ".Length + module.Length + 1)));
            Assert.Contains(module + "." + name, hover.GetProperty("contents").GetProperty("value").GetString());
        }
    }
}
