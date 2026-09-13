namespace Auvil.Tests;

public partial class CompilerTests
{
    [Theory]
    [InlineData("struct User { name: string; age: int }; user: User = User(name: 'Taro', age: 30); print(user.name)", "Taro")]
    [InlineData("struct User { age: int; fn is_adult(): bool { return self.age >= 20 } }; user: User = User(age: 30); print(user.is_adult())", "True")]
    [InlineData("struct Counter { value: int; fn add(amount: int): int { return self.value + amount } }; c: Counter = Counter(value: 2); print(c.add(3))", "5")]
    [InlineData("struct Counter { value: int; fn add(amount: int): void { self.value = self.value + amount } }; a: Counter = Counter(value: 1); b: Counter = a; b.add(4); print(a.value); print(b.value)", "1\n5")]
    [InlineData("struct User { age: int }; a: User = User(age: 30); b: User = a; b.age = 31; print(a.age); print(b.age)", "30\n31")]
    [InlineData("struct User { age: int }; a: User = User(age: 30); b: User = User(age: 0); b = a; b.age = 31; print(a.age); print(b.age)", "30\n31")]
    [InlineData("struct U { age: int }; fn change(u: U): U { u.age = 31; return u }; a: U = U(age: 30); b: U = change(a); print(a.age); print(b.age)", "30\n31")]
    [InlineData("struct U { age: int }; source: U = U(age: 30); fn get(): U { return source }; b: U = get(); b.age = 31; print(source.age)", "30")]
    [InlineData("struct U { age: int }; struct Box { user: U }; u: U = U(age: 30); a: Box = Box(user: u); b: Box = a; b.user.age = 31; print(u.age); print(a.user.age); print(b.user.age)", "30\n30\n31")]
    [InlineData("struct U { age: int }; struct Box { user: U }; a: Box = Box(user: U(age: 1)); b: U = a.user; b.age = 2; print(a.user.age); a.user = b; b.age = 3; print(a.user.age)", "1\n2")]
    [InlineData("struct U { age: int; fn grow(): void { self.age = self.age + 1 } }; struct Box { user: U }; a: Box = Box(user: U(age: 1)); a.user.grow(); print(a.user.age)", "2")]
    [InlineData("struct U { age: int; fn grow(): void { self.age = self.age + 1 }; fn twice(): void { self.grow(); self.grow() }; fn snapshot(): U { return self } }; a: U = U(age: 1); b: U = a.snapshot(); a.twice(); print(a.age); print(b.age)", "3\n1")]
    [InlineData("struct U { Age: int; age: int; Keys: string; fn get(): int { return self.Age + self.age } }; a: U = U(age: 1, Keys: 'ok', Age: 2); print(a.get()); print(a.Keys)", "3\nok")]
    [InlineData("struct U { age: int; fn answer(): int { 100; print('log'); return self.age } }; a: U = U(age: 42); print(a.answer())", "log\n42")]
    [InlineData("struct Empty {}; a: Empty = Empty(); b: Empty = a; print('ok')", "ok")]
    [InlineData("struct U { age: int }; fn make(): U { return U(age: 30) }; print(make().age); print(U(age: 31).age)", "30\n31")]
    [InlineData("struct U { age: int; fn grow(): U { self.age = self.age + 1; return self } }; print(U(age: 1).grow().age)", "2")]
    public async Task ExecutesStructValuesAndMethods(string source, string expected)
    {
        using var project = new Project(); project.Write("main.auv", source);
        var result = await project.Run();
        Assert.True(result.ExitCode == 0, result.Error);
        Assert.Equal(expected, result.Output.TrimEnd('\n'));
    }

