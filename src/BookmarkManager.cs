namespace Voyager;

/// <summary>
/// ブックマークマネージャー（内部ページ）へ渡すデータを作る。
///
/// 中身はページに埋め込まず、開いたあとにメッセージで送る。NavigateToString は 2MB までで、
/// 取り込んだファビコン（1,400 個以上）まで入れると収まらないため。
/// 一覧はフォルダ 1 つ分ずつ送り、ファビコンは同じ絵を 1 回だけ送る。
/// </summary>
internal static class BookmarkManager
{
    public const string PageId = "bookmarks";

    /// <summary>フォルダの木。親より先に子が来ないよう、深さ優先で並べる。</summary>
    public static object Tree(BookmarkStore store, string selected)
    {
        var kids = ChildrenMap(store);
        var counts = new Dictionary<string, int>();
        int Count(string id)
        {
            if (counts.TryGetValue(id, out var c)) return c;
            var n = 0;
            if (kids.TryGetValue(id, out var list))
                foreach (var k in list) n += k.IsLink ? 1 : Count(k.Id);
            return counts[id] = n;
        }

        var folders = new List<object>();
        void Walk(BookmarkNode f, int depth)
        {
            var hasSub = kids.TryGetValue(f.Id, out var list) && list.Any(k => k.IsFolder);
            folders.Add(new { id = f.Id, title = Label(f), depth, count = Count(f.Id), hasSub });
            if (depth >= 16 || list is null) return;
            foreach (var k in list) if (k.IsFolder) Walk(k, depth + 1);
        }
        foreach (var root in new[] { BookmarkStore.RootBar, BookmarkStore.RootOther })
            if (store.Get(root) is { } r) Walk(r, 0);

        return new { type = "bm:tree", folders, selected, dups = DuplicateGroups(store).Count };
    }

    /// <summary>1 フォルダ分の中身。</summary>
    public static object Items(BookmarkStore store, string folderId)
    {
        var icons = new List<string>();
        var iconIndex = new Dictionary<string, int>();
        var kids = ChildrenMap(store);
        var items = store.Children(folderId).Select(n => Item(n, kids, icons, iconIndex, path: null)).ToList();
        return new { type = "bm:items", folder = folderId, search = (string?)null, view = (string?)null, items, icons };
    }

    /// <summary>検索結果。どのフォルダに入っているかも付ける。</summary>
    public static object Search(BookmarkStore store, string query)
    {
        var icons = new List<string>();
        var iconIndex = new Dictionary<string, int>();
        var kids = ChildrenMap(store);
        var items = store.Search(query, 500)
            .Select(n => Item(n, kids, icons, iconIndex, path: PathLabel(store, n)))
            .ToList();
        return new { type = "bm:items", folder = (string?)null, search = query, view = (string?)null, items, icons };
    }

    /// <summary>
    /// 同じアドレスのブックマーク。グループごとに並べ、各グループの先頭は「残す候補」:
    /// ブックマーク バーの中にあるものを先に、次に古いものを先にする。
    /// </summary>
    public static object Duplicates(BookmarkStore store)
    {
        var icons = new List<string>();
        var iconIndex = new Dictionary<string, int>();
        var kids = ChildrenMap(store);
        var items = new List<object>();
        var groups = DuplicateGroups(store);
        for (var g = 0; g < groups.Count && items.Count < 1000; g++)
            foreach (var n in groups[g])
                items.Add(Item(n, kids, icons, iconIndex, path: PathLabel(store, n), group: g));
        return new { type = "bm:items", folder = (string?)null, search = (string?)null, view = "dups", items, icons };
    }

