# Design experiment: exhaustive one/two-byte inputs, not a production decoder.
trap { [Console]::Error.WriteLine($_.Exception.ToString()); exit 1 }
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$source = Get-Content src/Rukbat/Runtime/Native/CodePageCodec.cs.txt -Raw
$source += @'
public static class CodecBoundaryProbe {
    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true,
        CharSet = System.Runtime.InteropServices.CharSet.Unicode, ExactSpelling = true)]
    private static extern int MultiByteToWideChar(uint cp, uint flags, byte[] input,
        int count, [System.Runtime.InteropServices.Out] char[] output, int capacity);
    private static string Convert(int cp, byte[] bytes, char[] output) {
        if (bytes.Length == 0) return "";
        int length = MultiByteToWideChar((uint)cp, 8, bytes, bytes.Length, output, output.Length);
        if (length != 0) return new string(output, 0, length);
        int error = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
        if (error == 1113) return null;
        throw new System.ComponentModel.Win32Exception(error);
    }
    public static void Run() {
        long pairs = 0, mismatches = 0;
        int accepted = 0;
        char[] output = new char[16];
        for (int cp = 4; cp <= 65535; cp++) {
            RukbatCodePageCodec codec;
            try { codec = new RukbatCodePageCodec(cp); }
            catch (System.ArgumentException) { continue; }
            catch (System.ComponentModel.Win32Exception) { continue; }
            accepted++;
            byte[] sanity = new byte[] { 0, 1, 65, 66 };
            if (Convert(cp, sanity, output) != codec.GetString(sanity))
                throw new System.Exception("Probe marshalling differs from production cp=" + cp);
            string[] single = new string[256];
            int invalidSingles = 0, validPairs = 0, cpMismatches = 0;
            for (int b = 0; b < 256; b++) {
                single[b] = Convert(cp, new byte[] { (byte)b }, output);
                if (single[b] == null) invalidSingles++;
            }
            byte[] pair = new byte[2];
            for (int a = 0; a < 256; a++) for (int b = 0; b < 256; b++) {
                pair[0] = (byte)a; pair[1] = (byte)b;
                string whole = Convert(cp, pair, output);
                if (cp == 54936) {
                    bool shape = (a <= 0x7f && b <= 0x7f) ||
                        (a >= 0x81 && a <= 0xfe && ((b >= 0x40 && b <= 0x7e) || (b >= 0x80 && b <= 0xfe)));
                    if (shape != (whole != null)) throw new System.Exception("GB18030 two-byte grammar mismatch");
                }
                if (whole != null) validPairs++;
                // For chunk size 1, an invalid first byte is retained; the next
                // call sees the complete pair. Otherwise it has already emitted.
                string split = single[a] == null ? whole :
                    (single[b] == null ? null : single[a] + single[b]);
                pairs++;
                if (whole != split) {
                    cpMismatches++; mismatches++;
                    if (cpMismatches <= 3) System.Console.WriteLine(
                        "COUNTEREXAMPLE cp=" + cp + " bytes=" + System.BitConverter.ToString(pair)
                        + " whole=" + Hex(whole) + " split=" + Hex(split));
                }
            }
            System.Console.WriteLine("cp=" + cp + " invalid_single=" + invalidSingles
                + " valid_pairs=" + validPairs + " mismatches=" + cpMismatches);
        }
        System.Console.WriteLine("accepted=" + accepted + " pairs=" + pairs + " mismatches=" + mismatches);
        if (mismatches != 0) throw new System.Exception("Chunk boundary counterexamples found.");
        CheckGb18030(output);
    }
    private static byte[] Slice(byte[] input, int offset, int length) {
        byte[] result = new byte[length];
        System.Buffer.BlockCopy(input, offset, result, 0, length);
        return result;
    }
    private static string Split(int cp, byte[] input, int boundary, char[] output) {
        byte[] first = Slice(input, 0, boundary);
        for (int tail = 0; tail <= System.Math.Min(3, boundary); tail++) {
            string prefix = Convert(cp, Slice(first, 0, boundary - tail), output);
            if (prefix == null) continue;
            // Second call is EOF: all retained and remaining bytes must decode.
            string suffix = Convert(cp, Slice(input, boundary - tail, input.Length - boundary + tail), output);
            return suffix == null ? null : prefix + suffix;
        }
        return null;
    }
    private static void CheckGb18030(char[] output) {
        long cases = 0, valid = 0, invalid = 0, incomplete = 0, question = 0;
        int pointer = 0, rangeStart = -1;
        byte[] unit = new byte[4];
        for (int a = 0x81; a <= 0xfe; a++) for (int b = 0x30; b <= 0x39; b++)
        for (int c = 0x81; c <= 0xfe; c++) for (int d = 0x30; d <= 0x39; d++) {
            unit[0] = (byte)a; unit[1] = (byte)b; unit[2] = (byte)c; unit[3] = (byte)d;
            string whole = Convert(54936, unit, output);
            bool unmapped = (pointer >= 39420 && pointer < 189000) || pointer > 1237575;
            if (unmapped != (whole == "?"))
                throw new System.Exception("GB18030 range classification differs from observed substitution");
            if (whole == "?") {
                question++;
                if (rangeStart < 0) rangeStart = pointer;
            } else if (rangeStart >= 0) {
                System.Console.WriteLine("gb18030_question_pointer_range=" + rangeStart + ".." + (pointer - 1));
                rangeStart = -1;
            }
            pointer++;
            if (whole == null) invalid++; else valid++;
            for (int boundary = 1; boundary <= 3; boundary++) {
                if (Split(54936, unit, boundary, output) != whole)
                    throw new System.Exception("GB18030 mismatch " + System.BitConverter.ToString(unit));
                cases++;
            }
            // Visit each distinct incomplete prefix once.
            if (d == 0x30) {
                int firstLength = c == 0x81 ? (b == 0x30 ? 1 : 2) : 3;
                for (int length = firstLength; length <= 3; length++) {
                    byte[] prefix = Slice(unit, 0, length);
                    if (Convert(54936, prefix, output) != null)
                        throw new System.Exception("Unexpected complete GB18030 prefix");
                    incomplete++;
                }
            }
        }
        if (rangeStart >= 0) System.Console.WriteLine("gb18030_question_pointer_range=" + rangeStart + ".." + (pointer - 1));
        System.Console.WriteLine("gb18030_four_byte_native_accepted=" + valid + " rejected=" + invalid
            + " split_cases=" + cases + " incomplete_prefixes=" + incomplete + " question_outputs=" + question);
    }
    private static string Hex(string value) {
        if (value == null) return "REJECT";
        var result = new System.Text.StringBuilder();
        foreach (char c in value) result.Append(((int)c).ToString("X4")).Append(' ');
        return result.ToString().TrimEnd();
    }
}
'@
Add-Type -TypeDefinition $source
Write-Output ('PowerShell=' + $PSVersionTable.PSVersion + '; CLR=' + [Environment]::Version)
[CodecBoundaryProbe]::Run()
