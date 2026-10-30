using System.Text;

namespace Rukbat.Tests
{
    public sealed class PathApiTests : TestBase
    {
        [Trait("Category", "Unit")]
        [Fact]
        public void PathOperationsHaveFixedTypesAndNeedOnlyThePathRuntime()
        {
            var output = Success("""
            import path
            working: string = path.working_dir()
            absolute: string = path.resolve(working, 'data.csv')
            relative: string = path.relative(working, absolute)
            is_absolute: bool = path.is_absolute(absolute)
            stem: string = path.stem(relative)
            output: string = path.change_extension(relative, '.json')
            """);
            Assert.DoesNotContain("'fs.copy_file'", output);
            Assert.DoesNotContain("class RukbatMoveBatch", output);
            Assert.DoesNotContain("Add-Type", output);
        }

        [Trait("Category", "Unit")]
        [Theory]
        [InlineData("path.working_dir('x')")]
        [InlineData("path.resolve('C:/work')")]
        [InlineData("path.resolve(1, 'x')")]
        [InlineData("path.resolve('C:/work', null)")]
        [InlineData("path.relative('C:/work', 1)")]
        [InlineData("path.relative('C:/work', 'C:/work/x', 'x')")]
        [InlineData("path.is_absolute()")]
        [InlineData("path.is_absolute(1)")]
        [InlineData("path.stem(['x'])")]
        [InlineData("path.change_extension('x')")]
        [InlineData("path.change_extension('x', null)")]
        [InlineData("value: int = path.resolve('C:/work', 'x')")]
        [InlineData("value: string = path.is_absolute('C:/work')")]
        public void InvalidPathCallsAreRejectedBeforeEmission(string source)
            => Assert.NotEmpty(new Compiler().Check("import path\n" + source));

        [Trait("Category", "CrossVersion")]
        [Trait("Category", "Runtime")]
        [Theory]
        [InlineData("powershell")]
        [InlineData("pwsh")]
        public async Task ResolveAndRelativeUseExplicitBasesAndWindowsPathComponents(string shell)
        {
            await AssertProgram(shell, """
            import path
            import test
            test.assert(path.resolve('C:/work', 'data/../missing/a.csv') == 'C:\\work\\missing\\a.csv')
            test.assert(path.resolve('C:/work', 'D:/other/./a.csv') == 'D:\\other\\a.csv')
            test.assert(path.resolve('C:/work/deep', '/data/a.csv') == 'C:\\data\\a.csv')
            test.assert(path.resolve('C:/work', '.') == 'C:\\work')
            test.assert(path.resolve('C:/work/', '..') == 'C:\\')
            test.assert(path.resolve('//server/share/work', '/data/a.csv') == '\\\\server\\share\\data\\a.csv')
            test.assert(path.resolve('//server/share', '.') == '\\\\server\\share\\')
            test.assert(path.resolve('C:/work', '%TEMP%/~/[literal]/日本語.csv') == 'C:\\work\\%TEMP%\\~\\[literal]\\日本語.csv')
            test.assert(path.relative('C:/work', 'C:/work/data/a.csv') == 'data\\a.csv')
            test.assert(path.relative('C:/work/data', 'C:/work/out/a.csv') == '..\\out\\a.csv')
            test.assert(path.relative('C:/work', 'c:/WORK/') == '.')
            test.assert(path.relative('C:/work/deep', 'C:/') == '..\\..')
            test.assert(path.relative('C:/work', 'C:/work2/a.csv') == '..\\work2\\a.csv')
            test.assert(path.relative('C:/', 'C:/data/') == 'data')
            test.assert(path.relative('//SERVER/share/work', '//server/SHARE/out/a.csv') == '..\\out\\a.csv')
            test.assert(path.relative('//server/share', '//server/share/') == '.')
            test.assert(!path.equal('C:/work', 'C:/work/'))
            test.assert(path.extension('.env') == '.env')
            print('passed')
            """);
        }

        [Trait("Category", "CrossVersion")]
        [Trait("Category", "Runtime")]
        [Theory]
        [InlineData("powershell")]
        [InlineData("pwsh")]
        public async Task AbsolutePredicatesAndConversionsRejectUnsupportedPaths(string shell)
        {
            await AssertProgram(shell, """
            import path
            import test
            fn reject(value: string): void {
                try { path.resolve('C:/work', value) } catch err { return }
                test.fail('Expected path.resolve to reject ' + value)
            }
            fn reject_relative(base: string, target: string): void {
                try { path.relative(base, target) } catch err { return }
                test.fail('Expected path.relative to reject its inputs')
            }
            test.assert(path.is_absolute('C:/work'))
            test.assert(path.is_absolute('//server/share'))
            test.assert(path.is_absolute('//server/share/data'))
            test.assert(!path.is_absolute('/data'))
            test.assert(!path.is_absolute('data/file.csv'))
            for value in ['', '   ', 'C:foo', '//server', '//', '///server/share', '//./C:/file', '//?/C:/file', 'Registry::HKEY_LOCAL_MACHINE', 'C:/file:stream', 'bad|path', 'bad\npath'] {
                test.assert(!path.is_absolute(value))
                reject(value)
            }
            for base in ['', '.', '/work', 'C:work', '//server'] {
                failed := false
                try { path.resolve(base, 'C:/valid') } catch err { failed = true }
                test.assert(failed)
                reject_relative(base, 'C:/valid')
            }
            reject_relative('C:/work', 'relative.csv')
            reject_relative('C:/work', 'D:/work')
            reject_relative('//server/share/work', '//server/other/work')
            reject_relative('//server/share/work', '//other/share/work')
            reject_relative('C:/work', 'C:/file:stream')
            print('passed')
            """);
        }

