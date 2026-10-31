namespace Rukbat.Tests
{
    public sealed partial class HttpTests
    {
        [Trait("Category", "Runtime")]
        [Trait("Category", "CrossVersion")]
        [Theory]
        [InlineData("powershell")]
        [InlineData("pwsh")]
        public async Task ImportedScalarFieldsReachQueryHeadersAndRequestValidation(string shell)
        {
            using var project = new Project();
            await using var server = new Server();
            project.Write("values.rukbat", """
                import encoding
                import http

                fn string_value(value: string): string {
                    return value
                }

                fn number(value: int): int {
                    return value
                }

                fn flag(value: bool): bool {
                    return value
                }

                fn method(): http.Method {
                    return http.method_post
                }

                fn codec(): encoding.Encoding {
                    return encoding.utf8
                }

                fn body(value: http.Body): http.Body {
                    return value
                }
                """);
            await Verify(project, $$"""
                import encoding
                import http
                import test
                import "./values.rukbat" as values

                url := http.with_query(values.string_value("{{server.Url}}query"), [
                    http.QueryParameter {
                        name: values.string_value("x"),
                        value: values.string_value("a b")
                    }
                ])
                test.assert(url == "{{server.Url}}query?x=a%20b")
                headers := [http.Header {
                    name: values.string_value("X-Value"),
                    value: values.string_value("value")
                }]
                test.assert(http.header_values(headers, values.string_value("x-value"))[0] == "value")
                request := http.request(values.method(), values.string_value("{{server.Url}}echo"))
                request.headers = headers
                request.body = http.TextBody {
                    text: values.string_value("payload"),
                    encoding: values.codec()
                }
                const timeout_ms := 5000
                const maximum_bytes := 4096
                options := http.options(values.number(timeout_ms))
                options.max_response_bytes = values.number(maximum_bytes)
                options.max_redirects = values.number(0)
                options.use_windows_credentials = values.flag(false)
                options.proxy_use_windows_credentials = values.flag(false)
                response := http.send(request, options)
                test.assert(response.status == http.status_200_ok)
                test.assert(http.text(values.body(response.body), values.codec()) == "payload")
                options.proxy_url = values.string_value("{{server.Url}}")
                request = http.request(http.method_get, "http://unresolvable.invalid/proxy")
                response = http.send(request, options)
                test.assert(http.text(response.body, encoding.utf8) == "http://unresolvable.invalid/proxy")
                """, shell);
        }
    }
}
