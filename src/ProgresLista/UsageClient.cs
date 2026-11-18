using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ProgresLista;

public sealed record UsageWindow(double Percent, DateTimeOffset? ResetsAt);
public sealed record UsageInfo(UsageWindow? FiveHour, UsageWindow? Week, string? Error);

/// <summary>
/// Čte 5h a týdenní limit z api.anthropic.com/api/oauth/usage pomocí tokenu Claude Code
/// v ~/.claude/.credentials.json. Prošlý token obnoví a nový uloží zpět do souboru,
/// aby CLI dál fungovalo (refresh token se při obnově mění). Token se posílá jen na Anthropic.
/// </summary>
public static class UsageClient
{
    private const string ClientId = "9d1c250a-e61b-44d9-88ed-5944d1962f5e";
    private static readonly string[] TokenUrls =
    {
        "https://platform.claude.com/v1/oauth/token",
        "https://console.anthropic.com/v1/oauth/token",
    };
    private static readonly string CredPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", ".credentials.json");
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    public static async Task<UsageInfo> FetchAsync()
    {
        try
        {
            var token = await GetAccessTokenAsync();
            if (token == null)
                return new UsageInfo(null, null, "Chybí přihlášení. Spusť v terminálu: claude → /login");

            using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.anthropic.com/api/oauth/usage");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Headers.Add("anthropic-beta", "oauth-2025-04-20");
            using var res = await Http.SendAsync(req);
            var body = await res.Content.ReadAsStringAsync();
            if (!res.IsSuccessStatusCode) return new UsageInfo(null, null, $"Usage API: {(int)res.StatusCode}");

            var json = JsonNode.Parse(body);
            return new UsageInfo(ParseWindow(json?["five_hour"]), ParseWindow(json?["seven_day"]), null);
        }
        catch (Exception e)
        {
            return new UsageInfo(null, null, e.Message);
        }
    }

    private static UsageWindow? ParseWindow(JsonNode? node)
    {
        if (node?["utilization"] is not JsonValue v || !v.TryGetValue(out double pct)) return null;
        DateTimeOffset? resets = node["resets_at"] is JsonValue r && r.TryGetValue(out string? s)
            && DateTimeOffset.TryParse(s, out var d) ? d : null;
        return new UsageWindow(pct, resets);
    }

    private static async Task<string?> GetAccessTokenAsync()
    {
        if (!File.Exists(CredPath)) return null;
        var root = JsonNode.Parse(await File.ReadAllTextAsync(CredPath))!;
        var o = root["claudeAiOauth"];
        if (o == null) return null;

        var access = (string?)o["accessToken"];
        var expires = o["expiresAt"]?.GetValue<long>() ?? 0;
        if (!string.IsNullOrEmpty(access) && expires > DateTimeOffset.UtcNow.AddMinutes(2).ToUnixTimeMilliseconds())
            return access;

        var refresh = (string?)o["refreshToken"];
        if (string.IsNullOrEmpty(refresh)) return null;

        var payload = JsonSerializer.Serialize(new { grant_type = "refresh_token", refresh_token = refresh, client_id = ClientId });
        foreach (var url in TokenUrls)
        {
            using var res = await Http.PostAsync(url, new StringContent(payload, Encoding.UTF8, "application/json"));
            if (!res.IsSuccessStatusCode) continue;
            var j = JsonNode.Parse(await res.Content.ReadAsStringAsync())!;
            o["accessToken"] = (string?)j["access_token"];
            if (j["refresh_token"] is { } nr) o["refreshToken"] = (string?)nr;
            o["expiresAt"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + (j["expires_in"]?.GetValue<long>() ?? 3600) * 1000;

            var tmp = CredPath + ".tmp";
            await File.WriteAllTextAsync(tmp, root.ToJsonString());
            File.Move(tmp, CredPath, overwrite: true);
            return (string?)o["accessToken"];
        }
        return null;
    }
}
