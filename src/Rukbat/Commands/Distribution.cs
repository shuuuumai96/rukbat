using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace Rukbat
{
    internal static class Distribution
    {
        private const int DiagnosticFailure = 2;
        private const int ProbeTimeoutMs = 15000;
        public static string Version => typeof(Distribution).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0];

        public static int Doctor(TextWriter stdout, TextWriter stderr)
        {
            stdout.WriteLine($"Rukbat: {Version}");
            stdout.WriteLine($"Compiler directory: {AppContext.BaseDirectory}");
            stdout.WriteLine($"Host: {System.Runtime.InteropServices.RuntimeInformation.OSDescription} ({System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture})");
            if (!OperatingSystem.IsWindows())
            {
                stderr.WriteLine("FAIL: Running generated scripts requires Windows. Use a Windows computer for run/test.");
                return DiagnosticFailure;
            }
            // Probe the same command used by run. No profiles, network access, or policy changes.
            const string probe = """
            $ErrorActionPreference = 'Stop'
            [Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)
            $mode = [string]$ExecutionContext.SessionState.LanguageMode
            $native = $false
            $http = $false
            $detail = ''
            try {
                if ($mode -eq 'FullLanguage') {
                    Add-Type -TypeDefinition 'public static class RukbatDoctorProbe { public static int Value() { return 1; } }'
                    $native = [RukbatDoctorProbe]::Value() -eq 1
                    Add-Type -AssemblyName System.Net.Http
                    $http = $null -ne [System.Net.Http.HttpClientHandler].GetProperty('SslProtocols')
                }
            } catch { $detail = $_.Exception.Message }
            [ordered]@{ version = $PSVersionTable.PSVersion.ToString(); executable = (Join-Path $PSHOME 'powershell.exe'); mode = $mode; native = $native; http = $http; detail = $detail } | ConvertTo-Json -Compress
            """;
            var start = new ProcessStartInfo("powershell")
            {
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
                CreateNoWindow = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
            };
            foreach (var argument in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-EncodedCommand",
                Convert.ToBase64String(Encoding.Unicode.GetBytes(probe)) })
            {
                start.ArgumentList.Add(argument);
            }

            try
            {
                using var process = Process.Start(start)!;
                var output = process.StandardOutput.ReadToEndAsync();
                var error = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(ProbeTimeoutMs))
                {
                    process.Kill(entireProcessTree: true);
                    stderr.WriteLine("FAIL: PowerShell environment check timed out. Check application-control policy and PowerShell startup.");
                    return DiagnosticFailure;
                }
                var text = output.GetAwaiter().GetResult();
                var errors = error.GetAwaiter().GetResult();
                if (process.ExitCode != 0)
                {
                    throw new InvalidOperationException(errors.Trim());
                }

                using var result = JsonDocument.Parse(text);
                var root = result.RootElement;
                stdout.WriteLine($"PowerShell: {root.GetProperty("version").GetString()} ({root.GetProperty("executable").GetString()})");
                stdout.WriteLine($"Language mode: {root.GetProperty("mode").GetString()}");
                var ready = System.Version.Parse(root.GetProperty("version").GetString()!) >= new System.Version(5, 1)
                    && root.GetProperty("mode").GetString() == "FullLanguage" && root.GetProperty("native").GetBoolean();
                if (!ready)
                {
                    stderr.WriteLine("FAIL: run/test and native standard APIs need Windows PowerShell 5.1 in FullLanguage with Add-Type permitted. Ask your administrator to approve this environment; check/build can still be used.");
                }

                if (!root.GetProperty("http").GetBoolean())
                {
                    ready = false;
                    stderr.WriteLine("FAIL: HTTP requires the installed .NET Framework 4.7.1 or later handler with OS TLS support.");
                }
                if (root.GetProperty("detail").GetString() is { Length: > 0 } detail)
                {
                    stderr.WriteLine(detail);
                }

                stdout.WriteLine("Scope: local capabilities only; job permissions, execution policy, network authentication and scheduled-task access must be checked for each deployment.");
                if (ready)
                {
                    stdout.WriteLine("OK: Local PowerShell and native helper checks passed.");
                }

                return ready ? 0 : DiagnosticFailure;
            }
            catch (Exception error) when (error is Win32Exception or IOException or InvalidOperationException or JsonException or FormatException)
            {
                stderr.WriteLine($"FAIL: Could not check Windows PowerShell. Make powershell.exe available on PATH and run doctor again. {error.Message}");
                return DiagnosticFailure;
            }
        }
    }
}
