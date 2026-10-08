namespace Rukbat;

internal sealed record HttpConstant(string Name, object Value, ValueType Type);

internal static class HttpValues
{
    public static readonly ValueType Method = new("http.Method");
    public static readonly ValueType Body = new("http.Body");
    public static readonly string[] Methods = ["get", "head", "post", "put", "patch", "delete", "options"];
    // IANA HTTP status registry; RFC 9110 names are canonical. Reserved codes are
    // named explicitly; 418 retains its widely used legacy name. No numeric aliases.
    public static readonly (int Code, string Name)[] Statuses =
    [
        (100, "continue"), (101, "switching_protocols"), (102, "processing"), (103, "early_hints"),
        (104, "upload_resumption_supported"),
        (200, "ok"), (201, "created"), (202, "accepted"), (203, "non_authoritative_information"),
        (204, "no_content"), (205, "reset_content"), (206, "partial_content"), (207, "multi_status"),
        (208, "already_reported"), (226, "im_used"),
        (300, "multiple_choices"), (301, "moved_permanently"), (302, "found"), (303, "see_other"),
        (304, "not_modified"), (305, "use_proxy"), (306, "reserved"), (307, "temporary_redirect"), (308, "permanent_redirect"),
        (400, "bad_request"), (401, "unauthorized"), (402, "payment_required"), (403, "forbidden"),
        (404, "not_found"), (405, "method_not_allowed"), (406, "not_acceptable"), (407, "proxy_authentication_required"),
        (408, "request_timeout"), (409, "conflict"), (410, "gone"), (411, "length_required"),
        (412, "precondition_failed"), (413, "content_too_large"), (414, "uri_too_long"), (415, "unsupported_media_type"),
        (416, "range_not_satisfiable"), (417, "expectation_failed"), (418, "im_a_teapot"),
        (421, "misdirected_request"), (422, "unprocessable_content"), (423, "locked"), (424, "failed_dependency"),
        (425, "too_early"), (426, "upgrade_required"), (428, "precondition_required"), (429, "too_many_requests"),
        (431, "request_header_fields_too_large"), (451, "unavailable_for_legal_reasons"),
        (500, "internal_server_error"), (501, "not_implemented"), (502, "bad_gateway"), (503, "service_unavailable"),
        (504, "gateway_timeout"), (505, "http_version_not_supported"), (506, "variant_also_negotiates"),
        (507, "insufficient_storage"), (508, "loop_detected"), (510, "not_extended"), (511, "network_authentication_required")
    ];
    public static readonly HttpConstant[] Constants = Methods.Select(name =>
        new HttpConstant("method_" + name, name.ToUpperInvariant(), Method)).Concat(Statuses.Select(status =>
        new HttpConstant($"status_{status.Code}_{status.Name}", (long)status.Code, ValueType.Int))).ToArray();

    public const string Structures = "struct Header { name: string; value: string }; " +
        "struct QueryParameter { name: string; value: string }; " +
        "struct TextBody { text: string; encoding: encoding.Encoding }; " +
        "struct Request { method: Method; url: string; headers: Header[]; body: TextBody? }; " +
        "struct Options { timeout_ms: int; max_response_bytes: int; proxy_url: string?; " +
        "use_windows_credentials: bool; proxy_use_windows_credentials: bool; max_redirects: int }; " +
        "struct Response { status: int; url: string; headers: Header[]; body: Body }; " +
        "struct DownloadResult { status: int; url: string; headers: Header[]; saved: bool; size: int; error_body: Body? }";

    public static bool Contains(ValueType type) => type == Method || type == Body;
    public static ValueType? OpaqueType(string name) => name switch { "Method" => Method, "Body" => Body, _ => null };
}
