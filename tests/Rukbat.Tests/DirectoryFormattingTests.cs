using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Rukbat.Tests
{
    public sealed class DirectoryFormattingTests : TestBase
    {
        [Fact]
        [Trait("Category", "Unit")]
        public async Task FormatsDotFromTheCallingDirectoryRecursively()
        {
            using var project = new Project();
            project.Write("main.rukbat", "value:=1");
            project.Write("nested/deeper/module.RUKBAT", "fn f():void{print(2)}");
            project.Write("keep.txt", "value:=3");
            foreach (var directory in new[] { ".git", "bin", "obj" })
            {
                project.Write($"{directory}/keep.rukbat", "value:=4");
            }
            var result = await RunCli(project, ["fmt", "."]);
            Assert.True(result.ExitCode == 0, result.Error);
            Assert.Equal("value := 1\n", File.ReadAllText(project.PathFor("main.rukbat")));
            Assert.Equal("fn f(): void {\n    print(2)\n}\n", File.ReadAllText(project.PathFor("nested/deeper/module.RUKBAT")));
            Assert.Equal("value:=3", File.ReadAllText(project.PathFor("keep.txt")));
            foreach (var directory in new[] { ".git", "bin", "obj" })
            {
                Assert.Equal("value:=4", File.ReadAllText(project.PathFor($"{directory}/keep.rukbat")));
            }
            Assert.False(File.Exists(project.PathFor("main.ps1")));
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void ReportsInvalidSourceAndContinuesWithOtherFiles()
        {
            using var project = new Project();
            const string invalid = "value:='unterminated";
            project.Write("a-bad.rukbat", invalid);
            project.Write("z-good.rukbat", "value:=2");
            using var errors = new StringWriter();
            Assert.Equal(1, Cli.Run(["fmt", project.DirectoryPath], TextWriter.Null, errors));
            Assert.Contains("a-bad.rukbat", errors.ToString());
            Assert.Equal(invalid, File.ReadAllText(project.PathFor("a-bad.rukbat")));
            Assert.Equal("value := 2\n", File.ReadAllText(project.PathFor("z-good.rukbat")));
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void ReportsLockedFileAndContinuesWithOtherFiles()
        {
            using var project = new Project();
            project.Write("a-locked.rukbat", "value:=1");
            project.Write("z-good.rukbat", "value:=2");
            using var locked = File.Open(project.PathFor("a-locked.rukbat"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            using var errors = new StringWriter();
            Assert.Equal(2, Cli.Run(["fmt", project.DirectoryPath], TextWriter.Null, errors));
            Assert.Contains("a-locked.rukbat", errors.ToString());
            Assert.Equal("value := 2\n", File.ReadAllText(project.PathFor("z-good.rukbat")));
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void ReportsLockedDirectoryAndContinuesWithSiblingDirectories()
        {
            if (!OperatingSystem.IsWindows())
            {
                return;
            }

            using var project = new Project();
            const string invalid = "value:='unterminated";
            project.Write("a-before/first.rukbat", "first:=1");
            project.Write("b-locked/blocked.rukbat", "blocked:=2");
            project.Write("c-invalid/bad.rukbat", invalid);
            project.Write("z-after/last.rukbat", "last:=3");
            using var errors = new StringWriter();
            using (var locked = OpenDirectory(project.PathFor("b-locked"), FileListDirectory, NoSharing,
                IntPtr.Zero, OpenExisting, BackupSemantics, IntPtr.Zero))
            {
                Assert.False(locked.IsInvalid, new Win32Exception(Marshal.GetLastWin32Error()).Message);
                Assert.Equal(2, Cli.Run(["fmt", project.DirectoryPath], TextWriter.Null, errors));
            }

            Assert.Contains(project.PathFor("b-locked"), errors.ToString());
            Assert.Contains("bad.rukbat", errors.ToString());
            Assert.Equal("first := 1\n", File.ReadAllText(project.PathFor("a-before/first.rukbat")));
            Assert.Equal("blocked:=2", File.ReadAllText(project.PathFor("b-locked/blocked.rukbat")));
            Assert.Equal(invalid, File.ReadAllText(project.PathFor("c-invalid/bad.rukbat")));
            Assert.Equal("last := 3\n", File.ReadAllText(project.PathFor("z-after/last.rukbat")));
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void EmptyDirectorySucceeds()
        {
            using var project = new Project();
            Assert.Equal(0, Cli.Run(["fmt", project.DirectoryPath], TextWriter.Null, TextWriter.Null));
        }

        [Theory]
        [InlineData("missing")]
        [InlineData("missing.rukbat")]
        [InlineData("keep.txt")]
        [Trait("Category", "Unit")]
        public void InvalidTargetsReturnUsageOrIoFailure(string path)
        {
            using var project = new Project();
            project.Write("keep.txt", "preserve");
            Assert.Equal(2, Cli.Run(["fmt", project.PathFor(path)], TextWriter.Null, TextWriter.Null));
            Assert.Equal("preserve", File.ReadAllText(project.PathFor("keep.txt")));
        }

        private const uint FileListDirectory = 1;
        private const uint NoSharing = 0;
        private const uint OpenExisting = 3;
        private const uint BackupSemantics = 0x02000000;

        [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle OpenDirectory(string path, uint access, uint share,
            IntPtr security, uint disposition, uint flags, IntPtr template);
    }
}
