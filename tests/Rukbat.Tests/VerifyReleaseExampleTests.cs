using System.Security.Cryptography;

namespace Rukbat.Tests
{
    public class VerifyReleaseExampleTests : TestBase
    {
        public static IEnumerable<object[]> Cases()
        {
            foreach (var shell in new[] { "powershell", "pwsh" })
            {
                foreach (var scenario in new[] { "match", "mismatch", "missing-file", "empty", "invalid-digest", "invalid-separator", "parent-path", "missing-manifest", "no-arguments" })
                {
                    yield return new object[] { shell, scenario };
                }
            }
        }

        [Trait("Category", "CrossVersion")]
        [Trait("Category", "Runtime")]
        [Theory]
        [MemberData(nameof(Cases))]
        public async Task VerifiesManifestAndReportsFailures(string shell, string scenario)
        {
            using var project = new Project();
            var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "examples", "verify-release", "main.rukbat"));
            var output = Success(source);
            const string filename = "package [one].zip";
            var bytes = new byte[] { 0, 255, 13, 10, 128 };
            File.WriteAllBytes(project.PathFor(filename), bytes);
            var digest = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            var manifest = scenario switch
            {
                "mismatch" => new string('0', 64) + "  " + filename,
                "missing-file" => digest + "  missing.zip",
                "empty" => "\r\n\r\n",
                "invalid-digest" => "invalid  " + filename,
                "invalid-separator" => digest + " " + filename,
                "parent-path" => digest + "  ../outside.zip",
                _ => digest + "  " + filename + "\r\n\r\n"
            };
            if (scenario != "missing-manifest")
            {
                project.Write("SHA256SUMS", manifest);
            }

            var arguments = scenario == "no-arguments" ? Array.Empty<string>() : new[] { project.DirectoryPath };

            var result = shell == "powershell"
                ? await project.RunOutputWithWindowsPowerShell(output, arguments)
                : await project.RunOutput(output, arguments);

            var expectedExitCode = scenario switch { "match" => 0, "mismatch" => 1, _ => 2 };
            Assert.True(result.ExitCode == expectedExitCode, result.Output + result.Error);
            Assert.Equal(bytes, File.ReadAllBytes(project.PathFor(filename)));
            if (scenario == "match")
            {
                Assert.Contains("OK    " + filename, result.Output);
                Assert.Contains("All listed files match SHA256SUMS.", result.Output);
                Assert.Equal("", result.Error);
            }
            else
            {
                Assert.NotEmpty(result.Error);
                Assert.DoesNotContain("All listed files match", result.Output);
            }
        }
    }
}
