using System.Text.RegularExpressions;

namespace Rukbat.Tests
{
    public class TypeEmissionTests : TestBase
    {
        [Trait("Category", "Unit")]
        [Theory]
        [InlineData("", "int", "long")]
        [InlineData("", "float", "double")]
        [InlineData("", "bool", "bool")]
        [InlineData("", "string", "string")]
        [InlineData("", "int?", "object")]
        [InlineData("", "int[]", "System.Collections.Generic.List[object]")]
        [InlineData("", "int[]?", "object")]
        [InlineData("", "dict[string, int]", "hashtable")]
        [InlineData("", "dict[string, int]?", "object")]
        [InlineData("struct Item { value: int }", "Item", "hashtable")]
        [InlineData("struct Item { value: int }", "Item?", "object")]
        [InlineData("import encoding", "encoding.Encoding", "string")]
        [InlineData("import hash", "hash.Algorithm", "string")]
        [InlineData("import log", "log.Level", "string")]
        [InlineData("import fs", "fs.Lines", "object")]
        [InlineData("import re", "re.Pattern", "object")]
        [InlineData("import http", "http.Method", "string")]
        [InlineData("import http", "http.Body", "RukbatHttpBody")]
        [InlineData("import http", "http.Body?", "object")]
        [InlineData("import datetime", "datetime.Timestamp", "RukbatTimestamp")]
        [InlineData("import datetime", "datetime.Date", "RukbatDate")]
        [InlineData("import datetime", "datetime.Duration", "RukbatDuration")]
        [InlineData("import datetime", "datetime.Zone", "RukbatZone")]
        [InlineData("import json", "json.Value", "RukbatJsonValue")]
        [InlineData("import json", "json.Value?", "object")]
        [InlineData("import csv", "csv.Row", "RukbatCsvRow")]
        [InlineData("import cli", "cli.Result", "object")]
        public void EmitsParameterStorageType(string declarations, string type, string storageType)
        {
            var output = Success($"{declarations}\nfn accept(value: {type}): void {{}}\n");
            Assert.Matches(@"param\(\[" + Regex.Escape(storageType) + @"\]\$[^,)]+\)", output);
        }

        [Trait("Category", "Unit")]
        [Theory]
        [InlineData("import fs", "fs.lines")]
        [InlineData("import encoding", "encoding.Pattern")]
        [InlineData("import http", "http.Timestamp")]
        [InlineData("import datetime", "datetime.Body")]
        [InlineData("", "encoding.Encoding")]
        public void ScalarNamesRequireTheirCanonicalImportedModule(string imports, string type)
        {
            var result = Compile($"{imports}\nfn accept(value: {type}): void {{}}\n");
            Assert.False(result.Success);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "RUK2004");
        }

        [Trait("Category", "Unit")]
        [Fact]
        public void LocalStructDoesNotAcquireStandardScalarIdentity()
        {
            var output = Success("import encoding\nstruct Encoding { value: int }\n" +
                "fn local(value: Encoding): void {}\nfn standard(value: encoding.Encoding): void {}\n");
            Assert.Contains("param([hashtable]", output);
            Assert.Contains("param([string]", output);
        }
    }
}
