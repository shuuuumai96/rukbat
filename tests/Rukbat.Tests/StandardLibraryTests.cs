using System.Diagnostics;
using System.Text;

namespace Rukbat.Tests
{
    public class StandardLibraryTests : TestBase
    {
        [Trait("Category", "Unit")]
        [Fact]
        public void GeneratedScriptIncludesOnlyImportedStandardLibraryRuntimes()
        {
            var plain = Success("print('hello')");
            Assert.DoesNotContain("Unknown standard-library operation", plain);
            Assert.DoesNotContain("JSON value", plain);

            var json = Success("import json\nprint(json.stringify([1]))");
            Assert.Contains("JSON value", json);
            Assert.Contains("'json.get'", json);
            Assert.DoesNotContain("'fs.read_text'", json);
            Assert.DoesNotContain("'csv.parse'", json);

            var files = Success("import fs\nprint(fs.is_file('x'))");
            Assert.Contains("Unknown standard-library operation", files);
            Assert.DoesNotContain("JSON value", files);
            Assert.Contains("'fs.is_file'", files);
            Assert.Contains("'fs.list_files_recursive'", files);
            Assert.DoesNotContain("'env.get'", files);
            Assert.DoesNotContain("'process.run'", files);
        }

        [Trait("Category", "Runtime")]
        [Fact]
        public async Task FilesPathAndEnvironmentModulesUseTypedContracts()
        {
            using var project = new Project();
            var directory = project.DirectoryPath.Replace("\\", "\\\\");
            var variable = ("RUKBAT_TEST_" + Guid.NewGuid().ToString("N")).Replace("\\", "\\\\");
            project.Write("main.rukbat", $$"""
            import encoding; import fs
            import path
            import env
            target: string = path.join(['{{directory}}', 'data.txt'])
            fs.write_text(target, 'hello', encoding.utf8)
            print(fs.is_file(target))
            print(fs.read_text(target, encoding.utf8))
            print(path.filename(target))
            print(path.extension(target))
            names: string[] = fs.list_files('{{directory}}', '*.txt')
            print(names.length)
            env.set('{{variable}}', 'value')
            value: string? = env.get('{{variable}}')
            if value != null { print(value) }
            env.remove('{{variable}}')
            print(env.get('{{variable}}') == null)
            """);
            var result = await project.Run();
            Assert.Equal(0, result.ExitCode);
            Assert.Equal("True\nhello\ndata.txt\n.txt\n1\nvalue\nTrue", result.Output.TrimEnd('\n'));
            Assert.Equal("", result.Error);
            var bytes = File.ReadAllBytes(project.PathFor("data.txt"));
            Assert.False(bytes.Length >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf);
        }

        [Trait("Category", "CrossVersion")]
        [Trait("Category", "Runtime")]
        [Fact]
        public async Task EnvironmentRejectsEmptySetAndPreservesInheritedEmptyValue()
        {
            using var project = new Project();
            var variable = "RUKBAT_EMPTY_" + Guid.NewGuid().ToString("N");
            project.Write("main.rukbat", $$"""
            import env
            value: string? = env.get('{{variable}}')
            if value == null { print('missing') } else { print(value == '') }
            try { env.set('{{variable}}', '') } catch err { print(err.message) }
            env.remove('{{variable}}')
            print(env.get('{{variable}}') == null)
            """);
            var compiled = project.Compile();
            Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));

            var modern = await project.RunOutputWithEnvironment(compiled.Output!, "pwsh", variable, "");
            Assert.Equal(0, modern.ExitCode);
            Assert.Equal("True\nenv.set does not accept an empty value; use env.remove to delete '" + variable + "'.\nTrue", modern.Output.TrimEnd('\n'));
            Assert.Equal("", modern.Error);