    /// <summary>重複のグループ。2 件以上あるアドレスだけ。</summary>
    public static List<List<BookmarkNode>> DuplicateGroups(BookmarkStore store) =>
        store.Nodes
            .Where(n => n.IsLink && !n.IsDeleted && !string.IsNullOrWhiteSpace(n.Url))
            .GroupBy(n => DupKey(n.Url!))
            .Where(g => g.Count() > 1)
            .Select(g => g.OrderBy(n => InBar(store, n) ? 0 : 1)
                          .ThenBy(n => n.AddedAt ?? DateTimeOffset.MaxValue)
                          .ToList())
            .OrderBy(g => g[0].Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    /// <summary>
    /// 重複を見分けるためのアドレス。# 以降と既定のポート番号を落とし、ホスト名は小文字にそろえ、
    /// 「?」の無いアドレスの末尾の「/」は無視する。http と https、www の有無は別のものとして扱う。
    /// </summary>
    public static string DupKey(string url)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var u)) return url.Trim();
        var key = u.GetComponents(UriComponents.SchemeAndServer | UriComponents.PathAndQuery, UriFormat.UriEscaped);
        return key.Contains('?') ? key : key.TrimEnd('/');
    }

    private static bool InBar(BookmarkStore store, BookmarkNode n)
    {
        var cur = n.ParentId is null ? null : store.Get(n.ParentId);
        for (var guard = 0; cur is not null && guard < 64; guard++)
        {
            if (cur.Id == BookmarkStore.RootBar) return true;
            cur = cur.ParentId is null ? null : store.Get(cur.ParentId);
        }
        return false;
    }

    private static object Item(BookmarkNode n, Dictionary<string, List<BookmarkNode>> kids,
                               List<string> icons, Dictionary<string, int> iconIndex, string? path, int group = -1)
    {
        var icon = -1;
        if (n.IsLink && !string.IsNullOrEmpty(n.Icon))
        {
            if (!iconIndex.TryGetValue(n.Icon, out icon))
            {
                icon = icons.Count;
                icons.Add(n.Icon);
                iconIndex[n.Icon] = icon;
            }
        }
        var count = n.IsFolder && kids.TryGetValue(n.Id, out var list) ? list.Count : 0;
        return new { id = n.Id, kind = n.Kind, title = Label(n), url = n.Url, icon, count, path, parent = n.ParentId, group };
    }

    private static string Label(BookmarkNode n) =>
        (n.IsFolder ? Strings.FolderName(n.Title) : n.Title).Replace("\r", " ").Replace("\n", " ").Trim();

    private static string PathLabel(BookmarkStore store, BookmarkNode n)
    {
        var parts = new List<string>();
        var cur = n.ParentId is null ? null : store.Get(n.ParentId);
        for (var guard = 0; cur is not null && guard < 64; guard++)
        {
            parts.Insert(0, Strings.FolderName(cur.Title));
            cur = cur.ParentId is null ? null : store.Get(cur.ParentId);
        }
        return string.Join(" / ", parts);
    }

    /// <summary>親 id → 子（消したものを除き、並び順）。木を作るたびに全件を舐め直さないため。</summary>
    private static Dictionary<string, List<BookmarkNode>> ChildrenMap(BookmarkStore store) =>
        store.Nodes.Where(n => !n.IsDeleted && n.ParentId is not null)
             .GroupBy(n => n.ParentId!)
             .ToDictionary(g => g.Key, g => g.OrderBy(n => n.Index).ToList());

    /// <summary>
    /// 編集欄に入れられたアドレスを整える。http / https のものと、「example.com/path」のような
    /// スキームを省いたものだけ受け付ける。アドレス欄と違い「google」などの短縮語は解釈しない。
    /// </summary>
    public static string? NormalizeUrl(string? raw)
    {
        var t = (raw ?? "").Trim();
        if (t.Length == 0 || t.Contains(' ')) return null;

        if (Uri.TryCreate(t, UriKind.Absolute, out var u) && UrlHelper.IsNavigable(t))
            return GoodHost(u) ? u.ToString() : null;
        if (t.Contains("://")) return null;   // ftp:// など、http / https 以外

        // スキームを省いたもの。頭に https:// を足すだけだと、「https//example.com」（: 抜け）が
        // 「https」というホストとして通ってしまう。ホスト名にドットがあるかで見分ける。
        return Uri.TryCreate("https://" + t, UriKind.Absolute, out var w) && GoodHost(w) ? w.ToString() : null;
    }

    private static bool GoodHost(Uri u) =>
        u.HostNameType is UriHostNameType.IPv4 or UriHostNameType.IPv6
        || u.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
        || (u.Host.Contains('.') && !u.Host.StartsWith('.') && !u.Host.EndsWith('.'));
}
