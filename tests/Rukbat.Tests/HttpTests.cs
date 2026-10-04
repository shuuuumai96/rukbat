using System.Net;
using System.Net.Sockets;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Rukbat.Tests;

public sealed class HttpTests : TestBase
{
    [Trait("Category", "Unit")]
    [Theory]
    [InlineData("http.status_200_ok = 201")]
    [InlineData("http.method_get = http.method_post")]
    [InlineData("x := http.status_ok")]
    [InlineData("x := http.status_999_unknown")]
    [InlineData("http.request('GET', 'http://localhost')")]
    [InlineData("http.options(null)")]
    [InlineData("http.options(timeout_ms: 1000)")]
    [InlineData("http.send<int>(http.request(http.method_get, 'http://localhost'), http.options(1))")]
    [InlineData("x := http.Body {}")]
    [InlineData("x := http.Method {}")]
    [InlineData("print(http.method_get)")]
    [InlineData("json.stringify([http.method_get])")]
    [InlineData("powershell<http.Body>('Write-Output', 'x')")]
    [InlineData("struct Wrapper { value: http.Body?[] }; powershell<Wrapper>('Write-Output', 'x')")]
    [InlineData("fn f(r: http.Response): void { json.stringify(r) }")]
    [InlineData("fn f(b: http.Body): void { powershell<void>('Write-Output', [b]) }")]
    [InlineData("fn f(b: http.Body): bool { return b == b }")]
    [InlineData("http.text('hello', encoding.utf8)")]
    [InlineData("http.download(http.request(http.method_get, 'http://localhost'), 'out', 2.0, http.options(1))")]
    public void InvalidTypesConstantsAndBoundariesAreRejected(string source) =>
        Assert.False(Compile("import http; import encoding; import json\n" + source).Success);

    [Trait("Category", "Unit")]
    [Fact]
    public void ConstantsOpaqueContainersAndDataCopiesHaveCheckedTypes()
    {
        var output = Success("""
            import http; import test
            fn choose(method: http.Method): http.Method { return method }
            fn decode(body: http.Body): http.Body { return body }
            values: http.Method?[] = [choose(http.method_get), null]
            test.assert(http.status_200_ok == 200)
            test.assert(http.status_511_network_authentication_required == 511)
            request := http.request(choose(http.method_post), 'http://localhost')
            copy := request
            copy.headers.append(http.Header { name: 'Accept', value: 'application/json' })
            test.assert(request.headers.length == 0)
            """);
        Assert.DoesNotContain("RukbatProcessJob", output);
        Assert.DoesNotContain("RukbatZip", output);
        Assert.DoesNotContain("RukbatMoveBatch", output);
    }

