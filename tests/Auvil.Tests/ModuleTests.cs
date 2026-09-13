namespace Auvil.Tests;

public partial class CompilerTests
{
    [Fact]
    public async Task BundlesDirectoryModulesWithSharedTypesAndSingleInitialization()
    {
        using var project = new Project();
        project.Write("models/user.auv", "print('init'); struct User { name: string }");
        project.Write("services/users.auv", "import '../models/user.auv' as model\nfunc load(): model.User { return model.User(name: 'Taro') }");
        project.Write("main.auv", "import './models/user.auv' as model\nimport './services/users.auv' as users\nimport './services/../models/user.auv' as same\nu: model.User = users.load(); v: same.User = u; print(v.name)");
        var compiled = project.Compile();
        Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));
        foreach (var file in Directory.EnumerateFiles(project.DirectoryPath, "*.auv", SearchOption.AllDirectories)) File.Delete(file);
        var result = await project.RunOutput(compiled.Output!);
        Assert.True(result.ExitCode == 0, result.Error);
        Assert.Equal("init\nTaro", result.Output.TrimEnd('\n'));
    }

    [Fact]
    public async Task SupportsSameFileNamesAndLegacyImportAliases()
    {
        using var project = new Project();
        project.Write("left/math.auv", "fn get(): int { return 1 }");
        project.Write("right/math.auv", "fn get(): int { return 2 }");
        project.Write("math.auv", "fn get(): int { return 3 }");
        project.Write("main.auv", "import 'left/math.auv' as left\nimport 'right/math.auv' as right\nimport math as local\nprint(left.get()); print(right.get()); print(local.get())");
        var result = await project.Run();
        Assert.True(result.ExitCode == 0, result.Error);
        Assert.Equal("1\n2\n3", result.Output.TrimEnd('\n'));
    }

    [Fact]
    public void ReportsDirectoryImportCyclesAndSourceLocations()
    {
        using var project = new Project();
        project.Write("main.auv", "import './services/users.auv' as users");
        project.Write("services/users.auv", "import '../main.auv' as app");
        var cycle = Assert.Single(project.Compile().Diagnostics);
        Assert.Equal("AUV3001", cycle.Code);
        Assert.Equal(Path.GetFullPath(project.PathFor("services/users.auv")), cycle.FilePath);
        project.Write("services/users.auv", "fn load(): int { return 'wrong' }");
        var error = Assert.Single(project.Compile().Diagnostics);
        Assert.Equal("AUV2001", error.Code);
        Assert.Equal(Path.GetFullPath(project.PathFor("services/users.auv")), error.FilePath);
    }

    [Theory]
    [InlineData("import 'missing.auv' as missing", "AUV3002")]
    [InlineData("import '' as bad", "AUV3002")]
    [InlineData("import '/absolute.auv' as bad", "AUV3002")]
    [InlineData("import 'C:/absolute.auv' as bad", "AUV3002")]
    [InlineData("import 'models\\\\user.auv' as bad", "AUV3002")]
    [InlineData("import 'bad\\n.auv' as bad", "AUV3002")]
    [InlineData("import './models' as bad", "AUV3002")]
    [InlineData("import './models/user.auv'", "AUV1000")]
    [InlineData("import './models/user.auv' as fn", "AUV1000")]
    [InlineData("import './models/user.auv' as model\nimport './models/user.auv' as model", "AUV3000")]
    [InlineData("import './models/user.auv' as model\nmodel: int = 1", "AUV2002")]
    [InlineData("fn f(): void { import './models/user.auv' as model }", "AUV3000")]
    public void RejectsInvalidDirectoryImports(string source, string code)
    {
        using var project = new Project();
        project.Write("models/user.auv", "struct User { name: string }");
        project.Write("main.auv", source);
        var result = project.Compile();
        Assert.False(result.Success);
        Assert.Null(result.Output);
        Assert.Contains(result.Diagnostics, d => d.Code == code);
    }
}
