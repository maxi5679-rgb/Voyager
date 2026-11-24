using System.Net.Http.Json;
using System.Text.Json;

namespace Voyager;

internal sealed record LocalChatMessage(string Role, string Content);

/// <summary>Local-only OpenAI-compatible API calls for Ollama and LM Studio.</summary>
internal static class LocalChatClient
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromMinutes(10) };

    public static async Task<IReadOnlyList<string>> GetModelsAsync(Engine engine, CancellationToken cancellationToken = default)
    {
        using var response = await Client.GetAsync($"{engine.ApiBase}/models", cancellationToken);
        response.EnsureSuccessStatusCode();
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        if (!json.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            return [];

        return data.EnumerateArray()
            .Select(item => item.TryGetProperty("id", out var id) ? id.GetString() : null)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static async Task<string> ChatAsync(
        Engine engine, string model, IReadOnlyList<LocalChatMessage> messages, CancellationToken cancellationToken = default)
    {
        using var response = await Client.PostAsJsonAsync($"{engine.ApiBase}/chat/completions", new
        {
            model,
            messages = messages.Select(m => new { role = m.Role, content = m.Content }),
            stream = false,
        }, cancellationToken);

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"HTTP {(int)response.StatusCode}: {body[..Math.Min(body.Length, 700)]}");

        using var json = JsonDocument.Parse(body);
        if (!json.RootElement.TryGetProperty("choices", out var choices)
            || choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0
            || !choices[0].TryGetProperty("message", out var message)
            || !message.TryGetProperty("content", out var content))
            throw new JsonException("The local AI returned no message.");
        return content.ValueKind == JsonValueKind.String ? content.GetString() ?? "" : content.ToString();
    }
}
