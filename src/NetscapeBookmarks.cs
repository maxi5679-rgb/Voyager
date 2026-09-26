using System.Net;
using System.Text;

namespace Voyager;

internal sealed record ImportOptions(
    bool SkipEmptyFolders = true,
    bool ImportIcons = true,
    /// <summary>取り込み先をこの名前のフォルダにまとめる。null なら親へ直接。</summary>
    string? IntoFolderTitle = null,
    /// <summary>
    /// PERSONAL_TOOLBAR_FOLDER（＝相手のブックマークバー）の中身を、
    /// フォルダを作らずにこの id の直下へ入れる。null なら普通のフォルダとして取り込む。
    /// </summary>
    string? ToolbarParentId = null);

internal sealed record ImportResult(
    int Folders, int Links, int SkippedEmptyFolders, int BadDates, int Icons, int ToBar = 0)
{
    public override string ToString() =>
        Strings.ImportSummary(Folders, Links, SkippedEmptyFolders, BadDates, Icons, ToBar);
}

/// <summary>
/// Netscape Bookmark File Format（Chrome / Edge / Firefox / Safari 共通の bookmarks.html）
/// の読み書き。
///
/// 正規表現で属性を拾うと壊れる。ICON 属性には base64 の PNG が丸ごと入り、
/// HREF にはクエリ文字列が入るので、どちらにも "=" や英数字の塊が現れる。
/// 引用符の内側を見ない素朴な走査が必要なので、小さな字句解析を自前で持つ。
/// </summary>
internal static class NetscapeBookmarks
{
    /// <summary>これより古い / 新しい日時は壊れた値として捨てる。</summary>
    private static readonly DateTimeOffset MinSane = new(1995, 1, 1, 0, 0, 0, TimeSpan.Zero);

    // ===================== 読み込み =====================

    public static ImportResult Import(BookmarkStore store, string html, string parentId, ImportOptions? options = null)
    {
        var opt = options ?? new ImportOptions();

        var root = ParseTree(html, out var badDates);

        if (opt.SkipEmptyFolders) PruneEmpty(root);

        var target = parentId;
        if (!string.IsNullOrWhiteSpace(opt.IntoFolderTitle))
            target = store.AddFolder(parentId, opt.IntoFolderTitle!).Id;

        var folders = 0; var links = 0; var icons = 0; var skipped = 0; var toBar = 0;
        if (opt.SkipEmptyFolders) skipped = CountPruned(root);

        void Walk(ParsedFolder f, string into)
        {
            foreach (var child in f.Items)
            {
                if (child is ParsedFolder sub)
                {
                    // 相手のバーは「フォルダ」ではなく「こちらのバー」。入れ子を一段減らして中身を直に入れる。
                    if (sub.Toolbar && opt.ToolbarParentId is not null)
                    {
                        var before = links;
                        Walk(sub, opt.ToolbarParentId);
                        toBar += links - before;
                        continue;
                    }

                    var node = store.AddFolder(into, sub.Title, sub.AddedAt);
                    folders++;
                    Walk(sub, node.Id);
                }
                else if (child is ParsedLink link)
                {
                    var icon = opt.ImportIcons ? link.Icon : null;
                    if (icon is not null) icons++;
                    store.AddLink(into, link.Title, link.Url, icon, link.AddedAt);
                    links++;
                }
            }
        }

        Walk(root, target);
        return new ImportResult(folders, links, skipped, badDates, icons, toBar);
    }

    // ===================== 書き出し =====================

    public static string Export(BookmarkStore store)
    {
        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE NETSCAPE-Bookmark-file-1>\r\n");
        sb.Append("<!-- This is an automatically generated file.\r\n");
        sb.Append("     It will be read and overwritten.\r\n");
        sb.Append("     DO NOT EDIT! -->\r\n");
        sb.Append("<META HTTP-EQUIV=\"Content-Type\" CONTENT=\"text/html; charset=UTF-8\">\r\n");
        sb.Append("<TITLE>Bookmarks</TITLE>\r\n<H1>Bookmarks</H1>\r\n<DL><p>\r\n");

        // バーは PERSONAL_TOOLBAR_FOLDER として出す。他のブラウザがバーとして読む。
        WriteFolder(sb, store, BookmarkStore.RootBar, 1, toolbar: true);
        // 未整理は Chrome の「その他のブックマーク」に当たる位置へ
        WriteFolder(sb, store, BookmarkStore.RootOther, 1, toolbar: false,
                    titleOverride: "その他のブックマーク");

        sb.Append("</DL><p>\r\n");
        return sb.ToString();
    }

