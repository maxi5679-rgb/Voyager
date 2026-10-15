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

/// <summary>
/// 動画を作るサービス。どこも URL で指示文を受け取らないので、
/// 指示文はクリップボードに入れておき、サイトを開くだけにする。
/// </summary>
internal sealed record VideoService(string Id, string Name, string Maker, string Url);

internal static class VideoServices
{
    public static readonly IReadOnlyList<VideoService> All =
    [
        new("sora",   "Sora",        "OpenAI",   "https://sora.chatgpt.com/"),
        new("flow",   "Google Flow", "Google",   "https://labs.google/fx/tools/flow"),
        new("runway", "Runway",      "Runway",   "https://app.runwayml.com/"),
        new("kling",  "Kling",       "Kuaishou", "https://app.klingai.com/global/"),
    ];

    public static VideoService? ById(string? id) =>
        id is null ? null : All.FirstOrDefault(v => v.Id == id);

    public static VideoService Default => All[0];
}

/// <summary>
/// 質問の用途。アドレス欄の左のボタンで切り替える。
/// 「ふだん」は今選んでいる AI に送り、ほかは設定画面で用途ごとに決めた先に送る。
/// </summary>
internal static class Purposes
{
    public const string Chat = "chat";
    public const string Search = "search";
    public const string Image = "image";
    public const string Video = "video";

    public static readonly string[] All = [Chat, Search, Image, Video];

    public static string Label(string id) => id switch
    {
        Search => Strings.PurposeSearch,
        Image => Strings.PurposeImage,
        Video => Strings.PurposeVideo,
        _ => Strings.PurposeChat,
    };

    /// <summary>調べもの・画像に割り当てた AI。決めていなければ（知らない id でも）既定の先。</summary>
    public static Engine EngineFor(AppSettings s, string purpose) =>
        Engines.ById(s.PurposeAi?.GetValueOrDefault(purpose))
        ?? Engines.ById(purpose == Search ? "perplexity" : "chatgpt")
        ?? Engines.Default;

    public static VideoService VideoFor(AppSettings s) =>
        VideoServices.ById(s.PurposeAi?.GetValueOrDefault(Video)) ?? VideoServices.Default;
}

internal sealed record QuickSite(string Label, string Url);

internal static class QuickSites
{
    public static string GoogleUrl => Strings.Current == "en" ? "https://www.google.com/" : "https://www.google.co.jp/";
    public static string YahooUrl => Strings.Current == "en" ? "https://www.yahoo.com/" : "https://www.yahoo.co.jp/";

    // Resolve on each access so changing the UI language also changes built-in links.
    public static IReadOnlyList<QuickSite> All =>
    [
        new("Google",  GoogleUrl),
        new("Yahoo!",  YahooUrl),
        new("YouTube", "https://www.youtube.com/"),
    ];
}