    private static async Task Verify(Project project, string source, string shell)
    {
        project.Write("main.rukbat", source + "\nprint('passed')\n");
        var compiled = project.Compile(); Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));
        project.Write("deploy.ps1", compiled.Output!);
        File.Delete(project.PathFor("main.rukbat"));
        var result = await RunDeployed(shell, project.PathFor("deploy.ps1"), project.DirectoryPath, Path.GetTempPath());
        Assert.True(result.ExitCode == 0, result.Output + "\n" + result.Error);
        Assert.Equal("passed\n", result.Output);
        Assert.Equal("", result.Error);
    }

    [Trait("Category", "CrossVersion")]
    [Theory]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    public async Task ResponsesEncodingHeadersQueryAndExplicitBodies(string shell)
    {
        using var project = new Project(); await using var server = new Server();
        await Verify(project, $$"""
            import http; import encoding; import json; import test
            fn decode(body: http.Body): string { return http.text(body, encoding.utf8) }
            options := http.options(5000)
            request := http.request(http.method_get, '{{server.Url}}json')
            response := http.send(request, options)
            test.assert(response.status == http.status_200_ok)
            document := json.parse(decode(response.body))
            test.assert(json.as<bool>(json.get(document, 'ready')))
            response = http.send(http.request(http.method_get, '{{server.Url}}cp932'), options)
            expected := json.parse<string>('"\\u65e5\\u672c"')
            test.assert(http.text(response.body, encoding.windows_932) == expected)
            rejected := false
            try { http.text(response.body, encoding.utf8) } catch err { rejected = true }
            test.assert(rejected)
            response = http.send(http.request(http.method_get, '{{server.Url}}missing'), options)
            test.assert(response.status == http.status_404_not_found)
            test.assert(decode(response.body) == 'missing')
            values := http.header_values(response.headers, 'set-cookie')
            test.assert(values.length == 2)
            test.assert(values[0] == 'a=1')
            test.assert(values[1] == 'b=2')
            test.assert(http.header_values(response.headers, 'absent').length == 0)
            for status in [http.status_429_too_many_requests, http.status_500_internal_server_error, 599] {
                response = http.send(http.request(http.method_get, '{{server.Url}}status/' + text_status(status)), options)
                test.assert(response.status == status)
            }
            request = http.request(http.method_head, '{{server.Url}}head')
            response = http.send(request, options)
            test.assert(decode(response.body) == '')
            response = http.send(http.request(http.method_get, '{{server.Url}}empty'), options)
            test.assert(response.status == http.status_204_no_content)
            test.assert(decode(response.body) == '')
            request = http.request(http.method_post, '{{server.Url}}echo')
            request.headers = [http.Header { name: 'Content-Type', value: 'application/json' }]
            request.body = http.TextBody { text: '{"message":"' + expected + '"}', encoding: encoding.utf8 }
            response = http.send(request, options)
            test.assert(json.as<string>(json.get(json.parse(decode(response.body)), 'message')) == expected)
            request.body = http.TextBody { text: expected + '\r\n', encoding: encoding.windows_932 }
            response = http.send(request, options)
            test.assert(http.text(response.body, encoding.windows_932) == expected + '\r\n')
            request.body = http.TextBody { text: '', encoding: encoding.utf8 }
            response = http.send(request, options)
            test.assert(decode(response.body) == '')
            url := http.with_query('{{server.Url}}query?existing=1', [
                http.QueryParameter { name: 'x', value: expected + ' &+' },
                http.QueryParameter { name: 'x', value: '' }
            ])
            response = http.send(http.request(http.method_get, url), options)
            test.assert(decode(response.body) == '/query?existing=1&x=%E6%97%A5%E6%9C%AC%20%26%2B&x=')
            response = http.send(http.request(http.method_get, '{{server.Url}}cookie'), options)
            test.assert(decode(response.body) == 'none')
            import text
            fn text_status(value: int): string { return text.from_int(value) }
            """, shell);
    }

    [Trait("Category", "CrossVersion")]
    [Theory]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    public async Task RedirectAndProxyPoliciesAreExplicit(string shell)
    {
        using var project = new Project(); await using var other = new Server(); await using var server = new Server();
        server.OtherUrl = other.Url;
        await Verify(project, $$"""
            import http; import encoding; import test
            options := http.options(5000)
            response := http.send(http.request(http.method_get, '{{server.Url}}redirect'), options)
            test.assert(response.status == http.status_302_found)
            options.max_redirects = 2
            response = http.send(http.request(http.method_get, '{{server.Url}}redirect'), options)
            test.assert(response.status == http.status_200_ok)
            test.assert(response.url == '{{server.Url}}json')
            response = http.send(http.request(http.method_get, '{{server.Url}}foreign'), options)
            test.assert(response.status == http.status_302_found)
            rejected := false
            try { http.send(http.request(http.method_get, '{{server.Url}}loop'), options) } catch err { rejected = true }
            test.assert(rejected)
            rejected = false
            try { http.send(http.request(http.method_post, '{{server.Url}}echo'), options) } catch err { rejected = true }
            test.assert(rejected)
            options.max_redirects = 0
            options.proxy_url = '{{other.Url}}'
            response = http.send(http.request(http.method_get, 'http://unresolvable.invalid/proxy'), options)
            test.assert(http.text(response.body, encoding.utf8) == 'http://unresolvable.invalid/proxy')
            """, shell);
        Assert.DoesNotContain(other.Requests, value => value.Target == "/json");
        Assert.Contains(other.Requests, value => value.Target == "http://unresolvable.invalid/proxy");
    }

    [Trait("Category", "CrossVersion")]
    [Theory]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    public async Task DownloadsAreStreamedAndPublishedOnlyWhenComplete(string shell)
    {
        using var project = new Project(); await using var server = new Server();
        project.Write("existing.bin", "original");
        server.RacePath = project.PathFor("race.bin");
        await Verify(project, $$"""
            import http; import encoding; import fs; import test
            options := http.options(5000)
            options.max_response_bytes = 8
            request := http.request(http.method_get, '{{server.Url}}binary')
            result := http.download(request, './out.bin', 200000, options)
            test.assert(result.status == http.status_200_ok)
            test.assert(result.saved)
            test.assert(result.size == 131073)
            test.assert(result.error_body == null)
            result = http.download(http.request(http.method_get, '{{server.Url}}missing'), './missing.bin', 1, options)
            test.assert(!result.saved)
            test.assert(!fs.is_file('./missing.bin'))
            error_body := result.error_body
            if error_body != null { test.assert(http.text(error_body, encoding.utf8) == 'missing') }
            else { test.fail('Error response body was lost') }
            result = http.download(http.request(http.method_get, '{{server.Url}}status/206'), './partial.bin', 10, options)
            test.assert(!result.saved)
            test.assert(!fs.is_file('./partial.bin'))
            result = http.download(http.request(http.method_get, '{{server.Url}}zero'), './zero.bin', 10, options)
            test.assert(result.saved)
            test.assert(result.size == 0)
            rejected := false
            try { http.download(request, './existing.bin', 200000, options) } catch err { rejected = true }
            test.assert(rejected)
            test.assert(fs.read_text('./existing.bin', encoding.utf8) == 'original')
            for target in ['oversized.bin', 'truncated.bin', 'race.bin'] {
                path := '{{server.Url}}binary'
                maximum := 10
                if target == 'truncated.bin' { path = '{{server.Url}}truncated'; maximum = 200000 }
                if target == 'race.bin' { path = '{{server.Url}}race'; maximum = 200000 }
                rejected = false
                try { http.download(http.request(http.method_get, path), target, maximum, options) } catch err { rejected = true }
                test.assert(rejected)
            }
            test.assert(!fs.is_file('oversized.bin'))
            test.assert(!fs.is_file('truncated.bin'))
            test.assert(fs.read_text('race.bin', encoding.utf8) == 'winner')
            """, shell);
        Assert.Equal(Server.Binary, File.ReadAllBytes(project.PathFor("out.bin")));
        Assert.Empty(Directory.GetFiles(project.DirectoryPath, ".rukbat-http-*.tmp"));
    }

    [Trait("Category", "CrossVersion")]
    [Theory]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    public async Task DeadlinesLimitsAndInvalidInputsUseOrdinaryErrors(string shell)
    {
        using var project = new Project(); await using var server = new Server();
        await Verify(project, $$"""
            import http; import encoding; import test; import fs
            options := http.options(5000)
            options.max_response_bytes = 4
            for target in ['json', 'chunked', 'truncated', 'coded'] {
                rejected := false
                try { http.send(http.request(http.method_get, '{{server.Url}}' + target), options) } catch err { rejected = true }
                test.assert(rejected)
            }
            options = http.options(5000)
            for target in ['truncated', 'coded', 'large-headers'] {
                rejected := false
                try { http.send(http.request(http.method_get, '{{server.Url}}' + target), options) } catch err { rejected = true }
                test.assert(rejected)
            }
            options = http.options(250)
            for target in ['slow-headers', 'slow-body', 'drip'] {
                rejected := false
                try { http.send(http.request(http.method_get, '{{server.Url}}' + target), options) } catch err { rejected = true }
                test.assert(rejected)
            }
            rejected := false
            try { http.download(http.request(http.method_get, '{{server.Url}}slow-body'), './timeout.bin', 100, options) } catch err { rejected = true }
            test.assert(rejected)
            test.assert(!fs.is_file('./timeout.bin'))
            options = http.options(5000)
            for url in ['relative', 'ftp://localhost', '{{server.Url}}#fragment', 'http://user:password@localhost/', '{{server.Url}}bad%xx'] {
                rejected = false
                try { http.request(http.method_get, url) } catch err { rejected = true }
                test.assert(rejected)
            }
            request := http.request(http.method_get, '{{server.Url}}json')
            for timeout in [0, -1, 2147483648] {
                options.timeout_ms = timeout
                rejected = false
                try { http.send(request, options) } catch err { rejected = true }
                test.assert(rejected)
            }
            options = http.options(5000)
            for maximum in [0, 67108865] {
                options.max_response_bytes = maximum
                rejected = false
                try { http.send(request, options) } catch err { rejected = true }
                test.assert(rejected)
            }
            options = http.options(5000)
            for redirects in [-1, 21] {
                options.max_redirects = redirects
                rejected = false
                try { http.send(request, options) } catch err { rejected = true }
                test.assert(rejected)
            }
            options = http.options(5000)
            options.proxy_use_windows_credentials = true
            rejected = false
            try { http.send(request, options) } catch err { rejected = true }
            test.assert(rejected)
            options = http.options(5000)
            request.body = http.TextBody { text: 'x', encoding: encoding.utf8 }
            rejected = false
            try { http.send(request, options) } catch err { rejected = true }
            test.assert(rejected)
            request = http.request(http.method_post, '{{server.Url}}echo')
            request.body = http.TextBody { text: json.parse<string>('"\\u65e5\\u672c"'), encoding: encoding.ascii }
            rejected = false
            try { http.send(request, options) } catch err { rejected = true }
            test.assert(rejected)
            request = http.request(http.method_get, '{{server.Url}}json')
            for header in ['Host', 'Content-Length', 'Transfer-Encoding', 'Accept-Encoding'] {
                request.headers = [http.Header { name: header, value: 'x' }]
                rejected = false
                try { http.send(request, options) } catch err { rejected = true }
                test.assert(rejected)
            }
            request.headers = [http.Header { name: 'X-Test', value: 'x\r\nInjected: yes' }]
            rejected = false
            try { http.send(request, options) } catch err { rejected = true }
            test.assert(rejected)
            import json
            """, shell);
        Assert.Empty(Directory.GetFiles(project.DirectoryPath, ".rukbat-http-*.tmp"));
        Assert.DoesNotContain(server.Requests, r => r.Headers.ContainsKey("X-Test"));
        Assert.DoesNotContain(server.Requests, r => r.Method == "POST");
    }

    [Trait("Category", "CrossVersion")]
    [Theory]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    public async Task UntrustedCertificatesAndConnectionFailuresAreErrors(string shell)
    {
        using var project = new Project(); await using var tls = new Server(true);
        var closed = new TcpListener(IPAddress.Loopback, 0); closed.Start();
        var closedPort = ((IPEndPoint)closed.LocalEndpoint).Port; closed.Stop();
        await Verify(project, $$"""
            import http; import test
            options := http.options(3000)
            for url in ['{{tls.Url}}json', 'http://127.0.0.1:{{closedPort}}/'] {
                rejected := false
                try { http.send(http.request(http.method_get, url), options) } catch err { rejected = true }
                test.assert(rejected)
            }
            powershell<void>('Add-Type', TypeDefinition: 'public static class TestCertificateBypass { public static void Apply() { var type = System.Type.GetType("System.Net.ServicePointManager, System.Net.ServicePoint", false); if (type == null) { foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies()) { type = assembly.GetType("System.Net.ServicePointManager", false); if (type != null) break; } } System.Net.Security.RemoteCertificateValidationCallback callback = delegate { return true; }; type.GetProperty("ServerCertificateValidationCallback").SetValue(null, callback, null); } }')
            powershell<void>('Invoke-Expression', '[TestCertificateBypass]::Apply()')
            rejected := false
            try { http.send(http.request(http.method_get, '{{tls.Url}}json'), options) } catch err { rejected = true }
            test.assert(rejected)
            """, shell);
        Assert.Empty(tls.Requests);
    }

    [Trait("Category", "CrossVersion")]
    [Theory]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    public async Task DocumentedExampleRunsAgainstAnActualResponse(string shell)
    {
        using var project = new Project(); await using var server = new Server();
        project.Write("main.rukbat", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "examples", "http-json.rukbat")));
        var compiled = project.Compile(); Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));
        var result = shell == "powershell" ? await project.RunOutputWithWindowsPowerShell(compiled.Output!, server.Url + "json")
            : await project.RunOutput(compiled.Output!, server.Url + "json");
        Assert.Equal(0, result.ExitCode); Assert.Equal("{\"ready\":true}\n", result.Output); Assert.Empty(result.Error);
        Assert.Equal("application/json", Assert.Single(server.Requests).Headers["Accept"]);
    }

    private sealed record Received(string Method, string Target, Dictionary<string, string> Headers, byte[] Body);
    private sealed class Server : IAsyncDisposable
    {
        private readonly TcpListener listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource cancellation = new();
        private readonly List<Task> clients = [];
        private readonly Task accepting;
        private readonly X509Certificate2? certificate;
        public readonly List<Received> Requests = [];
        public string Url { get; }
        public string? OtherUrl { get; set; }
        public string? RacePath { get; set; }
        public static readonly byte[] Binary = Enumerable.Range(0, 131073).Select(i => (byte)i).ToArray();
        public Server(bool tls = false)
        {
            if (tls)
            {
                using var key = RSA.Create(2048);
                var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                var names = new SubjectAlternativeNameBuilder(); names.AddIpAddress(IPAddress.Loopback);
                request.CertificateExtensions.Add(names.Build());
                certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
            }
            listener.Start(); Url = $"{(tls ? "https" : "http")}://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/";
            accepting = Accept();
        }
        private async Task Accept()
        {
            try { while (!cancellation.IsCancellationRequested) { var client = await listener.AcceptTcpClientAsync(cancellation.Token); lock (clients) clients.Add(Serve(client)); } }
            catch (OperationCanceledException) { } catch (SocketException) when (cancellation.IsCancellationRequested) { }
        }
        private async Task Serve(TcpClient client)
        {
            using (client)
            try
            {
                using Stream stream = certificate is null ? client.GetStream() : new SslStream(client.GetStream());
                if (stream is SslStream secure)
                    await secure.AuthenticateAsServerAsync(new SslServerAuthenticationOptions { ServerCertificate = certificate, EnabledSslProtocols = SslProtocols.Tls12 }, cancellation.Token);
                var header = new List<byte>(); var one = new byte[1];
                while (header.Count < 65536 && await stream.ReadAsync(one, cancellation.Token) != 0)
                {
                    header.Add(one[0]); if (header.Count >= 4 && header.TakeLast(4).SequenceEqual("\r\n\r\n"u8.ToArray())) break;
                }
                var lines = Encoding.ASCII.GetString(header.ToArray()).Split("\r\n"); var start = lines[0].Split(' ');
                var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var line in lines.Skip(1)) { var colon = line.IndexOf(':'); if (colon > 0) headers[line[..colon]] = line[(colon + 1)..].Trim(); }
                var body = new byte[headers.TryGetValue("Content-Length", out var length) ? int.Parse(length) : 0];
                await stream.ReadExactlyAsync(body, cancellation.Token);
                var received = new Received(start[0], start[1], headers, body); lock (Requests) Requests.Add(received);
                var path = start[1].Split('?')[0];
                int status = 200; byte[] data = "{\"ready\":true}"u8.ToArray(); string extra = "";
                if (start[1].StartsWith("http://")) data = Encoding.UTF8.GetBytes(start[1]);
                if (path == "/cp932") data = [0x93, 0xfa, 0x96, 0x7b];
                if (path == "/echo") data = body;
                if (path == "/query") data = Encoding.UTF8.GetBytes(start[1]);
                if (path == "/cookie") data = Encoding.UTF8.GetBytes(headers.GetValueOrDefault("Cookie", "none"));
                if (path == "/missing") { status = 404; data = "missing"u8.ToArray(); extra = "Set-Cookie: a=1\r\nSet-Cookie: b=2\r\n"; }
                if (path == "/empty") { status = 204; data = []; }
                if (path == "/zero") data = [];
                if (path.StartsWith("/status/")) { status = int.Parse(path[8..]); data = []; }
                if (path is "/binary" or "/race") data = Binary;
                if (path == "/race") File.WriteAllText(RacePath!, "winner");
                if (path is "/redirect" or "/loop" or "/foreign") { status = 302; data = []; extra = "Location: " + (path == "/loop" ? "/loop" : path == "/foreign" ? OtherUrl + "json" : "/json") + "\r\n"; }
                if (path == "/slow-headers") await Task.Delay(2000, cancellation.Token);
                if (path is "/chunked" or "/drip")
                {
                    await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\nConnection: close\r\n\r\n"), cancellation.Token);
                    for (int i = 0; i < 20; i++) { await stream.WriteAsync("1\r\nx\r\n"u8.ToArray(), cancellation.Token); if (path == "/drip") await Task.Delay(50, cancellation.Token); }
                    await stream.WriteAsync("0\r\n\r\n"u8.ToArray(), cancellation.Token); return;
                }
                if (path == "/coded") extra = "Content-Encoding: gzip\r\n";
                if (path == "/large-headers") extra = "X-Large: " + new string('x', 65536) + "\r\n";
                long declared = path == "/truncated" ? data.Length + 100 : path == "/head" ? 999999999 : data.Length;
                await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 {status} Test\r\nContent-Length: {declared}\r\nConnection: close\r\n{extra}\r\n"), cancellation.Token);
                if (path == "/slow-body") await Task.Delay(2000, cancellation.Token);
                if (start[0] != "HEAD") await stream.WriteAsync(data, cancellation.Token);
            }
            catch (IOException) { } catch (OperationCanceledException) { } catch (SocketException) { } catch (AuthenticationException) { }
        }
        public async ValueTask DisposeAsync()
        {
            cancellation.Cancel(); listener.Stop(); await accepting;
            Task[] pending; lock (clients) pending = clients.ToArray(); await Task.WhenAll(pending); cancellation.Dispose(); certificate?.Dispose();
        }
    }
}