        [Trait("Category", "CrossVersion")]
        [Trait("Category", "Runtime")]
        [Theory]
        [InlineData("powershell")]
        [InlineData("pwsh")]
        public async Task FilenameOperationsPreservePathSpellingAndExistingExtensionRules(string shell)
        {
            await AssertProgram(shell, """
            import path
            import test
            test.assert(path.stem('dir/report.csv') == 'report')
            test.assert(path.stem('archive.tar.gz') == 'archive.tar')
            test.assert(path.stem('README') == 'README')
            test.assert(path.stem('.env') == '')
            test.assert(path.stem('report.') == 'report')
            for value in ['C:/', '//server/share', '//server/share/', 'dir/', '.', '..', 'dir/.', 'dir/..'] {
                test.assert(path.stem(value) == '')
            }
            test.assert(path.change_extension('dir/report.csv', '.json') == 'dir/report.json')
            test.assert(path.change_extension('dir\\report.csv', '.json') == 'dir\\report.json')
            test.assert(path.change_extension('../dir/report.csv', '') == '../dir/report')
            test.assert(path.change_extension('archive.tar.gz', '.zip') == 'archive.tar.zip')
            test.assert(path.change_extension('README', '.tar.gz') == 'README.tar.gz')
            test.assert(path.change_extension('.env', '.json') == '.json')
            test.assert(path.change_extension('report.', '.csv') == 'report.csv')
            test.assert(path.change_extension('report.', '') == 'report')
            test.assert(path.change_extension('README', '') == 'README')
            test.assert(path.change_extension('C:/dir/日本語.csv', '.json') == 'C:/dir/日本語.json')
            test.assert(path.change_extension('//server/share/file.csv', '.json') == '//server/share/file.json')
            fn reject_extension(extension: string): void {
                try { path.change_extension('report.csv', extension) } catch err { return }
                test.fail('Expected invalid extension to fail')
            }
            for extension in ['json', '.', './json', '.\\json', '.json:stream', '.bad|extension', '.bad\nextension'] {
                reject_extension(extension)
            }
            for value in ['', ' ', 'C:/', '//server/share', '//server/share/', 'dir/', '.', '..', 'dir/.', 'dir/..', '.env', 'dir/.env', 'C:report.csv', 'bad|path'] {
                failed := false
                try { path.change_extension(value, '') } catch err { failed = true }
                test.assert(failed)
            }
            print('passed')
            """);
        }

        [Trait("Category", "CrossVersion")]
        [Trait("Category", "Runtime")]
        [Theory]
        [InlineData("powershell")]
        [InlineData("pwsh")]
        public async Task RecursiveFileCopiesPreserveTheInputDirectoryTree(string shell)
        {
            using var project = new Project();
            project.Write("input/direct.csv", "direct");
            project.Write("input/nested/日本語.csv", "nested");
            project.Write("main.rukbat", """
            import path
            import fs
            root := path.resolve(path.working_dir(), 'input')
            output := path.resolve(path.working_dir(), 'output')
            for file in fs.list_files_recursive(root, '*.csv') {
                relative := path.relative(root, file)
                target := path.resolve(output, relative)
                fs.create_dir_all(path.parent(target))
                fs.copy_file(file, target)
            }
            print('passed')
            """);
            await AssertProgram(shell, project);
            Assert.Equal("direct", File.ReadAllText(project.PathFor("output/direct.csv")));
            Assert.Equal("nested", File.ReadAllText(project.PathFor("output/nested/日本語.csv")));
            Assert.Equal(2, Directory.GetFiles(project.PathFor("output"), "*", SearchOption.AllDirectories).Length);
        }

        private static async Task AssertProgram(string shell, string source)
        {
            using var project = new Project();
            project.Write("main.rukbat", source);
            await AssertProgram(shell, project);
        }

        private static async Task AssertProgram(string shell, Project project)
        {
            var compiled = project.Compile();
            Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));
            var script = project.PathFor("job.ps1");
            File.WriteAllText(script, compiled.Output, new UTF8Encoding(true));
            var result = await RunDeployed(shell, script, project.DirectoryPath, project.DirectoryPath);
            Assert.True(result.ExitCode == 0, result.Error);
            Assert.Equal("", result.Error);
            Assert.Equal("passed\n", result.Output);
        }
    }
}
