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

        return new { type = "bm:tree", folders, selected };
    }

    /// <summary>1 フォルダ分の中身。</summary>
    public static object Items(BookmarkStore store, string folderId)
    {
        var icons = new List<string>();
        var iconIndex = new Dictionary<string, int>();
        var kids = ChildrenMap(store);
        var items = store.Children(folderId).Select(n => Item(n, kids, icons, iconIndex, path: null)).ToList();
        return new { type = "bm:items", folder = folderId, search = (string?)null, items, icons };
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
        return new { type = "bm:items", folder = (string?)null, search = query, items, icons };
    }

    private static object Item(BookmarkNode n, Dictionary<string, List<BookmarkNode>> kids,
                               List<string> icons, Dictionary<string, int> iconIndex, string? path)
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
        return new { id = n.Id, kind = n.Kind, title = Label(n), url = n.Url, icon, count, path };
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
        if (UrlHelper.IsNavigable(t)) return new Uri(t).ToString();
        var withScheme = "https://" + t;
        return t.Contains('.') && UrlHelper.IsNavigable(withScheme) ? new Uri(withScheme).ToString() : null;
    }
}