    private static void WriteFolder(StringBuilder sb, BookmarkStore store, string id, int depth,
                                    bool toolbar, string? titleOverride = null)
    {
        if (store.Get(id) is not { } folder || folder.IsDeleted) return;
        var pad = new string(' ', depth * 4);

        sb.Append(pad).Append("<DT><H3");
        if (folder.AddedAt is { } a) sb.Append(" ADD_DATE=\"").Append(a.ToUnixTimeSeconds()).Append('"');
        if (toolbar) sb.Append(" PERSONAL_TOOLBAR_FOLDER=\"true\"");
        sb.Append('>').Append(Esc(titleOverride ?? folder.Title)).Append("</H3>\r\n");
        sb.Append(pad).Append("<DL><p>\r\n");

        foreach (var n in store.Children(id))
        {
            if (n.IsFolder) WriteFolder(sb, store, n.Id, depth + 1, toolbar: false);
            else
            {
                sb.Append(pad).Append("    <DT><A HREF=\"").Append(Esc(n.Url ?? ""))
                  .Append('"');
                if (n.AddedAt is { } la) sb.Append(" ADD_DATE=\"").Append(la.ToUnixTimeSeconds()).Append('"');
                if (n.Icon is { Length: > 0 } ic) sb.Append(" ICON=\"").Append(Esc(ic)).Append('"');
                sb.Append('>').Append(Esc(n.Title)).Append("</A>\r\n");
            }
        }

        sb.Append(pad).Append("</DL><p>\r\n");
    }

    private static string Esc(string s) => WebUtility.HtmlEncode(s);

    // ===================== 解析 =====================

    private abstract record ParsedItem;

    private sealed record ParsedLink(string Title, string Url, string? Icon, DateTimeOffset? AddedAt) : ParsedItem;

    private sealed record ParsedFolder(string Title, DateTimeOffset? AddedAt) : ParsedItem
    {
        public List<ParsedItem> Items { get; } = [];
        public int PrunedFolders { get; set; }

        /// <summary>PERSONAL_TOOLBAR_FOLDER が付いていた。相手のブックマークバー。</summary>
        public bool Toolbar { get; init; }
    }

    private static ParsedFolder ParseTree(string html, out int badDates)
    {
        var bad = 0;
        var root = new ParsedFolder("(root)", null);
        var stack = new Stack<ParsedFolder>();
        stack.Push(root);

        ParsedFolder? pending = null;   // <H3> は読んだが、まだ <DL> が来ていないフォルダ
        var i = 0;

        while (i < html.Length)
        {
            var lt = html.IndexOf('<', i);
            if (lt < 0) break;

            if (!ReadTag(html, lt, out var name, out var attrs, out var after)) break;
            i = after;

            switch (name)
            {
                case "h3":
                {
                    var title = ReadTextUntilClose(html, ref i, "h3");
                    pending = new ParsedFolder(title, ParseDate(attrs, "add_date", ref bad))
                    {
                        Toolbar = attrs.TryGetValue("personal_toolbar_folder", out var tb)
                                  && tb.Equals("true", StringComparison.OrdinalIgnoreCase),
                    };
                    break;
                }
                case "a":
                {
                    var title = ReadTextUntilClose(html, ref i, "a");
                    if (attrs.TryGetValue("href", out var href) && href.Length > 0)
                    {
                        attrs.TryGetValue("icon", out var icon);
                        stack.Peek().Items.Add(new ParsedLink(
                            title, WebUtility.HtmlDecode(href),
                            string.IsNullOrWhiteSpace(icon) ? null : icon,
                            ParseDate(attrs, "add_date", ref bad)));
                    }
                    break;
                }
                case "dl":
                {
                    // <H3> の直後の <DL> だけが新しいフォルダの中身。
                    // 一番外側の <DL> のように対応する <H3> が無いものは、
                    // 今のフォルダをもう一度積んで </DL> との数を合わせる。
                    if (pending is not null)
                    {
                        stack.Peek().Items.Add(pending);
                        stack.Push(pending);
                        pending = null;
                    }
                    else stack.Push(stack.Peek());
                    break;
                }
                case "/dl":
                {
                    if (stack.Count > 1) stack.Pop();
                    break;
                }
            }
        }

        badDates = bad;
        return root;
    }

