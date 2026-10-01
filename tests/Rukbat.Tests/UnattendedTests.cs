using System.Diagnostics;
using System.Text;

namespace Rukbat.Tests;

public class UnattendedTests : TestBase
{
    private static string Literal(string value) => value.Replace("\\", "/").Replace("'", "\\'");

    private static string Build(Project project, string source)
    {
        project.Write("main.rukbat", source);
        var compiled = project.Compile();
        Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));
        project.Write("job.ps1", compiled.Output!);
        return compiled.Output!;
    }

    [Theory]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    [Trait("Category", "CrossVersion")]
    [Trait("Category", "Runtime")]
    public async Task MoveBatchCommitsAndReplaysWithoutRepeatingMoves(string shell)
    {
        using var project = new Project();
        project.Write("first.txt", "first");
        project.Write("second.txt", "second");
        Build(project, """
            import fs
            fs.move_files_batch(['first.txt', 'second.txt'], ['archive/first.txt', 'archive/second.txt'], 'moves.journal')
            """);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var result = await RunDeployed(shell, project.PathFor("job.ps1"), project.DirectoryPath, project.DirectoryPath);
            Assert.True(result.ExitCode == 0, result.Error);
            Assert.Equal("", result.Output);
            Assert.True(File.Exists(project.PathFor("archive/second.txt")), string.Join("\n", Directory.GetFiles(project.DirectoryPath, "*", SearchOption.AllDirectories)));
            Assert.Equal("first", File.ReadAllText(project.PathFor("archive/first.txt")));
            Assert.Equal("second", File.ReadAllText(project.PathFor("archive/second.txt")));
            Assert.False(File.Exists(project.PathFor("first.txt")));
            Assert.False(File.Exists(project.PathFor("second.txt")));
        }
        Assert.EndsWith("COMMIT1\n", Encoding.UTF8.GetString(File.ReadAllBytes(project.PathFor("moves.journal"))));
    }

    [Theory]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    [Trait("Category", "CrossVersion")]
    [Trait("Category", "Runtime")]
    public async Task MoveBatchRejectsConflictsBeforeMovingAnyFile(string shell)
    {
        using var project = new Project();
        project.Write("first.txt", "first");
        project.Write("second.txt", "second");
        project.Write("archive/second.txt", "unrelated");
        Build(project, """
            import fs
            fs.move_files_batch(['first.txt', 'second.txt'], ['archive/first.txt', 'archive/second.txt'], 'moves.journal')
            """);
        var result = await RunDeployed(shell, project.PathFor("job.ps1"), project.DirectoryPath, project.DirectoryPath);
        Assert.NotEqual(0, result.ExitCode);
        Assert.Equal("first", File.ReadAllText(project.PathFor("first.txt")));
        Assert.Equal("second", File.ReadAllText(project.PathFor("second.txt")));
        Assert.Equal("unrelated", File.ReadAllText(project.PathFor("archive/second.txt")));
        Assert.False(File.Exists(project.PathFor("archive/first.txt")));
    }

    [Theory]
    [InlineData("import fs; fs.move_files_batch(['x'], ['y'])")]
    [InlineData("import fs; fs.move_files_batch('x', ['y'], 'j')")]
    [InlineData("import fs; fs.move_files_batch(['x'], [1], 'j')")]
    [InlineData("import fs; fs.recover_file_moves(1)")]
    [Trait("Category", "Unit")]
    public void BatchOperationsRejectInvalidArguments(string source) => Assert.False(Compile(source).Success);

    private static Process Start(Project project, string shell)
    {
        var info = new ProcessStartInfo(shell)
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            CreateNoWindow = true, WorkingDirectory = project.DirectoryPath
        };
        foreach (var argument in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-File", project.PathFor("job.ps1") }) info.ArgumentList.Add(argument);
        return Process.Start(info)!;
    }

    private static async Task WaitForMarker(Project project, Process runner)
    {
        var deadline = Stopwatch.StartNew();
        while (!File.Exists(project.PathFor("ready")) && !runner.HasExited && deadline.Elapsed < TimeSpan.FromSeconds(15))
            await Task.Delay(20);
        Assert.True(File.Exists(project.PathFor("ready")), runner.HasExited ? await runner.StandardError.ReadToEndAsync() : "Fixture did not reach crash point.");
    }

    private static string Pause(Project project) => "System.IO.File.WriteAllText(" +
        System.Text.Json.JsonSerializer.Serialize(project.PathFor("ready")) + ", \"ready\"); System.Threading.Thread.Sleep(60000);";

    private static async Task WaitForFileRelease(Project project)
    {
        var deadline = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                foreach (var path in Directory.GetFiles(project.DirectoryPath, "*.txt", SearchOption.AllDirectories))
                {
                    using var stream = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                }
                return;
            }
            catch (IOException) when (deadline.Elapsed < TimeSpan.FromSeconds(5)) { await Task.Delay(20); }
        }
    }

    private const string TwoMoves = """
        import fs
        fs.move_files_batch(['first.txt', 'second.txt'], ['archive/first.txt', 'archive/second.txt'], 'moves.journal')
        """;

    [Theory]
    [InlineData("powershell", false)]
    [InlineData("pwsh", false)]
    [InlineData("powershell", true)]
    [InlineData("pwsh", true)]
    [Trait("Category", "CrossVersion")]
    [Trait("Category", "Runtime")]
    public async Task MoveBatchRecoversAfterHostIsKilledBeforeOrAfterFirstRename(string shell, bool afterRename)
    {
        using var project = new Project();
        project.Write("first.txt", "first"); project.Write("second.txt", "second");
        var generated = Build(project, TwoMoves);
        var point = afterRename ? "Rename(entry, entry.Destination);" : "Save(journal, context.Entries);";
        Assert.Contains(point, generated);
        project.Write("job.ps1", generated.Replace(point, point + Pause(project)));
        using (var runner = Start(project, shell))
        {
            try { await WaitForMarker(project, runner); }
            finally { if (!runner.HasExited) runner.Kill(); await runner.WaitForExitAsync(); }
        }
        Assert.Equal(afterRename, File.Exists(project.PathFor("archive/first.txt")));
        Build(project, "import fs; fs.recover_file_moves('moves.journal')");
        for (var repeat = 0; repeat < 2; repeat++)
        {
            var recovered = await RunDeployed(shell, project.PathFor("job.ps1"), project.DirectoryPath, project.DirectoryPath);
            Assert.True(recovered.ExitCode == 0, recovered.Error);
            Assert.Equal("first", File.ReadAllText(project.PathFor("first.txt")));
            Assert.Equal("second", File.ReadAllText(project.PathFor("second.txt")));
            Assert.False(File.Exists(project.PathFor("archive/first.txt")));
        }
        Build(project, TwoMoves);
        var retried = await RunDeployed(shell, project.PathFor("job.ps1"), project.DirectoryPath, project.DirectoryPath);
        Assert.True(retried.ExitCode == 0, retried.Error);
        Assert.Equal("first", File.ReadAllText(project.PathFor("archive/first.txt")));
        Assert.Equal("second", File.ReadAllText(project.PathFor("archive/second.txt")));
    }

    [Theory]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    [Trait("Category", "CrossVersion")]
    [Trait("Category", "Runtime")]
    public async Task MoveBatchRollsBackOnFailureAndRejectsConcurrentExecution(string shell)
    {
        using var project = new Project();
        project.Write("first.txt", "first"); project.Write("second.txt", "second");
        var generated = Build(project, TwoMoves);
        project.Write("job.ps1", generated.Replace("entry.Moved = true;", "entry.Moved = true; " + Pause(project) + "throw new IOException(\"injected failure\");"));
        using var runner = Start(project, shell);
        try
        {
            await WaitForMarker(project, runner);
            project.Write("job.ps1", generated);
            var duplicate = await RunDeployed(shell, project.PathFor("job.ps1"), project.DirectoryPath, project.DirectoryPath);
            Assert.NotEqual(0, duplicate.ExitCode);
            Assert.Contains("Cannot lock path", duplicate.Error);
        }
        finally { if (!runner.HasExited) runner.Kill(); await runner.WaitForExitAsync(); }
        // Exercise the ordinary exception path separately from hard termination.
        Build(project, "import fs; fs.recover_file_moves('moves.journal')");
        var recovery = await RunDeployed(shell, project.PathFor("job.ps1"), project.DirectoryPath, project.DirectoryPath);
        Assert.True(recovery.ExitCode == 0, recovery.Error);
        project.Write("job.ps1", generated.Replace("entry.Moved = true;", "entry.Moved = true; if (entry.Moved) throw new IOException(\"injected failure\");"));
        var failed = await RunDeployed(shell, project.PathFor("job.ps1"), project.DirectoryPath, project.DirectoryPath);
        Assert.NotEqual(0, failed.ExitCode);
        Assert.Contains("files were rolled back", failed.Error);
        Assert.Equal("first", File.ReadAllText(project.PathFor("first.txt")));
        Assert.Equal("second", File.ReadAllText(project.PathFor("second.txt")));
        Assert.False(File.Exists(project.PathFor("archive/first.txt")));
    }

    [Theory]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    [Trait("Category", "CrossVersion")]
    [Trait("Category", "Runtime")]
    public async Task RecoveryRefusesChangedFilesAndCorruptJournals(string shell)
    {
        using var project = new Project();
        project.Write("first.txt", "first"); project.Write("second.txt", "second");
        var generated = Build(project, TwoMoves);
        project.Write("job.ps1", generated.Replace("Rename(entry, entry.Destination);", "Rename(entry, entry.Destination); " + Pause(project)));
        using (var runner = Start(project, shell))
        {
            try { await WaitForMarker(project, runner); }
            finally { if (!runner.HasExited) runner.Kill(); await runner.WaitForExitAsync(); }
        }
        await WaitForFileRelease(project);
        project.Write("archive/first.txt", "external change");
        Build(project, "import fs; fs.recover_file_moves('moves.journal')");
        var result = await RunDeployed(shell, project.PathFor("job.ps1"), project.DirectoryPath, project.DirectoryPath);
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("contents changed", result.Error);
        Assert.Equal("external change", File.ReadAllText(project.PathFor("archive/first.txt")));
        Assert.Equal("second", File.ReadAllText(project.PathFor("second.txt")));
        var bytes = File.ReadAllBytes(project.PathFor("moves.journal"));
        bytes[20] ^= 1;
        File.WriteAllBytes(project.PathFor("moves.journal"), bytes);
        result = await RunDeployed(shell, project.PathFor("job.ps1"), project.DirectoryPath, project.DirectoryPath);
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("checksum mismatch", result.Error);
    }

    [Theory]
    [InlineData("powershell", false)]
    [InlineData("pwsh", false)]
    [InlineData("powershell", true)]
    [InlineData("pwsh", true)]
    [Trait("Category", "CrossVersion")]
    [Trait("Category", "Runtime")]
    public async Task ProcessJobKillsDescendantsOnTimeoutOrHostTermination(string shell, bool killHost)
    {
        using var project = new Project();
        project.Write("child.ps1", "[IO.File]::WriteAllText('ready', [string]$PID); Start-Sleep -Seconds 60");
        project.Write("parent.ps1", "Start-Process powershell -ArgumentList '-NoProfile -NonInteractive -File child.ps1' -NoNewWindow | Out-Null; exit 0");
        Build(project, $$"""
            import process
            try { process.run('powershell', ['-NoProfile', '-NonInteractive', '-File', 'parent.ps1'], {{(killHost ? "null" : "4000")}}) }
            catch err { print(err.message) }
            """);
        using var runner = Start(project, shell);
        Process? descendant = null;
        try
        {
            await WaitForMarker(project, runner);
            descendant = Process.GetProcessById(int.Parse(File.ReadAllText(project.PathFor("ready"))));
            if (killHost) runner.Kill();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            await runner.WaitForExitAsync(timeout.Token);
            await descendant.WaitForExitAsync(timeout.Token);
            if (!killHost)
            {
                Assert.Equal(0, runner.ExitCode);
                Assert.Contains("process timed out after 4000 ms", await runner.StandardOutput.ReadToEndAsync());
                Assert.Equal("", await runner.StandardError.ReadToEndAsync());
            }
        }
        finally
        {
            if (!runner.HasExited) { runner.Kill(); await runner.WaitForExitAsync(); }
            if (descendant is not null)
            {
                if (!descendant.HasExited) { descendant.Kill(); await descendant.WaitForExitAsync(); }
                descendant.Dispose();
            }
        }
    }

    [Theory]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    [Trait("Category", "CrossVersion")]
    [Trait("Category", "Runtime")]
    public async Task ProcessJobBoundsCaptureAndClosesStandardInput(string shell)
    {
        using var project = new Project();
        project.Write("output.ps1", "$buffer = New-Object byte[] 1048576; $out = [Console]::OpenStandardOutput(); for ($i=0; $i -lt 80; $i++) { $out.Write($buffer,0,$buffer.Length) }; Start-Sleep 60");
        Build(project, """
            import process
            input := process.run('powershell', ['-NoProfile', '-Command', '[Console]::Write([Console]::In.ReadToEnd().Length)'], 5000)
            print(input.stdout)
            try { process.run('powershell', ['-NoProfile', '-File', 'output.ps1'], 10000) }
            catch err { print(err.message) }
            """);
        var result = await RunDeployed(shell, project.PathFor("job.ps1"), project.DirectoryPath, project.DirectoryPath);
        Assert.True(result.ExitCode == 0, result.Error);
        Assert.StartsWith("0\n", result.Output);
        Assert.Contains("exceeded 64 MiB", result.Output);
    }

    [Theory]
    [InlineData("powershell", false)]
    [InlineData("pwsh", false)]
    [InlineData("powershell", true)]
    [InlineData("pwsh", true)]
    [Trait("Category", "CrossVersion")]
    [Trait("Category", "Runtime")]
    public async Task MoveBatchRetriesAfterTerminationAroundCommit(string shell, bool committed)
    {
        using var project = new Project();
        project.Write("first.txt", "first"); project.Write("second.txt", "second");
        var generated = Build(project, TwoMoves);
        const string commitStart = "journal.Write(Commit, 0, Commit.Length);";
        var pausePoint = committed ? commitStart + "\n                journal.Flush(true);" : commitStart;
        Assert.Contains(pausePoint, generated);
        project.Write("job.ps1", generated.Replace(pausePoint, committed ? pausePoint + Pause(project) : Pause(project) + pausePoint));
        using (var runner = Start(project, shell))
        {
            try { await WaitForMarker(project, runner); }
            finally { if (!runner.HasExited) runner.Kill(); await runner.WaitForExitAsync(); }
        }
        await WaitForFileRelease(project);
        Assert.False(File.Exists(project.PathFor("first.txt")));
        Assert.False(File.Exists(project.PathFor("second.txt")));
        var before = File.ReadAllBytes(project.PathFor("moves.journal"));
        Build(project, TwoMoves);
        var result = await RunDeployed(shell, project.PathFor("job.ps1"), project.DirectoryPath, project.DirectoryPath);
        Assert.True(result.ExitCode == 0, result.Error);
        Assert.Equal("first", File.ReadAllText(project.PathFor("archive/first.txt")));
        Assert.Equal("second", File.ReadAllText(project.PathFor("archive/second.txt")));
        if (committed) Assert.Equal(before, File.ReadAllBytes(project.PathFor("moves.journal")));
    }

    [Theory]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    [Trait("Category", "CrossVersion")]
    [Trait("Category", "Runtime")]
    public async Task RecoveryCanItselfBeInterruptedAndRepeated(string shell)
    {
        using var project = new Project();
        project.Write("first.txt", "first"); project.Write("second.txt", "second");
        var generated = Build(project, TwoMoves);
        project.Write("job.ps1", generated.Replace("journal.Write(Commit, 0, Commit.Length);", Pause(project) + "journal.Write(Commit, 0, Commit.Length);"));
        using (var runner = Start(project, shell))
        {
            try { await WaitForMarker(project, runner); }
            finally { if (!runner.HasExited) runner.Kill(); await runner.WaitForExitAsync(); }
        }
        File.Delete(project.PathFor("ready"));
        generated = Build(project, "import fs; fs.recover_file_moves('moves.journal')");
        project.Write("job.ps1", generated.Replace("Rename(entry, entry.Source);", "Rename(entry, entry.Source); " + Pause(project)));
        using (var runner = Start(project, shell))
        {
            try { await WaitForMarker(project, runner); }
            finally { if (!runner.HasExited) runner.Kill(); await runner.WaitForExitAsync(); }
        }
        project.Write("job.ps1", generated);
        var result = await RunDeployed(shell, project.PathFor("job.ps1"), project.DirectoryPath, project.DirectoryPath);
        Assert.True(result.ExitCode == 0, result.Error);
        Assert.Equal("first", File.ReadAllText(project.PathFor("first.txt")));
        Assert.Equal("second", File.ReadAllText(project.PathFor("second.txt")));
    }

    [Theory]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    [Trait("Category", "CrossVersion")]
    [Trait("Category", "Runtime")]
    public async Task MoveBatchRejectsDifferentPlansAndCrossVolumeMoves(string shell)
    {
        using var project = new Project();
        project.Write("first.txt", "first"); project.Write("second.txt", "second");
        Build(project, TwoMoves);
        var result = await RunDeployed(shell, project.PathFor("job.ps1"), project.DirectoryPath, project.DirectoryPath);
        Assert.True(result.ExitCode == 0, result.Error);
        Build(project, "import fs; fs.move_files_batch(['first.txt'], ['elsewhere.txt'], 'moves.journal')");
        result = await RunDeployed(shell, project.PathFor("job.ps1"), project.DirectoryPath, project.DirectoryPath);
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("another plan", result.Error);
        var other = DriveInfo.GetDrives().First(d => d.IsReady && d.DriveType == DriveType.Fixed && d.DriveFormat == "NTFS" && d.Name != Path.GetPathRoot(project.DirectoryPath));
        project.Write("third.txt", "third");
        Build(project, $"import fs; fs.move_files_batch(['third.txt'], ['{Literal(Path.Combine(other.Name, "rukbat-must-not-create-" + Guid.NewGuid()))}'], 'other.journal')");
        result = await RunDeployed(shell, project.PathFor("job.ps1"), project.DirectoryPath, project.DirectoryPath);
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("one NTFS volume", result.Error);
        Assert.Equal("third", File.ReadAllText(project.PathFor("third.txt")));
    }

    [Theory]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    [Trait("Category", "CrossVersion")]
    [Trait("Category", "Runtime")]
    public async Task ConstrainedLanguageFailsBeforeUserOperations(string shell)
    {
        using var project = new Project();
        var generated = Build(project, "import fs; fs.write_text('must-not-exist', 'changed', 'utf-8')");
        project.Write("job.ps1", "$ExecutionContext.SessionState.LanguageMode = 'ConstrainedLanguage'\n" + generated);
        var result = await RunDeployed(shell, project.PathFor("job.ps1"), project.DirectoryPath, project.DirectoryPath);
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("requiresFullLanguage", string.Concat(result.Error.Where(c => !char.IsWhiteSpace(c))));
        Assert.False(File.Exists(project.PathFor("must-not-exist")));
    }

    [Theory]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    [Trait("Category", "CrossVersion")]
    [Trait("Category", "Runtime")]
    public async Task MoveBatchRejectsUnrecoverablePlanSizesBeforeMutation(string shell)
    {
        using var project = new Project();
        project.Write("untouched.txt", "original");
        using (var large = File.Create(project.PathFor("large.journal"))) large.SetLength(64 * 1024 * 1024 + 1025);
        Build(project, """
            import fs
            fs.move_files_batch([], [], 'seed.journal')
            try {
                powershell<void>('Invoke-Expression', "[RukbatMoveBatch]::Run([string[]]::new(100001), [string[]]::new(100001), 'oversized.journal')")
            } catch err { print(err.message) }
            try { fs.recover_file_moves('large.journal') } catch err { print(err.message) }
            """);
        var result = await RunDeployed(shell, project.PathFor("job.ps1"), project.DirectoryPath, project.DirectoryPath);
        Assert.True(result.ExitCode == 0, result.Error);
        Assert.Contains("at most 100000 entries", result.Output);
        Assert.Contains("Journal exceeds", result.Output);
        Assert.False(File.Exists(project.PathFor("oversized.journal")));
        Assert.Equal("original", File.ReadAllText(project.PathFor("untouched.txt")));
    }

    [Theory]
    [InlineData("powershell", "Junction")]
    [InlineData("pwsh", "Junction")]
    [InlineData("powershell", "HardLink")]
    [InlineData("pwsh", "HardLink")]
    [Trait("Category", "CrossVersion")]
    [Trait("Category", "Runtime")]
    public async Task MoveBatchRejectsLinksWithoutChangingTheirTargets(string shell, string linkType)
    {
        using var project = new Project();
        project.Write("real/original.txt", "original");
        var source = linkType == "Junction" ? "linked/original.txt" : "linked.txt";
        var target = linkType == "Junction" ? project.PathFor("real") : project.PathFor("real/original.txt");
        var link = linkType == "Junction" ? project.PathFor("linked") : project.PathFor("linked.txt");
        var generated = Build(project, $"import fs; fs.move_files_batch(['{source}'], ['archive/moved.txt'], 'moves.journal')");
        var setup = $"New-Item -ItemType {linkType} -Path '{link.Replace("'", "''")}' -Target '{target.Replace("'", "''")}' -ErrorAction Stop | Out-Null\n";
        project.Write("job.ps1", setup + generated);
        try
        {
            var result = await RunDeployed(shell, project.PathFor("job.ps1"), project.DirectoryPath, project.DirectoryPath);
            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains(linkType == "Junction" ? "Reparse points" : "one link", result.Error);
            Assert.Equal("original", File.ReadAllText(project.PathFor("real/original.txt")));
            Assert.Equal("original", File.ReadAllText(project.PathFor(source)));
            Assert.False(File.Exists(project.PathFor("archive/moved.txt")));
        }
        finally
        {
            if (linkType == "Junction" && Directory.Exists(link)) Directory.Delete(link);
        }
    }
}
