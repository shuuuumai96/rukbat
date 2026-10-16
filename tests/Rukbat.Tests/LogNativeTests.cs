using System.Globalization;
using System.Text;

namespace Rukbat.Tests;

[CollectionDefinition("Log console", DisableParallelization = true)]
public class LogConsoleCollection { }

[Collection("Log console")]
public class LogNativeTests
{
    private static readonly DateTimeOffset Timestamp = new(2026, 10, 16, 18, 0, 0, TimeSpan.FromHours(9));
    private const string RecordTime = "time=2026-10-16T09:00:00.1234567Z";
    private const int LargeMessageLength = 1024 * 1024;
    // Allow fixed formatter/encoding overhead without allowing another full copy.
    private const int AllocationOverheadBytes = 16 * 1024;

    [Trait("Category", "Unit")]
    [Theory]
    [InlineData("DEBUG")]
    [InlineData("INFO")]
    [InlineData("WARN")]
    [InlineData("ERROR")]
    public void FormatHasCanonicalUtcAndOneCrLf(string label)
    {
        var record = RukbatLog.Format(Timestamp.AddTicks(1234567), label, "Starting");
        Assert.Equal($"{RecordTime} level={label} message=\"Starting\"\r\n", record);
    }

    [Trait("Category", "Unit")]
    [Theory]
    [InlineData("", "")]
    [InlineData("日😀\ud7ff\ue000", "日😀\ud7ff\ue000")]
    [InlineData("\"\\\b\t\n\f\r", "\\\"\\\\\\b\\t\\n\\f\\r")]
    [InlineData("\u0000\u0001\u000b\u001b\u001f", "\\u0000\\u0001\\u000b\\u001b\\u001f")]
    [InlineData("\u007f\u0080\u0085\u009f", "\\u007f\\u0080\\u0085\\u009f")]
    [InlineData("\u2028\u2029", "\\u2028\\u2029")]
    [InlineData(" /<>&'\u0020\u007e\u00a0\u2027\u202a", " /<>&'\u0020\u007e\u00a0\u2027\u202a")]
    public void MessageEscapingPreservesExactlyTheSpecifiedScalars(string message, string escaped)
    {
        var record = RukbatLog.Format(Timestamp.AddTicks(1234567), "INFO", message);
        Assert.Equal($"{RecordTime} level=INFO message=\"{escaped}\"\r\n", record);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void EveryControlCharacterIsEscapedWithoutPhysicalNewlines()
    {
        var message = string.Concat(Enumerable.Range(0, 32).Concat(Enumerable.Range(127, 33)).Select(value => (char)value));
        var record = RukbatLog.Format(Timestamp, "INFO", message);
        Assert.All(record[..^2], character => Assert.False(char.IsControl(character)));
        Assert.EndsWith("\r\n", record);
        Assert.Contains("\\u009a\\u009b\\u009c\\u009d\\u009e\\u009f", record);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void FormattingAndEncodingAreCultureIndependent()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("ar-SA");
            Assert.Equal($"{RecordTime} level=WARN message=\"日😀\"\r\n",
                RukbatLog.Format(Timestamp.AddTicks(1234567), "WARN", "日😀"));
            var prepared = RukbatLog.Prepare(["WARN", "日😀", true]);
            var record = Assert.IsType<string>(prepared[0]);
            var bytes = Assert.IsType<byte[]>(prepared[1]);
            Assert.Equal(new UTF8Encoding(false, true).GetBytes(record), bytes);
            Assert.False(bytes.AsSpan().StartsWith(new byte[] { 239, 187, 191 }));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void PreparationUsesOneCurrentUtcTimestampAndMakesBytesOnlyWhenRequested()
    {
        var earliest = DateTimeOffset.UtcNow;
        var prepared = RukbatLog.Prepare(["INFO", "record", false]);
        var latest = DateTimeOffset.UtcNow;
        Assert.Null(prepared[1]);
        var record = Assert.IsType<string>(prepared[0]);
        var actual = DateTimeOffset.ParseExact(record[5..33], "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'",
            CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
        Assert.InRange(actual, earliest, latest);
        Assert.EndsWith(" level=INFO message=\"record\"\r\n", record);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void UnpairedSurrogatesAreRejectedBeforeAnyRecordIsAvailable()
    {
        foreach (var message in new[] { "\ud800", "\udc00", "\ud800x", "x\ud800", "\ud800\ud800", "\udc00\ud800", "😀\udc00" })
        {
            Assert.Throws<ArgumentException>(() => RukbatLog.Format(Timestamp, "INFO", message));
            Assert.Throws<ArgumentException>(() => RukbatLog.Prepare(["INFO", message, false]));
            Assert.Throws<ArgumentException>(() => RukbatLog.Prepare(["INFO", message, true]));
        }
    }

    [Trait("Category", "Unit")]
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LargePlainMessagesAllocateOnlyCurrentRecordAndOptionalBytes(bool file)
    {
        var arguments = new object[] { "INFO", new string('x', LargeMessageLength), file };
        RukbatLog.Prepare(arguments);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var prepared = RukbatLog.Prepare(arguments);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        var record = Assert.IsType<string>(prepared[0]);
        var maximum = (file ? 5L : 4L) * record.Length + AllocationOverheadBytes;
        Assert.InRange(allocated, 0, maximum);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void AppendCreatesBomFreeFilePreservesExistingBytesAndReleasesItsHandle()
    {
        var directory = Path.Combine(Path.GetTempPath(), "rukbat-log-native-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "日本語 [log].txt");
        try
        {
            var first = RukbatLog.Prepare(["INFO", "日😀", true]);
            var second = RukbatLog.Prepare(["ERROR", "second\nline", true]);
            RukbatLog.AppendFile([path, first]);
            Assert.Equal((byte[])first[1], File.ReadAllBytes(path));
            using (File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
            RukbatLog.AppendFile([path, second]);
            Assert.Equal(((byte[])first[1]).Concat((byte[])second[1]), File.ReadAllBytes(path));
            using (File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
            File.WriteAllBytes(path, [255, 0, 239, 187, 191]);
            RukbatLog.AppendFile([path, second]);
            Assert.Equal(new byte[] { 255, 0, 239, 187, 191 }.Concat((byte[])second[1]), File.ReadAllBytes(path));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void AppendHonorsSharingAndDoesNotCreateParentsOrLeakMessageText()
    {
        var directory = Path.Combine(Path.GetTempPath(), "rukbat-log-native-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "log.txt");
        var prepared = RukbatLog.Prepare(["INFO", "sensitive-message-contents", true]);
        try
        {
            using (var locked = File.Open(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
            {
                var error = Assert.Throws<IOException>(() => RukbatLog.AppendFile([path, prepared]));
                Assert.DoesNotContain("sensitive-message-contents", error.ToString());
            }
            Assert.Empty(File.ReadAllBytes(path));
            Assert.Throws<DirectoryNotFoundException>(() => RukbatLog.AppendFile([Path.Combine(directory, "missing", "log.txt"), prepared]));
            Assert.False(Directory.Exists(Path.Combine(directory, "missing")));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void StderrWritesOneCompleteRecordFlushesAndDoesNotCloseTheExistingWriter()
    {
        var previous = Console.Error;
        var writer = new TrackingWriter();
        try
        {
            Console.SetError(writer);
            var prepared = RukbatLog.Prepare(["WARN", "日😀\r\n", false]);
            RukbatLog.WriteStderr(prepared);
            Assert.Equal((string)prepared[0], writer.Text.ToString());
            Assert.Equal(1, writer.Writes);
            Assert.Equal(1, writer.Flushes);
            Assert.False(writer.Disposed);
        }
        finally { Console.SetError(previous); }
    }

    [Trait("Category", "Unit")]
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void StderrPropagatesWriteAndFlushFailuresWithoutClosingTheWriter(bool write)
    {
        var previous = Console.Error;
        var writer = new TrackingWriter { FailWrite = write, FailFlush = !write };
        try
        {
            Console.SetError(writer);
            var prepared = RukbatLog.Prepare(["INFO", "sensitive-message-contents", false]);
            var error = Assert.Throws<IOException>(() => RukbatLog.WriteStderr(prepared));
            Assert.Equal(write ? "injected write failure" : "injected flush failure", error.Message);
            Assert.DoesNotContain("sensitive-message-contents", error.ToString());
            Assert.False(writer.Disposed);
        }
        finally { Console.SetError(previous); }
    }

    [Trait("Category", "Unit")]
    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    public void FileFailuresPreservePrimaryAndAlwaysAttemptExactlyOneClose(bool write, bool flush, bool close)
    {
        var stream = new FailingStream { FailWrite = write, FailClose = close };
        var flushCalls = 0;
        var error = Assert.Throws<IOException>(() => RukbatLog.WriteAndClose(stream, [65], () =>
        {
            flushCalls++;
            if (flush) throw new IOException("injected flush failure");
        }));
        var primary = write ? "injected write failure" : flush ? "injected flush failure" : "injected close failure";
        Assert.StartsWith(primary, error.Message);
        Assert.Equal(write ? 0 : 1, flushCalls);
        Assert.Equal(1, stream.Closes);
        if (close && (write || flush))
        {
            Assert.Contains("Cleanup failed: injected close failure", error.Message);
            Assert.Equal(primary, error.InnerException!.Message);
        }
        else Assert.Null(error.InnerException);
    }

    private sealed class TrackingWriter : TextWriter
    {
        public override Encoding Encoding => Encoding.UTF8;
        public StringBuilder Text { get; } = new();
        public int Writes { get; private set; }
        public int Flushes { get; private set; }
        public bool Disposed { get; private set; }
        public bool FailWrite { get; init; }
        public bool FailFlush { get; init; }
        public override void Write(string? value)
        {
            Writes++;
            if (FailWrite) throw new IOException("injected write failure");
            Text.Append(value);
        }
        public override void Flush()
        {
            Flushes++;
            if (FailFlush) throw new IOException("injected flush failure");
        }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }

    private sealed class FailingStream : MemoryStream
    {
        public bool FailWrite { get; init; }
        public bool FailClose { get; init; }
        public int Closes { get; private set; }
        public override void Write(byte[] buffer, int offset, int count)
        {
            if (FailWrite) throw new IOException("injected write failure");
            base.Write(buffer, offset, count);
        }
        protected override void Dispose(bool disposing)
        {
            Closes++;
            base.Dispose(disposing);
            if (FailClose) throw new IOException("injected close failure");
        }
    }
}
