using System.Text.Json;

namespace Baseera.App.Services;

internal static partial class ChatGptAuth
{
    private static string ErrorCode(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("error", out var error)) return "";
        return error.ValueKind == JsonValueKind.String ? error.GetString() ?? "" : error.ValueKind == JsonValueKind.Object ? Optional(error, "code") : "";
    }
    internal static ChatGptException ResponseFailure(string code, int status, string requestId = "", string shape = "", string parameter = "")
    {
        string key = code switch
        {
            "subscription_sharing_usage_limit_exceeded" => "chatGptUsageLimit",
            "subscription_sharing_user_not_eligible" => "chatGptNotEligible",
            "subscription_sharing_usage_unavailable" or "subscription_sharing_user_unavailable" => "chatGptTemporarilyUnavailable",
            "subscription_sharing_unsupported_capability" or "subscription_sharing_route_not_supported" => "chatGptConnectionRejected",
            "chatpass_v2_scope_not_authorized" or "chatpass_v2_invalid_authorization_context" or "invalid_client" => "chatGptConnectionRejected",
            "subscription_sharing_invalid_user" => "chatGptReconnect",
            _ => status switch { 401 => "chatGptReconnect", 403 => "chatGptConnectionRejected", 429 => "chatGptUsageLimit", >= 500 => "chatGptTemporarilyUnavailable", _ => "chatGptStreamFailed" }
        };
        return new(key, status, code, requestId, shape, parameter);
    }
    private static string RequestId(HttpResponseMessage response) => response.Headers.TryGetValues("x-request-id", out var values) ? values.FirstOrDefault() ?? "" : "";
    private static async Task<ChatGptException> HttpFailure(HttpResponseMessage response, CancellationToken ct)
    {
        string code = "", shape = "unstructured", parameter = "";
        try
        {
            using var json = await AiCatalog.ReadJson(response, 100_000, ct);
            code = ErrorCode(json.RootElement);
            if (json.RootElement.ValueKind == JsonValueKind.Object && json.RootElement.TryGetProperty("error", out var error)) parameter = Optional(error, "param");
            shape = json.RootElement.TryGetProperty("error", out _) ? "error" : json.RootElement.TryGetProperty("detail", out _) ? "detail" : "other-json";
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or InvalidOperationException) { }
        return ResponseFailure(code, (int)response.StatusCode, RequestId(response), shape, parameter);
    }
}
