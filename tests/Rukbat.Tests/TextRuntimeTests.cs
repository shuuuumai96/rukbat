namespace Rukbat.Tests;

public class TextRuntimeTests
{
    private const long MaximumCountingAllocationBytes = 1024;
    private const long StringAllocationOverheadBytes = 1024;
    private const int LargeInputLength = 1024 * 1024;

    [Trait("Category", "Unit")]
    [Theory]
    [InlineData("", 0L)]
    [InlineData("abc", 3L)]
    [InlineData("\0", 1L)]
    [InlineData("A\ud83d\ude00B\u0301", 4L)]
    [InlineData("\ud800\udc00", 1L)]
    [InlineData("\udbff\udfff", 1L)]
    [InlineData("\ud7ff\ue000", 2L)]
    [InlineData("\ud83d\ude00\ud83d\ude00", 2L)]
    public void LengthCountsUnicodeCodePoints(string value, long expected)
        => Assert.Equal(expected, RukbatText.Length(value));

    [Trait("Category", "Unit")]
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LengthDoesNotAllocateBoundaryTables(bool supplementary)
    {
        var value = supplementary
            ? string.Concat(Enumerable.Repeat("A\ud83d\ude00\u0301", LargeInputLength / 4))
            : new string('x', LargeInputLength);
        var expected = supplementary ? LargeInputLength / 4 * 3L : LargeInputLength;
        RukbatText.Length(value);

        var before = GC.GetAllocatedBytesForCurrentThread();
        var actual = RukbatText.Length(value);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(expected, actual);
        Assert.InRange(allocated, 0, MaximumCountingAllocationBytes);
    }

    [Trait("Category", "Unit")]
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CaseConversionAllocatesOnlyTheResultString(bool upper)
    {
        var value = new string(upper ? 'x' : 'X', LargeInputLength);
        Func<string, string> convert = upper ? RukbatText.Upper : RukbatText.Lower;
        convert(value);

        var before = GC.GetAllocatedBytesForCurrentThread();
        var actual = convert(value);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(value.Length, actual.Length);
        Assert.Equal(upper ? 'X' : 'x', actual[0]);
        Assert.InRange(allocated, 0, value.Length * sizeof(char) + StringAllocationOverheadBytes);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void SearchValidationDoesNotAllocateABoundaryTable()
    {
        var search = new string('x', LargeInputLength);
        RukbatText.IndexOf("", search);

        var before = GC.GetAllocatedBytesForCurrentThread();
        var actual = RukbatText.IndexOf("", search);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Null(actual);
        Assert.InRange(allocated, 0, MaximumCountingAllocationBytes);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void UnicodeOperationsRejectUnpairedSurrogates()
    {
        // Construct these in-process so test serialization cannot replace invalid UTF-16.
        foreach (var value in new[] { "\ud800", "\udc00", "\ud800x", "x\ud800", "\ud800\ud800", "\udc00\ud800", "\ud83d\ude00\udc00" })
        {
            Assert.Throws<ArgumentException>(() => RukbatText.Length(value));
            Assert.Throws<ArgumentException>(() => RukbatText.Upper(value));
            Assert.Throws<ArgumentException>(() => RukbatText.Lower(value));
            Assert.Throws<ArgumentException>(() => RukbatText.Slice(value, 0, 0));
            Assert.Throws<ArgumentException>(() => RukbatText.IndexOf(value, ""));
            Assert.Throws<ArgumentException>(() => RukbatText.IndexOf("", value));
        }
    }
}