    /// <summary>
    /// タグを1つ読む。属性値は引用符の内側をそのまま通すので、
    /// base64 でも URL でも途中で切れない。
    /// </summary>
    private static bool ReadTag(string s, int lt, out string name,
                                out Dictionary<string, string> attrs, out int after)
    {
        name = ""; attrs = new(StringComparer.OrdinalIgnoreCase); after = lt + 1;
        var i = lt + 1;
        if (i >= s.Length) return false;

        // コメントと宣言は読み飛ばす
        if (s[i] == '!')
        {
            var end = s.IndexOf('>', i);
            after = end < 0 ? s.Length : end + 1;
            return true;
        }

        var start = i;
        if (i < s.Length && s[i] == '/') i++;
        while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '/')) i++;
        name = s[start..i].ToLowerInvariant();

        while (i < s.Length && s[i] != '>')
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
            if (i >= s.Length || s[i] == '>') break;

            var ns = i;
            while (i < s.Length && s[i] != '=' && s[i] != '>' && !char.IsWhiteSpace(s[i])) i++;
            var an = s[ns..i].ToLowerInvariant();

            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
            if (i < s.Length && s[i] == '=')
            {
                i++;
                while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
                if (i < s.Length && (s[i] == '"' || s[i] == '\''))
                {
                    var quote = s[i++];
                    var vs = i;
                    while (i < s.Length && s[i] != quote) i++;   // ここが肝。引用符の内側は素通し
                    if (an.Length > 0) attrs[an] = s[vs..i];
                    if (i < s.Length) i++;
                }
                else
                {
                    var vs = i;
                    while (i < s.Length && s[i] != '>' && !char.IsWhiteSpace(s[i])) i++;
                    if (an.Length > 0) attrs[an] = s[vs..i];
                }
            }
            else if (an.Length > 0) attrs[an] = "";
        }

        after = i < s.Length ? i + 1 : s.Length;
        return true;
    }

    private static string ReadTextUntilClose(string s, ref int i, string tag)
    {
        var close = s.IndexOf("</" + tag, i, StringComparison.OrdinalIgnoreCase);
        if (close < 0) { var all = s[i..]; i = s.Length; return Clean(all); }
        var text = s[i..close];
        var gt = s.IndexOf('>', close);
        i = gt < 0 ? s.Length : gt + 1;
        return Clean(text);
    }

    private static string Clean(string s) => WebUtility.HtmlDecode(s).Trim();

    /// <summary>
    /// ADD_DATE は Unix 秒。ただし実ファイルには 0 や 5849 年のような値が混ざる。
    /// 常識的な範囲に収まらないものは「日時不明」として null にする。
    /// そうしないと「追加順」で壊れた値が永久に先頭へ居座る。
    /// </summary>
    private static DateTimeOffset? ParseDate(Dictionary<string, string> attrs, string key, ref int bad)
    {
        if (!attrs.TryGetValue(key, out var raw) || !long.TryParse(raw, out var sec)) return null;
        try
        {
            var t = DateTimeOffset.FromUnixTimeSeconds(sec);
            if (t < MinSane || t > DateTimeOffset.UtcNow.AddDays(1)) { bad++; return null; }
            return t;
        }
        catch (ArgumentOutOfRangeException) { bad++; return null; }
    }

    // ---------- 空フォルダの掃除 ----------

    private static bool PruneEmpty(ParsedFolder f)
    {
        var kept = new List<ParsedItem>();
        var pruned = 0;
        foreach (var item in f.Items)
        {
            if (item is ParsedFolder sub)
            {
                var empty = PruneEmpty(sub);
                pruned += sub.PrunedFolders;
                if (empty) { pruned++; continue; }
            }
            kept.Add(item);
        }
        f.Items.Clear();
        f.Items.AddRange(kept);
        f.PrunedFolders = pruned;
        return f.Items.Count == 0;
    }

    /// <summary>
    /// PruneEmpty の中で子の分もすでに親へ足し込んであるので、根の値がそのまま総数。
    /// ここで子を再帰的に足すと二重に数える（実際それで 27 を 54 と出していた）。
    /// </summary>
    private static int CountPruned(ParsedFolder f) => f.PrunedFolders;
}
