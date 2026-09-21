namespace Voyager;

internal sealed record Engine(string Id, string Name, string Maker, string Home, string AskFormat)
{
    /// <summary>質問文を、その AI のクエリ付き URL に変換する。</summary>
    public string Ask(string text)
    {
        var q = Uri.EscapeDataString(text.Trim());
        if (q.Length > 1800) q = q[..1800];
        return string.Format(AskFormat, q);
    }
}

internal static class Engines
{
    public static readonly IReadOnlyList<Engine> All =
    [
        new("grok",       "Grok",       "xAI",        "https://grok.com/",                  "https://grok.com/?q={0}"),
        new("chatgpt",    "ChatGPT",    "OpenAI",     "https://chatgpt.com/",               "https://chatgpt.com/?q={0}"),
        new("claude",     "Claude",     "Anthropic",  "https://claude.ai/new",              "https://claude.ai/new?q={0}"),
        new("gemini",     "Gemini",     "Google",     "https://gemini.google.com/app",      "https://gemini.google.com/app?q={0}"),
        new("perplexity", "Perplexity", "Perplexity", "https://www.perplexity.ai/",         "https://www.perplexity.ai/search?q={0}"),
        new("copilot",    "Copilot",    "Microsoft",  "https://copilot.microsoft.com/",     "https://copilot.microsoft.com/?q={0}"),
    ];

    public static Engine? ById(string? id) =>
        id is null ? null : All.FirstOrDefault(e => e.Id == id);

    public static Engine Default => All[0];
}

internal sealed record QuickSite(string Label, string Url);

internal static class QuickSites
{
    public static readonly IReadOnlyList<QuickSite> All =
    [
        new("Google",  "https://www.google.co.jp/"),
        new("Yahoo!",  "https://www.yahoo.co.jp/"),
        new("YouTube", "https://www.youtube.com/"),
    ];
}
