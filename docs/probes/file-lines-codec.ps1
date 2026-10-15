# Design experiment only. Run from the repository root in a fresh PowerShell process.
# Trap also makes failures nonzero when this file is supplied to -Command - via stdin.
trap { [Console]::Error.WriteLine($_.Exception.ToString()); exit 1 }
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = (Get-Location).Path
$source = Get-Content (Join-Path $root 'src/Rukbat/Runtime/Native/CodePageCodec.cs.txt') -Raw
$source += @'
public static class StreamingCodecProbe {
    // Experimental candidate, NOT the production decoder.
    public static string Decode(int cp, byte[] input, int chunkSize) {
        var codec = new RukbatCodePageCodec(cp);
        var result = new System.Text.StringBuilder();
        byte[] pending = new byte[0];
        for (int offset = 0; offset < input.Length; offset += chunkSize) {
            int n = System.Math.Min(chunkSize, input.Length - offset);
            byte[] block = new byte[pending.Length + n];
            System.Buffer.BlockCopy(pending, 0, block, 0, pending.Length);
            System.Buffer.BlockCopy(input, offset, block, pending.Length, n);
            bool found = false;
            for (int tail = 0; tail <= System.Math.Min(3, block.Length); tail++) {
                try {
                    string text = codec.GetString(block, 0, block.Length - tail);
                    result.Append(text);
                    pending = new byte[tail];
                    System.Buffer.BlockCopy(block, block.Length - tail, pending, 0, tail);
                    found = true;
                    break;
                } catch (System.ComponentModel.Win32Exception e) {
                    if (e.NativeErrorCode != 1113) throw;
                }
            }
            if (!found) throw new System.Text.DecoderFallbackException("Invalid block");
        }
        result.Append(codec.GetString(pending));
        return result.ToString();
    }
    public static string Run() {
        int accepted = 0, validCases = 0, invalidCases = 0;
        var random = new System.Random(98171);
        // Probe actual accepted native code pages, including extra code pages.
        for(int cp = 4; cp <= 65535; cp++) {
            RukbatCodePageCodec codec;
            try { codec = new RukbatCodePageCodec(cp); }
            catch (System.ArgumentException) { continue; }
            catch (System.ComponentModel.Win32Exception) { continue; }
            accepted++;
            var inputs = new System.Collections.Generic.List<byte[]>();
            inputs.Add(codec.GetBytes("A\r\n\rB\n\0"));
            // Raw fixtures also cover noncanonical CP932 encodings.
            if (cp == 932) inputs.Add(new byte[] { 0xee, 0xe0, 13, 10, 0xfb, 0xfc });
            if (cp == 54936) inputs.Add(new byte[] { 0x94, 0x39, 0xfc, 0x36, 13, 10 });
            // Collect valid native units, rather than assuming ASCII delimiters.
            var validUnits = new System.Collections.Generic.List<byte[]>();
            for (int b = 0; b < 256; b++) {
                var unit = new byte[] { (byte)b };
                try { codec.GetString(unit); validUnits.Add(unit); }
                catch (System.ComponentModel.Win32Exception) { }
            }
            for (int i = 0; i < 300; i++) {
                var unit = new byte[] { (byte)random.Next(256), (byte)random.Next(256) };
                try { codec.GetString(unit); validUnits.Add(unit); }
                catch (System.ComponentModel.Win32Exception) { }
            }
            var concatenated = new System.Collections.Generic.List<byte>();
            foreach (var unit in validUnits) concatenated.AddRange(unit);
            inputs.Add(concatenated.ToArray());
            for (int i = 0; i < 20; i++) {
                var bytes = new byte[random.Next(1, 35)];
                random.NextBytes(bytes); inputs.Add(bytes);
            }
            foreach (var input in inputs) {
                string expected = null;
                bool rejected = false;
                try { expected = codec.GetString(input); }
                catch (System.ComponentModel.Win32Exception) { rejected = true; }
                for (int size = 1; size <= 9; size++) {
                    string actual = null;
                    bool chunkRejected = false;
                    try { actual = Decode(cp, input, size); }
                    catch (System.Text.DecoderFallbackException) { chunkRejected = true; }
                    catch (System.ComponentModel.Win32Exception e) {
                        if (e.NativeErrorCode != 1113) throw;
                        chunkRejected = true;
                    }
                    if (rejected != chunkRejected || (!rejected && expected != actual))
                        throw new System.Exception("Mismatch cp="+cp+" chunk="+size+" bytes="+System.BitConverter.ToString(input));
                    if(rejected) invalidCases++; else validCases++;
                }
            }
        }
        return "accepted_native_code_pages="+accepted+" valid_cases="+validCases+" invalid_cases="+invalidCases;
    }
}
'@
Add-Type -TypeDefinition $source
Write-Output ('PowerShell=' + $PSVersionTable.PSVersion + '; CLR=' + [Environment]::Version)
[StreamingCodecProbe]::Run()
