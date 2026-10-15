#nullable disable
using System.Text;

namespace Rukbat.Tests;

public class FileLineRuntimeTests
{
    [Trait("Category", "Unit")]
    [Fact]
    public void ShortReadsAndEveryUnicodeBoundaryMatchWholeSplitting()
    {
        var encodings = new[] { ("utf-8", (Encoding)new UTF8Encoding(false, true)),
            ("utf-16le", new UnicodeEncoding(false, false, true)), ("utf-16be", new UnicodeEncoding(true, false, true)),
            ("utf-32le", new UTF32Encoding(false, false, true)), ("utf-32be", new UTF32Encoding(true, false, true)) };
        var texts = new[] { "", "a\n", "\r\r\n", "\n", "a\r\nb\rc\n", "日😀\r\n\0\u2028", new string('x', 131077) };
        foreach (var (name, encoding) in encodings) foreach (var text in texts)
        {
            var bytes = encoding.GetBytes(text);
            var expected = text.Length == 0 ? Array.Empty<string>() : text.Split(["\r\n", "\r", "\n"], StringSplitOptions.None);
            if (text.EndsWith('\r') || text.EndsWith('\n')) expected = expected[..^1];
            // A one-byte stream splits every multibyte character, BOM probe, and CRLF.
            foreach (var width in new[] { 1, 2, 3, 5, 65536 })
            {
                var stream = new ShortReadStream(bytes, width);
                var cursor = new RukbatFileLineCursor(stream, name, RukbatFileText.CreateDecoder(encoding));
                var actual = new List<string>();
                while (cursor.MoveNext()) actual.Add(cursor.Current);
                Assert.Equal(expected, actual);
                Assert.False(cursor.MoveNext());
                cursor.Close(); cursor.Close();
                Assert.Null(cursor.Current); Assert.True(stream.Disposed);
                Assert.Throws<InvalidOperationException>(() => cursor.MoveNext());
            }
        }
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void ClosingDoesNotFlushOrValidateUnreadInputAndDetachesBeforeDisposeFailure()
    {
        var flushes = 0;
        var stream = new ShortReadStream([65, 10, 66, 10, 255], 1) { FailDispose = true };
        var decode = RukbatFileText.CreateDecoder(new UTF8Encoding(false, true));
        var cursor = new RukbatFileLineCursor(stream, "utf-8", (bytes, offset, count, final) =>
        {
            if (final) flushes++;
            return decode(bytes, offset, count, final);
        });
        Assert.True(cursor.MoveNext()); Assert.Equal("A", cursor.Current);
        Assert.Throws<IOException>(() => cursor.Close());
        cursor.Close();
        Assert.Equal(0, flushes); Assert.Null(cursor.Current); Assert.Equal(1, stream.DisposeCalls);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void BomHandlingIsExactIncludingPartialAndRepeatedBoms()
    {
        var encoding = new UnicodeEncoding(false, false, true);
        var stream = new ShortReadStream([255, 254, 255, 254, 0, 0], 1);
        var cursor = new RukbatFileLineCursor(stream, "utf-16le", RukbatFileText.CreateDecoder(encoding));
        Assert.True(cursor.MoveNext()); Assert.Equal("\ufeff\0", cursor.Current);
        Assert.False(cursor.MoveNext()); cursor.Close();
        Assert.Equal(2, RukbatFileText.BomOffset("utf-16le", [255, 254, 0, 0], 4));
        Assert.Equal(0, RukbatFileText.BomOffset("utf-8", [239, 187], 2));
        Assert.Throws<ArgumentException>(() => RukbatFileText.BomOffset("utf-32le", [0, 0, 254, 255], 4));
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void Gb18030ValidatesAllFourByteShapesAndSplitPositions()
    {
        if (!OperatingSystem.IsWindows()) return;
        var codec = new RukbatCodePageCodec(54936);
        byte[] unit = new byte[4];
        var accepted = 0; var rejected = 0;
        for (var pointer = 0; pointer < 1587600; pointer++)
        {
            var rest = pointer;
            unit[3] = (byte)(0x30 + rest % 10); rest /= 10;
            unit[2] = (byte)(0x81 + rest % 126); rest /= 126;
            unit[1] = (byte)(0x30 + rest % 10); rest /= 10;
            unit[0] = (byte)(0x81 + rest);
            if (pointer is >= 39420 and < 189000 or > 1237575)
            {
                Assert.Throws<System.ComponentModel.Win32Exception>(() => codec.GetString(unit)); rejected++;
                continue;
            }
            Assert.NotEqual("?", codec.GetString(unit)); accepted++;
        }
        Assert.Equal(1087996, accepted); Assert.Equal(499604, rejected);
        foreach (var bytes in new[] { new byte[] { 0x94, 0x39, 0xfc, 0x36 }, new byte[] { 0x84, 0x31, 0xa5, 0x30 },
            new byte[] { 0xfe, 0x39, 0xfe, 0x39 }, new byte[] { 0x81, 0x30, 0x81 } })
        {
            for (var cut = 1; cut < bytes.Length; cut++)
            {
                var decoder = codec.CreateDecoder();
                var prefix = decoder(bytes, 0, cut, false);
                Assert.Equal("", prefix);
                if (bytes[0] == 0x94) Assert.Equal(codec.GetString(bytes), decoder(bytes, cut, bytes.Length - cut, true));
                else Assert.Throws<System.ComponentModel.Win32Exception>(() => decoder(bytes, cut, bytes.Length - cut, true));
                Assert.Throws<InvalidOperationException>(() => decoder([], 0, 0, true));
            }
        }
        Assert.Equal("?", codec.GetString([63]));
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void AllAcceptedNativeCodecsPreserveConcatenatedUnitsAndIndependentState()
    {
        if (!OperatingSystem.IsWindows()) return;
        var accepted = 0;
        for (var number = 4; number <= 65535; number++)
        {
            RukbatCodePageCodec codec;
            try { codec = new RukbatCodePageCodec(number); }
            catch (ArgumentException) { continue; }
            catch (System.ComponentModel.Win32Exception) { continue; }
            accepted++;
            var bytes = new List<byte>();
            var single = new bool[256];
            for (var b = 0; b < 256; b++)
            {
                try { codec.GetString([(byte)b]); single[b] = true; bytes.Add((byte)b); }
                catch (System.ComponentModel.Win32Exception) { }
            }
            for (var a = 0; a < 256; a++)
            {
                if (single[a]) continue;
                for (var b = 0; b < 256; b++)
                {
                    try { codec.GetString([(byte)a, (byte)b]); bytes.Add((byte)a); bytes.Add((byte)b); }
                    catch (System.ComponentModel.Win32Exception) { }
                }
            }
            if (number == 54936) bytes.AddRange([0x94, 0x39, 0xfc, 0x36]);
            var input = bytes.ToArray();
            var expected = codec.GetString(input);
            foreach (var width in new[] { 1, 2, 3, 7, 65536 })
            {
                var decoder = codec.CreateDecoder();
                var output = new StringBuilder();
                for (var offset = 0; offset < input.Length; offset += width)
                    output.Append(decoder(input, offset, Math.Min(width, input.Length - offset), false));
                output.Append(decoder([], 0, 0, true));
                Assert.True(expected == output.ToString(), $"Code page {number}, width {width}");
            }
            // Every traversal creates its own decoder, including an empty traversal.
            Assert.Equal("", codec.CreateDecoder()([], 0, 0, true));
        }
        Assert.True(accepted >= 26);
    }

    private sealed class ShortReadStream(byte[] bytes, int width) : MemoryStream(bytes)
    {
        public bool Disposed { get; private set; }
        public bool FailDispose { get; init; }
        public int DisposeCalls { get; private set; }
        public override int Read(byte[] buffer, int offset, int count) => base.Read(buffer, offset, Math.Min(count, width));
        protected override void Dispose(bool disposing)
        {
            DisposeCalls++; Disposed = true; base.Dispose(disposing);
            if (FailDispose) throw new IOException("injected close failure");
        }
    }
}