    [Theory]
    [InlineData("a: Missing = Missing()", "Unknown")]
    [InlineData("struct U {}; struct U {}", "Duplicate struct")]
    [InlineData("struct U { age: int; age: int }", "Duplicate field")]
    [InlineData("struct U { age: void }", "cannot have type void")]
    [InlineData("struct U { age: Missing }", "Unknown struct/type")]
    [InlineData("struct U { age: int }; a: U = U()", "Missing required field 'age'")]
    [InlineData("struct U { age: int }; a: U = U(age: 1, typo: 2)", "Unknown field 'typo'")]
    [InlineData("struct U { age: int }; a: U = U(age: '30')", "Expected int for field 'age', got string")]
    [InlineData("struct U { age: int }; a: U = U(age: 1, age: 2)", "Duplicate initializer")]
    [InlineData("struct U { age: int }; a: U = U(1)", "requires named field arguments")]
    [InlineData("struct U { age: int }; a: U = U(age: 1); a.age = 'bad'", "Expected int for field 'age'")]
    [InlineData("struct U { age: int }; a: U = U(age: 1); print(a.typo)", "Unknown field 'typo'")]
    [InlineData("struct U { age: int }; a: U = U(age: 1); print(a.missing())", "Unknown method 'missing'")]
    [InlineData("a: int = 1; print(a.age)", "Cannot access field")]
    [InlineData("a: int = 1; print(a.go())", "Unknown method")]
    [InlineData("print(self)", "self is available only")]
    [InlineData("fn f(): int { return self.age }", "self is available only")]
    [InlineData("struct U { fn f(): void {}; fn f(): void {} }", "Duplicate method")]
    [InlineData("struct U { f: int; fn f(): void {} }", "conflicts with a field")]
    [InlineData("struct U { fn f(): int { return 'bad' } }", "Expected int")]
    [InlineData("struct U { fn f(): int {} }", "must return a value")]
    [InlineData("struct U { fn f(x: int): int { return x } }; a: U = U(); a.f()", "expects 1 arguments")]
    [InlineData("struct U { fn f(x: int): int { return x } }; a: U = U(); a.f('bad')", "Expected int for argument")]
    [InlineData("struct U { fn f(): void { self = U() } }", "identifier")]
    [InlineData("struct U { age: int }; U(age: 1).age = 2", "not a temporary")]
    [InlineData("struct A { age: int }; struct B { age: int }; a: A = B(age: 1)", "Expected A")]
    [InlineData("struct U { next: U }", "Recursive value field")]
    [InlineData("struct A { b: B }; struct B { a: A }", "Recursive value field")]
    [InlineData("struct U {}; powershell<void>('Write-Output', U())", "cannot be passed directly")]
    [InlineData("struct U {}; print(U())", "Print individual struct fields")]
    [InlineData("struct U {}; a: bool = U() == U()", "cannot combine")]
    [InlineData("struct int {}", "conflicts with an existing type")]
    [InlineData("struct U {}; fn U(): void {}", "conflicts with a struct")]
    [InlineData("fn f(): void { struct U {} }", "only at module level")]
    public void RejectsInvalidStructPrograms(string source, string message)
    {
        var result = Compile(source);
        Assert.False(result.Success);
        Assert.Null(result.Output);
        Assert.Contains(result.Diagnostics, d => d.Message.Contains(message, StringComparison.OrdinalIgnoreCase));
        Assert.All(result.Diagnostics, d => { Assert.True(d.Line > 0); Assert.True(d.Column > 0); });
    }

    [Fact]
    public async Task ImportsStructTypesMethodsAndValueArguments()
    {
        using var project = new Project();
        project.Write("model.auv", "struct U { age: int; fn grow(): void { self.age = self.age + 1 } }; fn grow(u: U): U { u.grow(); return u }");
        project.Write("main.auv", "import model\na: model.U = model.U(age: 30); b: model.U = model.grow(a); b.grow(); print(a.age); print(b.age)");
        var result = await project.Run();
        Assert.True(result.ExitCode == 0, result.Error);
        Assert.Equal("30\n32", result.Output.TrimEnd('\n'));
    }

    [Fact]
    public void StructIdentityIncludesItsDefiningModule()
    {
        using var project = new Project();
        project.Write("left.auv", "struct U { age: int }");
        project.Write("right.auv", "struct U { age: int }");
        project.Write("main.auv", "import left\nimport right\nx: left.U = right.U(age: 1)");
        Assert.Contains(project.Compile().Diagnostics, d => d.Code == "AUV2001" && d.Message.Contains("left.U") && d.Message.Contains("right.U"));
    }
}
