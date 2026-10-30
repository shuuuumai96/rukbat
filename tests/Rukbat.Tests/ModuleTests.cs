namespace Rukbat.Tests
{
    public class ModuleTests : TestBase
    {
        [Trait("Category", "Runtime")]
        [Fact]
        public async Task BundlesDirectoryModulesWithSharedTypesAndSingleInitialization()
        {
            using var project = new Project();
            project.Write("models/user.rukbat", "print('init'); struct User { name: string }");
            project.Write("services/users.rukbat", "import '../models/user.rukbat' as model\nfn load(): model.User { return model.User {name: 'Taro'} }");
            project.Write("main.rukbat", "import './models/user.rukbat' as model\nimport './services/users.rukbat' as users\nimport './services/../models/user.rukbat' as same\nu: model.User = users.load(); v: same.User = u; print(v.name)");
            var compiled = project.Compile();
            Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));
            foreach (var file in Directory.EnumerateFiles(project.DirectoryPath, "*.rukbat", SearchOption.AllDirectories))
            {
                File.Delete(file);
            }

            var result = await project.RunOutput(compiled.Output!);
            Assert.True(result.ExitCode == 0, result.Error);
            Assert.Equal("init\nTaro", result.Output.TrimEnd('\n'));
        }

        [Trait("Category", "Runtime")]
        [Fact]
        public async Task SupportsSameFileNamesAndLegacyImportAliases()
        {
            using var project = new Project();
            project.Write("left/math.rukbat", "fn get(): int { return 1 }");
            project.Write("right/math.rukbat", "fn get(): int { return 2 }");
            project.Write("math.rukbat", "fn get(): int { return 3 }");
            project.Write("main.rukbat", "import './left/math.rukbat' as left\nimport './right/math.rukbat' as right\nimport './math.rukbat' as local\nprint(left.get()); print(right.get()); print(local.get())");
            var result = await project.Run();
            Assert.True(result.ExitCode == 0, result.Error);
            Assert.Equal("1\n2\n3", result.Output.TrimEnd('\n'));
        }

        [Trait("Category", "Unit")]
        [Fact]
        public void ReportsDirectoryImportCyclesAndSourceLocations()
        {
            using var project = new Project();
            project.Write("main.rukbat", "import './services/users.rukbat' as users");
            project.Write("services/users.rukbat", "import '../main.rukbat' as app");
            var cycle = Assert.Single(project.Compile().Diagnostics);
            Assert.Equal("RUK3001", cycle.Code);
            Assert.Equal(Path.GetFullPath(project.PathFor("services/users.rukbat")), cycle.FilePath);
            project.Write("services/users.rukbat", "fn load(): int { return 'wrong' }");
            var error = Assert.Single(project.Compile().Diagnostics);
            Assert.Equal("RUK2001", error.Code);
            Assert.Equal(Path.GetFullPath(project.PathFor("services/users.rukbat")), error.FilePath);
        }

        [Trait("Category", "Unit")]
        [Theory]
        [InlineData("import 'missing.rukbat' as missing", "RUK3002")]
        [InlineData("import '' as bad", "RUK3002")]
        [InlineData("import '/absolute.rukbat' as bad", "RUK3002")]
        [InlineData("import 'C:/absolute.rukbat' as bad", "RUK3002")]
        [InlineData("import 'models\\\\user.rukbat' as bad", "RUK3002")]
        [InlineData("import 'bad\\n.rukbat' as bad", "RUK3002")]
        [InlineData("import './models' as bad", "RUK3002")]
        [InlineData("import './models/user.rukbat'", "RUK1000")]
        [InlineData("import './models/user.rukbat' as fn", "RUK1000")]
        [InlineData("import './models/user.rukbat' as model\nimport './models/user.rukbat' as model", "RUK3000")]
        [InlineData("import './models/user.rukbat' as model\nmodel: int = 1", "RUK2002")]
        [InlineData("fn f(): void { import './models/user.rukbat' as model }", "RUK3000")]
        public void RejectsInvalidDirectoryImports(string source, string code)
        {
            using var project = new Project();
            project.Write("models/user.rukbat", "struct User { name: string }");
            project.Write("main.rukbat", source);
            var result = project.Compile();
            Assert.False(result.Success);
            Assert.Null(result.Output);
            Assert.Contains(result.Diagnostics, d => d.Code == code);
        }
    }
}
