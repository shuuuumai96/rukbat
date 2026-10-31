namespace Rukbat.Tests
{
    public class StandardConstantTests : TestBase
    {
        [Trait("Category", "Unit")]
        [Theory]
        [InlineData("log", "logging level")]
        [InlineData("http", "HTTP constant")]
        [InlineData("encoding", "encoding value")]
        [InlineData("hash", "hash algorithm")]
        [InlineData("datetime", "datetime constant")]
        public void UnknownConstantsPreserveModuleSpecificDiagnostics(string module, string description)
        {
            var result = Compile($"import {module}\nvalue := {module}.missing\n");
            Assert.False(result.Success);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "RUK2004" &&
                diagnostic.Message == $"Unknown {description} 'missing'.");
        }

        [Trait("Category", "Unit")]
        [Theory]
        [InlineData("log", "level_info")]
        [InlineData("http", "method_get")]
        [InlineData("http", "status_200_ok")]
        [InlineData("encoding", "utf8")]
        [InlineData("hash", "sha256")]
        [InlineData("datetime", "utc")]
        public void ResolvedConstantsRemainReadOnly(string module, string name)
        {
            var result = Compile($"import {module}\n{module}.{name} = {module}.{name}\n");
            Assert.False(result.Success);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "RUK2010" &&
                diagnostic.Message == "Standard-library constants are read-only.");
        }
    }
}
