using System.IO.Compression;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace Rukbat.Tests
{
    public class ArchiveHashTests : TestBase
    {
        private static string Literal(string value) => "'" + value.Replace("\\", "\\\\").Replace("'", "\\'") + "'";
        private static string Expected(string value) => "json.parse<string>(" + Literal(JsonSerializer.Serialize(value)) + ")";

        [Trait("Category", "Unit")]
        [Theory]
        [InlineData("hash.file('x', 'sha256')")]
        [InlineData("hash.text('x', hash.sha256, 'utf-8')")]
        [InlineData("hash.sha256 = hash.md5")]
        [InlineData("hash.name(encoding.utf8)")]
        [InlineData("x := hash.Algorithm {}")]
        [InlineData("x := hash.sha256 + hash.md5")]
        [InlineData("x := hash.sha256 < hash.md5")]
        [InlineData("print(hash.sha256)")]
        [InlineData("json.stringify([hash.sha256])")]
        [InlineData("json.parse<hash.Algorithm[]>('[]')")]
        [InlineData("powershell<hash.Algorithm>('Write-Output', 'sha256')")]
        [InlineData("struct Config { value: hash.Algorithm }; powershell<Config>('Write-Output', 'x')")]
        [InlineData("powershell<void>('Write-Output', [hash.sha256])")]
        [InlineData("struct Config { value: hash.Algorithm }; json.stringify(Config { value: hash.sha256 })")]
        [InlineData("hash.parse(name: 'sha256')")]
        [InlineData("hash.parse<int>('sha256')")]
        [InlineData("zip.create(['a', 'b'], 'out.zip')")]
        [InlineData("zip.list('x.zip', null)")]
        [InlineData("zip.options('utf-8')")]
        [InlineData("o := zip.options(encoding.utf8); o.max_entries = 1.5")]
        [InlineData("zip.extract('x.zip', 'out')")]
        public void RejectsInvalidTypesAndExternalBoundaries(string source) =>
            Assert.False(Compile("import zip; import hash; import encoding; import json\n" + source).Success);

        [Trait("Category", "Unit")]
        [Fact]
        public void OrdinaryOptionsAndOpaqueAlgorithmContainersAreChecked()
        {
            Success("""
            import zip; import encoding; import hash
            fn choose(value: hash.Algorithm): hash.Algorithm { return value }
            values: hash.Algorithm?[] = [choose(hash.sha256), null]
            options := zip.Options { name_encoding: encoding.utf8, max_entries: 10, max_total_bytes: 1000 }
            entries: zip.Entry[] = zip.list('x.zip', options)
            """);
            var hash = Success("import hash; print(hash.name(hash.sha256))");
            Assert.DoesNotContain("RukbatZip", hash);
            Assert.DoesNotContain("RukbatProcessJob", hash);
            Assert.DoesNotContain("RukbatMoveBatch", hash);
            var zip = Success("import zip; zip.create('source', 'out.zip')");
            Assert.DoesNotContain("RukbatHash", zip);
            Assert.DoesNotContain("RukbatProcessJob", zip);
        }

        [Trait("Category", "CrossVersion")]
        [Theory]
        [InlineData("powershell")]
        [InlineData("pwsh")]
        public async Task ChecksKnownHashVectorsAndExplicitTextEncoding(string shell)
        {
            using var project = new Project();
            File.WriteAllBytes(project.PathFor("abc.bin"), "abc"u8.ToArray());
            var binary = new byte[5 * 1024 * 1024 + 17];
            for (int i = 0; i < binary.Length; i++)
            {
                binary[i] = (byte)i;
            }

            File.WriteAllBytes(project.PathFor("large.bin"), binary);
            project.Write("choice.rukbat", "import hash\nfn select(value: hash.Algorithm): hash.Algorithm { return value }");
            var source = "import hash; import encoding; import test; import json; import './choice.rukbat' as choice\n";
            foreach (var (name, algorithm) in new (string, HashAlgorithm)[] {
                ("sha256", SHA256.Create()), ("sha384", SHA384.Create()), ("sha512", SHA512.Create()), ("sha1", SHA1.Create()), ("md5", MD5.Create()) })
            {
                using (algorithm)
                {
                    var expected = Convert.ToHexString(algorithm.ComputeHash("abc"u8.ToArray())).ToLowerInvariant();
                    source += $"test.assert(hash.file({Literal(project.PathFor("abc.bin"))}, choice.select(hash.{name})) == '{expected}')\n" +
                        $"test.assert(hash.text('abc', hash.{name}, encoding.utf8) == '{expected}')\n" +
                        $"test.assert(hash.parse(hash.name(hash.{name})) == hash.{name})\n";
                }
            }
            source += $$"""
            test.assert(hash.text('', hash.sha256, encoding.utf8) == 'e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855')
            test.assert(hash.file({{Literal(project.PathFor("large.bin"))}}, hash.sha256) == '{{Convert.ToHexString(SHA256.HashData(binary)).ToLowerInvariant()}}')
            test.assert(hash.text('abc', hash.sha256, encoding.utf16_le) == '{{Convert.ToHexString(SHA256.HashData(Encoding.Unicode.GetBytes("abc"))).ToLowerInvariant()}}')
            japan := {{Expected("日本")}}
            test.assert(hash.text(japan, hash.sha256, encoding.windows_932) == '{{Convert.ToHexString(SHA256.HashData([0x93, 0xfa, 0x96, 0x7b])).ToLowerInvariant()}}')
            rejected := false
            try { hash.text(japan, hash.sha256, encoding.ascii) } catch err { rejected = true }
            test.assert(rejected)
            malformed := powershell<string>('Invoke-Expression', '[string][char]0xD800')
            for selected in [encoding.utf8, encoding.windows_932] {
                rejected := false
                try { hash.text(malformed, hash.sha256, selected) } catch err { rejected = true }
                test.assert(rejected)
            }
            for invalid in ['SHA256', ' sha256', 'sha-256', 'crc32'] {
                failed := false
                try { hash.parse(invalid) } catch err { failed = true }
                test.assert(failed)
            }
            missing := false
            try { hash.file('missing-hash-input', hash.sha256) } catch err { missing = true }
            test.assert(missing)
            print('passed')
            """;
            project.Write("main.rukbat", source);
            await Verify(project, shell);
        }

        [Trait("Category", "CrossVersion")]
        [Theory]
        [InlineData("powershell")]
        [InlineData("pwsh")]
        public async Task CreatesListsAndExtractsHiddenEmptyAndUnicodeFiles(string shell)
        {
            using var project = new Project();
            project.Write("source/日本.txt", "payload");
            project.Write("source/hidden.bin", "hidden");
            project.Write("source/zero.bin", "");
            Directory.CreateDirectory(project.PathFor("source/empty"));
            Directory.CreateDirectory(project.PathFor("source/hidden-dir"));
            File.SetAttributes(project.PathFor("source/hidden.bin"), FileAttributes.Hidden);
            File.SetAttributes(project.PathFor("source/hidden-dir"), FileAttributes.Directory | FileAttributes.Hidden);
            var root = project.DirectoryPath.Replace('\\', '/');
            project.Write("main.rukbat", $$"""
            import zip; import encoding; import fs; import json; import test
            options := zip.options(encoding.windows_932)
            test.assert(options.max_entries == 100000)
            test.assert(options.max_total_bytes == 10737418240)
            zip.create('{{root}}/source', '{{root}}/archive.zip')
            entries := zip.list('{{root}}/archive.zip', options)
            test.assert(entries.length == 5)
            found := false
            for entry in entries {
                if entry.name == {{Expected("日本.txt")}} { found = true; test.assert(entry.size == 7); test.assert(!entry.is_directory) }
            }
            test.assert(found)
            zip.extract('{{root}}/archive.zip', '{{root}}/out', options)
            test.assert(fs.read_text('{{root}}/out/hidden.bin', encoding.utf8) == 'hidden')
            test.assert(fs.is_dir('{{root}}/out/empty'))
            test.assert(fs.is_dir('{{root}}/out/hidden-dir'))
            test.assert(fs.is_file('{{root}}/out/zero.bin'))
            existing := false
            try { zip.extract('{{root}}/archive.zip', '{{root}}/out', options) } catch err { existing = true }
            test.assert(existing)
            zip.create('{{root}}/source/empty', '{{root}}/empty.zip')
            test.assert(zip.list('{{root}}/empty.zip', options).length == 0)
            zip.extract('{{root}}/empty.zip', '{{root}}/empty-out', options)
            test.assert(fs.is_dir('{{root}}/empty-out'))
            print('passed')
            """);
            await Verify(project, shell);
            Assert.Equal("payload", File.ReadAllText(project.PathFor("out/日本.txt")));
            Assert.Empty(Directory.GetFileSystemEntries(project.DirectoryPath, ".rukbat-zip-*.tmp"));
            using var archive = ZipFile.OpenRead(project.PathFor("archive.zip"));
            Assert.Contains(archive.Entries, e => e.FullName == "日本.txt");
        }

        private sealed record RawEntry(byte[] Name, byte[] Data, ushort Flags = 0x800, ushort Method = 0,
            uint Attributes = 0, uint? Size = null, uint? Crc = null);

        private static byte[] RawZip(params RawEntry[] entries)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
            var offsets = new List<uint>();
            static uint Crc(byte[] data) {
                uint crc = uint.MaxValue;
                foreach (byte value in data) {
                    crc ^= value;
                    for (int i = 0; i < 8; i++)
                    {
                        crc = (crc & 1) != 0 ? 0xedb88320 ^ (crc >> 1) : crc >> 1;
                    }
                }
                return crc ^ uint.MaxValue;
            }
            foreach (var e in entries) {
                offsets.Add((uint)stream.Position);
                writer.Write(0x04034b50u); writer.Write((ushort)20); writer.Write(e.Flags); writer.Write(e.Method);
                writer.Write(0u); writer.Write(e.Crc ?? Crc(e.Data)); writer.Write((uint)e.Data.Length); writer.Write(e.Size ?? (uint)e.Data.Length);
                writer.Write((ushort)e.Name.Length); writer.Write((ushort)0); writer.Write(e.Name); writer.Write(e.Data);
            }
            uint central = (uint)stream.Position;
            for (int i = 0; i < entries.Length; i++) {
                var e = entries[i];
                writer.Write(0x02014b50u); writer.Write((ushort)0x314); writer.Write((ushort)20); writer.Write(e.Flags); writer.Write(e.Method);
                writer.Write(0u); writer.Write(e.Crc ?? Crc(e.Data)); writer.Write((uint)e.Data.Length); writer.Write(e.Size ?? (uint)e.Data.Length);
                writer.Write((ushort)e.Name.Length); writer.Write((ushort)0); writer.Write((ushort)0); writer.Write((ushort)0); writer.Write((ushort)0);
                writer.Write(e.Attributes); writer.Write(offsets[i]); writer.Write(e.Name);
            }
            uint size = (uint)stream.Position - central;
            writer.Write(0x06054b50u); writer.Write((ushort)0); writer.Write((ushort)0);
            writer.Write((ushort)entries.Length); writer.Write((ushort)entries.Length); writer.Write(size); writer.Write(central); writer.Write((ushort)0);
            return stream.ToArray();
        }
        private static RawEntry Entry(string name, string data = "abc") => new(Encoding.UTF8.GetBytes(name), Encoding.UTF8.GetBytes(data));

        [Trait("Category", "CrossVersion")]
        [Theory]
        [InlineData("powershell")]
        [InlineData("pwsh")]
        public async Task MixedUtf8AndWindows932NamesRespectEntryFlags(string shell)
        {
            using var project = new Project();
            var cpName = "legacy/"u8.ToArray().Concat(new byte[] { 0x93, 0xfa, 0x96, 0x7b }).Concat(".txt"u8.ToArray()).ToArray();
            File.WriteAllBytes(project.PathFor("mixed.zip"), RawZip(new(cpName, "abc"u8.ToArray(), Flags: 0), Entry("unicode/日本.txt")));
            var root = project.DirectoryPath.Replace('\\', '/');
            project.Write("main.rukbat", $$"""
            import zip; import encoding; import test; import json
            options := zip.options(encoding.windows_932)
            items := zip.list('{{root}}/mixed.zip', options)
            test.assert(items[0].name == {{Expected("legacy/日本.txt")}})
            test.assert(items[1].name == {{Expected("unicode/日本.txt")}})
            zip.extract('{{root}}/mixed.zip', '{{root}}/out', options)
            rejected := false
            try { zip.list('{{root}}/mixed.zip', zip.options(encoding.utf8)) } catch err { rejected = true }
            test.assert(rejected)
            print('passed')
            """);
            await Verify(project, shell);
            Assert.Equal("abc", File.ReadAllText(project.PathFor("out/legacy/日本.txt")));
            Assert.Equal("abc", File.ReadAllText(project.PathFor("out/unicode/日本.txt")));
        }

        [Trait("Category", "CrossVersion")]
        [Theory]
        [InlineData("powershell")]
        [InlineData("pwsh")]
        public async Task RejectsUnsafeArchivesBeforePublishingAndChecksActualContent(string shell)
        {
            using var project = new Project();
            var cases = new List<byte[]>();
            foreach (string name in new[] { "../outside.txt", "/absolute", "C:/root", "C:relative", "a/../../b", "a//b", "a/./b", "a:stream", "CON", "com1.txt", "a.", "a ", "a\0b", "\\server\\share" })
            {
                cases.Add(RawZip(Entry(name)));
            }

            cases.Add(RawZip(Entry("A"), Entry("a")));
            cases.Add(RawZip(Entry("a/b"), Entry("a")));
            cases.Add(RawZip(Entry("a"), Entry("a/b")));
            cases.Add(RawZip(Entry("a/") with { Data = [] }, Entry("A/") with { Data = [] }));
            cases.Add(RawZip(Entry("link") with { Attributes = 0xa1ff0000 }));
            cases.Add(RawZip(Entry("link") with { Attributes = (uint)FileAttributes.ReparsePoint }));
            cases.Add(RawZip(Entry("dir/")));
            cases.Add(RawZip(Entry("encrypted") with { Flags = 1 }));
            cases.Add(RawZip(Entry("unsupported") with { Method = 99 }));
            cases.Add(RawZip(new RawEntry([0xed, 0xa0, 0x80], [], Flags: 0x800)));
            cases.Add(RawZip(Entry("crc") with { Crc = 1 }));
            cases.Add(RawZip(Entry("size") with { Size = 1 }));
            cases.Add(RawZip(Entry("deflate") with { Method = 8 }));
            cases.Add(RawZip(Entry("truncated"))[..^8]);
            var disagreement = RawZip(Entry("local")); disagreement[30] = (byte)'X'; cases.Add(disagreement);
            var oversizedMetadata = RawZip(Entry("metadata"));
            BitConverter.GetBytes(64u * 1024 * 1024 + 1).CopyTo(oversizedMetadata, oversizedMetadata.Length - 22 + 12);
            cases.Add(oversizedMetadata);
            var root = project.DirectoryPath.Replace('\\', '/');
            var source = "import zip; import encoding; import fs; import test\noptions := zip.options(encoding.utf8)\n";
            for (int i = 0; i < cases.Count; i++) {
                File.WriteAllBytes(project.PathFor($"bad-{i}.zip"), cases[i]);
                source += $"if true {{ rejected := false\ntry {{ zip.extract('{root}/bad-{i}.zip', '{root}/out-{i}', options) }} catch err {{ rejected = true }}\n" +
                    $"test.assert(rejected)\ntest.assert(!fs.is_dir('{root}/out-{i}'))\n}}\n";
            }
            File.WriteAllBytes(project.PathFor("small.zip"), RawZip(Entry("first"), Entry("second")));
            foreach (var condition in new[] { "options.max_entries = 1", "options.max_total_bytes = 5", "options.max_entries = 0", "options.max_entries = 2147483648", "options.max_total_bytes = 0", "options.name_encoding = encoding.utf16_le" })
            {
                source += $"if true {{ options := zip.options(encoding.utf8)\n{condition}\nrejected := false\ntry {{ zip.extract('{root}/small.zip', '{root}/limited', options) }} catch err {{ rejected = true }}\ntest.assert(rejected)\n}}\n";
            }

            project.Write("main.rukbat", source + "print('passed')\n");
            await Verify(project, shell);
            Assert.False(Directory.Exists(project.PathFor("limited")));
            Assert.False(File.Exists(project.PathFor("outside.txt")));
            Assert.Empty(Directory.GetFileSystemEntries(project.DirectoryPath, ".rukbat-zip-*.tmp"));
        }

        [Trait("Category", "CrossVersion")]
        [Theory]
        [InlineData("powershell")]
        [InlineData("pwsh")]
        public async Task CreationFailuresPreserveExistingOutputsAndCleanTemporaryFiles(string shell)
        {
            using var project = new Project();
            project.Write("source/file", "input");
            project.Write("existing.zip", "keep");
            Directory.CreateDirectory(project.PathFor("existing-dir"));
            var root = project.DirectoryPath.Replace('\\', '/');
            project.Write("main.rukbat", $$"""
            import zip; import encoding; import test
            for output in ['{{root}}/existing.zip', '{{root}}/existing-dir', '{{root}}/missing/out.zip', '{{root}}/source/self.zip', '{{root}}/locked.zip'] {
                rejected := false
                try { zip.create('{{root}}/source', output) } catch err { rejected = true }
                test.assert(rejected)
            }
            print('passed')
            """);
            using (var locked = new FileStream(project.PathFor("source/file"), FileMode.Open, FileAccess.Read, FileShare.None))
            {
                await Verify(project, shell);
            }

            Assert.Equal("keep", File.ReadAllText(project.PathFor("existing.zip")));
            Assert.False(File.Exists(project.PathFor("locked.zip")));
            Assert.Empty(Directory.GetFileSystemEntries(project.DirectoryPath, ".rukbat-zip-*.tmp"));
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeviceIoControl(SafeFileHandle handle, uint code, IntPtr input, int inputLength,
            IntPtr output, int outputLength, out int returned, IntPtr overlapped);

        [Trait("Category", "CrossVersion")]
        [Theory]
        [InlineData("powershell")]
        [InlineData("pwsh")]
        public async Task ReadsZip64ArchivesWithOffsetsAboveFourGiB(string shell)
        {
            using var project = new Project();
            var path = project.PathFor("large.zip");
            var name = "data.txt"u8.ToArray();
            long offset = (long)uint.MaxValue + 4096;
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            using (var writer = new BinaryWriter(stream)) {
                Assert.True(DeviceIoControl(stream.SafeFileHandle, 0x900c4, IntPtr.Zero, 0, IntPtr.Zero, 0, out _, IntPtr.Zero),
                    "Cannot mark the temporary ZIP64 fixture sparse: " + Marshal.GetLastWin32Error());
                stream.Position = offset;
                var ordinary = RawZip(Entry("data.txt"));
                writer.Write(ordinary.AsSpan(0, 30 + name.Length + 3));
                long central = stream.Position;
                writer.Write(0x02014b50u); writer.Write((ushort)45); writer.Write((ushort)45); writer.Write((ushort)0x800); writer.Write((ushort)0);
                writer.Write(0u); writer.Write(0x352441c2u); writer.Write(3u); writer.Write(3u);
                writer.Write((ushort)name.Length); writer.Write((ushort)12); writer.Write((ushort)0); writer.Write((ushort)0); writer.Write((ushort)0);
                writer.Write(0u); writer.Write(uint.MaxValue); writer.Write(name);
                writer.Write((ushort)1); writer.Write((ushort)8); writer.Write((ulong)offset);
                long zip64 = stream.Position;
                writer.Write(0x06064b50u); writer.Write(44ul); writer.Write((ushort)45); writer.Write((ushort)45);
                writer.Write(0u); writer.Write(0u); writer.Write(1ul); writer.Write(1ul); writer.Write((ulong)(zip64 - central)); writer.Write((ulong)central);
                writer.Write(0x07064b50u); writer.Write(0u); writer.Write((ulong)zip64); writer.Write(1u);
                writer.Write(0x06054b50u); writer.Write((ushort)0); writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)1);
                writer.Write((uint)(zip64 - central)); writer.Write(uint.MaxValue); writer.Write((ushort)0);
            }
            Assert.True(new FileInfo(path).Length > uint.MaxValue);
            var root = project.DirectoryPath.Replace('\\', '/');
            project.Write("main.rukbat", $$"""
            import zip; import encoding; import fs; import test
            options := zip.options(encoding.utf8)
            entries := zip.list('{{root}}/large.zip', options)
            test.assert(entries.length == 1)
            test.assert(entries[0].size == 3)
            zip.extract('{{root}}/large.zip', '{{root}}/out', options)
            test.assert(fs.read_text('{{root}}/out/data.txt', encoding.utf8) == 'abc')
            print('passed')
            """);
            await Verify(project, shell);
        }

        [Trait("Category", "CrossVersion")]
        [Theory]
        [InlineData("powershell")]
        [InlineData("pwsh")]
        public async Task RejectsSourceAndOutputJunctions(string shell)
        {
            using var project = new Project();
            project.Write("source/data", "data");
            project.Write("target/keep", "unchanged");
            var root = project.DirectoryPath.Replace('\\', '/');
            project.Write("main.rukbat", $$"""
            import zip; import test
            powershell<void>('New-Item', ItemType: 'Junction', Path: '{{root}}/source/link', Target: {{Literal(project.PathFor("target"))}})
            source_link := false
            try { zip.create('{{root}}/source', '{{root}}/bad.zip') } catch err { source_link = true }
            test.assert(source_link)
            output_link := false
            try { zip.create('{{root}}/target', '{{root}}/source/link/bad.zip') } catch err { output_link = true }
            test.assert(output_link)
            print('passed')
            """);
            try {
                await Verify(project, shell);
                Assert.False(File.Exists(project.PathFor("bad.zip")));
                Assert.False(File.Exists(project.PathFor("target/bad.zip")));
                Assert.Equal("unchanged", File.ReadAllText(project.PathFor("source/link/keep")));
                Assert.Equal("unchanged", File.ReadAllText(project.PathFor("target/keep")));
                Assert.Empty(Directory.GetFileSystemEntries(project.DirectoryPath, ".rukbat-zip-*.tmp"));
            } finally {
                if (Directory.Exists(project.PathFor("source/link")))
                {
                    Directory.Delete(project.PathFor("source/link"));
                }
            }
        }

        private static async Task Verify(Project project, string shell)
        {
            var compiled = project.Compile();
            Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));
            var result = shell == "powershell" ? await project.RunOutputWithWindowsPowerShell(compiled.Output!) : await project.RunOutput(compiled.Output!);
            Assert.True(result.ExitCode == 0, result.Error);
            Assert.Empty(result.Error);
            Assert.Equal("passed\n", result.Output);
        }
    }
}