            var windows = await project.RunOutputWithEnvironment(compiled.Output!, "powershell", variable, "");
            Assert.Equal(0, windows.ExitCode);
            Assert.Equal(modern.Output, windows.Output);
            Assert.Equal("", windows.Error);
        }

        [Trait("Category", "CrossVersion")]
        [Trait("Category", "Runtime")]
        [Fact]
        public async Task ProcessRunReturnsNonzeroExitAndSeparateOutput()
            => await AssertProcessRunReturnsNonzeroExitAndSeparateOutput(crossVersion: true);

        [Trait("Smoke", "PowerShell51")]
        [Trait("Category", "Runtime")]
        [Fact]
        public async Task ProcessRunReturnsNonzeroExitAndSeparateOutputOnWindowsPowerShell51()
            => await AssertProcessRunReturnsNonzeroExitAndSeparateOutput(crossVersion: false);

        [Trait("Category", "CrossVersion")]
        [Trait("Category", "Runtime")]
        [Fact]
        public async Task ProcessRunEnforcesTimeoutAndTerminatesRootOnBothHosts()
        {
            using var project = new Project();
            project.Write("slow.ps1", "param([string]$marker)\n[IO.File]::WriteAllText($marker, [string]$PID)\nStart-Sleep -Seconds 10");
            var fixture = project.PathFor("slow.ps1").Replace("\\", "\\\\");
            foreach (var host in new[] { "pwsh", "powershell" })
            {
                var marker = project.PathFor(host + ".pid");
                project.Write("main.rukbat", $$"""
                import encoding; import process
                try { process.run('powershell', ['-NoLogo', '-NoProfile', '-NonInteractive', '-File', '{{fixture}}', '{{marker.Replace("\\", "\\\\")}}'], process.options(3000, encoding.utf8)) }
                catch err { print(err.message) }
                """);
                var compiled = project.Compile();
                Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));
                var result = await RunOnHost(project, compiled.Output!, host);
                Assert.Equal(0, result.ExitCode);
                Assert.Contains("process timed out after 3000 ms", result.Output);
                var pid = int.Parse(File.ReadAllText(marker));
                Assert.Throws<ArgumentException>(() => System.Diagnostics.Process.GetProcessById(pid));
            }
        }

        [Trait("Category", "Runtime")]
        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(2147483648L)]
        public async Task ProcessRunRejectsOutOfRangeTimeoutBeforeLaunch(long timeout)
        {
            using var project = new Project();
            var marker = project.PathFor("started.txt").Replace("\\", "\\\\");
            project.Write("main.rukbat", $"import encoding; import process\ntry {{ process.run('powershell', ['-Command', '[IO.File]::WriteAllText(\"{marker}\", \"started\")'], process.options({timeout}, encoding.utf8)) }} catch err {{ print(err.message) }}");
            var result = await project.Run();
            Assert.Equal(0, result.ExitCode);
            Assert.Contains(timeout <= 0 ? "timeout must be greater than zero" : "timeout must not exceed", result.Output);
            Assert.False(File.Exists(project.PathFor("started.txt")));
        }

        [Trait("Category", "CrossVersion")]
        [Trait("Category", "Runtime")]
        [Fact]
        public async Task ProcessRunTimeoutDoesNotDeadlockWhileDrainingBothStreams()
        {
            using var project = new Project();
            project.Write("noisy.ps1", "$text = 'x' * 4096\nwhile ($true) { [Console]::Out.Write($text); [Console]::Error.Write($text) }");
            var fixture = project.PathFor("noisy.ps1").Replace("\\", "\\\\");
            project.Write("main.rukbat", $$"""
            import encoding; import process
            try { process.run('powershell', ['-NoLogo', '-NoProfile', '-NonInteractive', '-File', '{{fixture}}'], process.options(500, encoding.utf8)) }
            catch err { print(err.message) }
            """);
            var compiled = project.Compile();
            Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));
            foreach (var host in new[] { "pwsh", "powershell" })
            {
                var result = await RunOnHost(project, compiled.Output!, host);
                Assert.Equal(0, result.ExitCode);
                Assert.Contains("process timed out after 500 ms", result.Output);
            }
        }

        [Trait("Category", "CrossVersion")]
        [Trait("Category", "Runtime")]
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task ProcessRunTimeoutReturnsWhenDescendantRetainsOutputHandles(bool rootExitsEarly)
        {
            using var project = new Project();
            project.Write("descendant.ps1", "param([string]$marker)\n[IO.File]::WriteAllText($marker, [string]$PID)\nStart-Sleep -Seconds 30");
            project.Write("parent.ps1", "param([string]$childScript, [string]$marker)\n$arguments = @('-NoLogo', '-NoProfile', '-NonInteractive', '-File', ('\"' + $childScript + '\"'), ('\"' + $marker + '\"'))\n$child = Start-Process -FilePath 'powershell' -ArgumentList $arguments -NoNewWindow -PassThru\n" +
                (rootExitsEarly ? "exit 0" : "$child.WaitForExit()"));
            var parent = project.PathFor("parent.ps1").Replace("\\", "\\\\");
            var descendant = project.PathFor("descendant.ps1").Replace("\\", "\\\\");
            foreach (var host in new[] { "pwsh", "powershell" })
            {
                var marker = project.PathFor(host + "-descendant.pid");
                project.Write("main.rukbat", $$"""
                import encoding; import process
                try { process.run('powershell', ['-NoLogo', '-NoProfile', '-NonInteractive', '-File', '{{parent}}', '{{descendant}}', '{{marker.Replace("\\", "\\\\")}}'], process.options(3000, encoding.utf8)) }
                catch err { print(err.message) }
                """);
                var compiled = project.Compile();
                Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));

                project.Write("main.ps1", compiled.Output!);
                var info = new ProcessStartInfo(host)
                {
                    UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
                    CreateNoWindow = true
                };
                foreach (var argument in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-File", project.PathFor("main.ps1") })
                {
                    info.ArgumentList.Add(argument);
                }

                using var runner = Process.Start(info)!;
                var stdout = runner.StandardOutput.ReadToEndAsync();
                var stderr = runner.StandardError.ReadToEndAsync();
                try
                {
                    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                    await runner.WaitForExitAsync(deadline.Token);
                    Assert.Equal(0, runner.ExitCode);
                    Assert.True(File.Exists(marker), "The descendant must have started and inherited the output handles.");
                }
                finally
                {
                    if (!runner.HasExited)
                    {
                        runner.Kill(true);
                    }
                    // Windows can also inherit the test runner's capture handles. Terminate the
                    // fixture after checking the script deadline, before awaiting capture EOF.
                    if (File.Exists(marker))
                    {
                        var pid = int.Parse(File.ReadAllText(marker));
                        try
                        {
                            using var process = System.Diagnostics.Process.GetProcessById(pid);
                            process.Kill();
                            process.WaitForExit(5000);
                        }
                        catch (ArgumentException)
                        {
                        }
                    }
                }
                Assert.Contains("process timed out after 3000 ms", await stdout.WaitAsync(TimeSpan.FromSeconds(5)));
                Assert.Equal("", await stderr.WaitAsync(TimeSpan.FromSeconds(5)));
            }
        }

        [Trait("Category", "CrossVersion")]
        [Trait("Category", "Runtime")]
        [Theory]
        [InlineData("pwsh")]
        [InlineData("powershell")]
        public async Task ProcessRunCapturesLargeValidUtf8Output(string host)
        {
            using var project = new Project();
            project.Write("large-output.ps1", """
            $bytes = New-Object byte[] 65536
            for ($index = 0; $index -lt $bytes.Length; $index++) { $bytes[$index] = 65 }
            $stdout = [Console]::OpenStandardOutput()
            for ($block = 0; $block -lt 16; $block++) { $stdout.Write($bytes, 0, $bytes.Length) }
            [Console]::Error.Write('stderr')
            exit 7
            """);
            var fixture = project.PathFor("large-output.ps1").Replace("\\", "\\\\");
            var stdoutPath = project.PathFor("stdout.txt").Replace("\\", "\\\\");
            var stderrPath = project.PathFor("stderr.txt").Replace("\\", "\\\\");
            project.Write("main.rukbat", $$"""
            import encoding; import fs
            import process
            result := process.run('powershell', ['-NoLogo', '-NoProfile', '-NonInteractive', '-File', '{{fixture}}'], process.options(null, encoding.utf8))
            fs.write_text('{{stdoutPath}}', result.stdout, encoding.utf8)
            fs.write_text('{{stderrPath}}', result.stderr, encoding.utf8)
            print(result.exit_code)
            """);

            var compiled = project.Compile();
            Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));
            var result = await RunOnHost(project, compiled.Output!, host);

            Assert.Equal(0, result.ExitCode);
            Assert.Equal("7\n", result.Output);
            Assert.Equal("", result.Error);
            AssertRepeatedBytes(File.ReadAllBytes(project.PathFor("stdout.txt")), (byte)'A');
            Assert.Equal(Encoding.UTF8.GetBytes("stderr"), File.ReadAllBytes(project.PathFor("stderr.txt")));
        }

        [Trait("Category", "CrossVersion")]
        [Trait("Category", "Runtime")]
        [Theory]
        [InlineData("stdout")]
        [InlineData("stderr")]
        public async Task ProcessRunLargeInvalidUtf8IsCatchableWithoutHanging(string stream)
        {
            using var project = new Project();
            project.Write("invalid-output.ps1", $$"""
            $bytes = New-Object byte[] 65536
            for ($index = 0; $index -lt $bytes.Length; $index++) { $bytes[$index] = 255 }
            $target = [Console]::OpenStandard{{(stream == "stdout" ? "Output" : "Error")}}()
            for ($block = 0; $block -lt 16; $block++) { $target.Write($bytes, 0, $bytes.Length) }
            """);
            var fixture = project.PathFor("invalid-output.ps1").Replace("\\", "\\\\");
            project.Write("main.rukbat", $$"""
            import encoding; import process
            try { process.run('powershell', ['-NoLogo', '-NoProfile', '-NonInteractive', '-File', '{{fixture}}'], process.options(null, encoding.utf8)) }
            catch err { print('caught') }
            """);

            var compiled = project.Compile();
            Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));
            foreach (var host in new[] { "pwsh", "powershell" })
            {
                var result = await RunOnHost(project, compiled.Output!, host);
                Assert.Equal(0, result.ExitCode);
                Assert.Equal("caught\n", result.Output);
                Assert.Equal("", result.Error);
            }
        }

        [Trait("Category", "CrossVersion")]
        [Trait("Category", "Runtime")]
        [Theory]
        [InlineData("pwsh")]
        [InlineData("powershell")]
        public async Task ProcessRunCapturesLargeOutputFromBothStreams(string host)
        {
            using var project = new Project();
            project.Write("both-output.ps1", """
            $stdoutBytes = New-Object byte[] 65536
            $stderrBytes = New-Object byte[] 65536
            for ($index = 0; $index -lt $stdoutBytes.Length; $index++) {
                $stdoutBytes[$index] = 65
                $stderrBytes[$index] = 66
            }
            $stdout = [Console]::OpenStandardOutput()
            $stderr = [Console]::OpenStandardError()
            for ($block = 0; $block -lt 16; $block++) {
                $stdout.Write($stdoutBytes, 0, $stdoutBytes.Length)
                $stderr.Write($stderrBytes, 0, $stderrBytes.Length)
            }
            exit 7
            """);
            var fixture = project.PathFor("both-output.ps1").Replace("\\", "\\\\");
            var stdoutPath = project.PathFor("stdout.txt").Replace("\\", "\\\\");
            var stderrPath = project.PathFor("stderr.txt").Replace("\\", "\\\\");
            project.Write("main.rukbat", $$"""
            import encoding; import fs
            import process
            result := process.run('powershell', ['-NoLogo', '-NoProfile', '-NonInteractive', '-File', '{{fixture}}'], process.options(null, encoding.utf8))
            fs.write_text('{{stdoutPath}}', result.stdout, encoding.utf8)
            fs.write_text('{{stderrPath}}', result.stderr, encoding.utf8)
            print(result.exit_code)
            """);

            var compiled = project.Compile();
            Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));
            var result = await RunOnHost(project, compiled.Output!, host);

            Assert.Equal(0, result.ExitCode);
            Assert.Equal("7\n", result.Output);
            Assert.Equal("", result.Error);
            AssertRepeatedBytes(File.ReadAllBytes(project.PathFor("stdout.txt")), (byte)'A');
            AssertRepeatedBytes(File.ReadAllBytes(project.PathFor("stderr.txt")), (byte)'B');
        }

        private static Task<RunResult> RunOnHost(Project project, string output, string host)
            => host == "powershell"
                ? project.RunOutputWithWindowsPowerShell(output)
                : project.RunOutput(output);

        private static void AssertRepeatedBytes(byte[] actual, byte value)
        {
            Assert.Equal(1024 * 1024, actual.Length);
            Assert.All(actual, actualValue => Assert.Equal(value, actualValue));
        }

        private async Task AssertProcessRunReturnsNonzeroExitAndSeparateOutput(bool crossVersion)
        {
            using var project = new Project();
            project.Write("main.rukbat", """
            import encoding; import process
            result: process.Result = process.run('powershell', [
                '-NoLogo', '-NoProfile', '-Command',
                'Start-Sleep -Milliseconds 100; [Console]::Out.Write("out"); [Console]::Error.Write("err"); exit 7'
            ], process.options(5000, encoding.utf8))
            print(result.exit_code)
            print(result.stdout)
            print(result.stderr)
            """);
            var compiled = project.Compile();
            Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));
            var results = crossVersion
                ? new[] { await project.RunOutput(compiled.Output!), await project.RunOutputWithWindowsPowerShell(compiled.Output!) }
                : new[] { await project.RunOutputWithWindowsPowerShell(compiled.Output!) };
            foreach (var result in results)
            {
                Assert.Equal(0, result.ExitCode);
                Assert.Equal("7\nout\nerr", result.Output.TrimEnd('\n'));
                Assert.Equal("", result.Error);
            }
        }

        [Trait("Category", "CrossVersion")]
        [Trait("Category", "Runtime")]
        [Fact]
        public async Task FilesReadRejectsNonUtf8OnBothPowerShellTargets()
        {
            using var project = new Project();
            var path = project.PathFor("utf16.txt").Replace("\\", "\\\\");
            File.WriteAllBytes(project.PathFor("utf16.txt"), [0xff, 0xfe, 0x41, 0x00]);
            project.Write("main.rukbat", $$"""
            import encoding; import fs
            try { fs.read_text('{{path}}', encoding.utf8) } catch err { print('invalid UTF-8') }
            """);
            var compiled = project.Compile();
            Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));
            foreach (var result in new[]
            {
                await project.RunOutput(compiled.Output!),
                await project.RunOutputWithWindowsPowerShell(compiled.Output!)
            })
            {
                Assert.Equal(0, result.ExitCode);
                Assert.Equal("invalid UTF-8", result.Output.TrimEnd('\n'));
                Assert.Equal("", result.Error);
            }
        }

        [Trait("Category", "Runtime")]
        [Fact]
        public async Task ProcessLaunchFailureIsAnRukbatError()
        {
            using var project = new Project();
            project.Write("main.rukbat", "import encoding; import process\nprocess.run('missing-rukbat-executable', [], process.options(null, encoding.utf8))");
            var result = await project.Run();
            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("missing-rukbat-executable", result.Error);
        }

        [Trait("Category", "Unit")]
        [Theory]
        [InlineData("import encoding; import fs\nfs.read_text(1, encoding.utf8)")]
        [InlineData("import path\npath.join('one')")]
        [InlineData("import env\nenv.set('x', null)")]
        [InlineData("import encoding; import process\nprocess.run('x', [1], process.options(null, encoding.utf8))")]
        [InlineData("import process\nprocess.run('x', [])")]
        [InlineData("import json as j")]
        [InlineData("import './json.rukbat' as json")]
        public void RejectsInvalidStandardLibraryCallsAndImports(string source)
        {
            var result = Compile(source);
            Assert.False(result.Success);
            Assert.NotEmpty(result.Diagnostics);
        }
    }
}
