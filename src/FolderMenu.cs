namespace Voyager;

/// <summary>
/// 右クリックの「フォルダへ移動 ▶」。バーとサイドバーの両方から使う。
///
/// フォルダの木をそのままサブメニューにする。子フォルダを持つフォルダは開くと
/// 先頭に「ここに入れる」があり、その下に子フォルダが並ぶ。子の無いフォルダは押せばそこへ入る。
///
/// 中身は開いたときに作る。取り込んだブックマークはフォルダが数百あることがあり、
/// 右クリックのたびに全部を組み立てると重いため。
/// </summary>
internal static class FolderMenu
{
    /// <summary>深く潜りすぎるメニューは操作できない。バーのフォルダメニューと同じ 5 段で止める。</summary>
    private const int MaxDepth = 5;

    public static ToolStripMenuItem MoveTo(BookmarkStore store, BookmarkNode node, int dpi, Action<string> move)
    {
        var root = new ToolStripMenuItem(Strings.MoveToFolder);
        root.DropDownOpening += (_, _) => Log.Write("move menu: opened");
        foreach (var id in new[] { BookmarkStore.RootBar, BookmarkStore.RootOther })
            if (store.Get(id) is { } top)
                root.DropDownItems.Add(Entry(store, node, top, dpi, move, 0));
        return root;
    }

    private static ToolStripItem Entry(BookmarkStore store, BookmarkNode node, BookmarkNode folder,
                                       int dpi, Action<string> move, int depth)
    {
        var full = Strings.FolderName(folder.Title).Replace("\r", " ").Replace("\n", " ").Trim();
        var shown = DarkMenu.Fit(full, dpi);
        var here = folder.Id == node.ParentId;   // もう入っている所は押せなくする

        var subs = SubFolders(store, node, folder.Id);
        if (subs.Count == 0 || depth >= MaxDepth)
        {
            var leaf = DarkMenu.Item(shown, () => move(folder.Id), !here);
            if (full != shown) leaf.ToolTipText = full;
            return leaf;
        }

        var item = new ToolStripMenuItem(shown);
        if (full != shown) item.ToolTipText = full;

        // 開くまで中身は作らない。空だと矢印が出ないので、仮の 1 件を入れておく。
        item.DropDownItems.Add(new ToolStripMenuItem("…") { Enabled = false });
        var built = false;
        item.DropDownOpening += (_, _) =>
        {
            if (built) return;
            built = true;
            item.DropDownItems.Clear();
            item.DropDownItems.Add(DarkMenu.Item(Strings.PutHere, () => move(folder.Id), !here));
            item.DropDownItems.Add(new ToolStripSeparator());
            var subs2 = SubFolders(store, node, folder.Id);
            foreach (var s in subs2)
                item.DropDownItems.Add(Entry(store, node, s, dpi, move, depth + 1));
            Log.Write($"move menu: opened a folder with {subs2.Count} subfolders (depth {depth})");
        };
        return item;
    }

    /// <summary>
    /// 行き先に出してよい子フォルダ。動かすのがフォルダなら、それ自身（とその下）は除く。
    /// 自分の中へは入れられない。
    /// </summary>
    private static List<BookmarkNode> SubFolders(BookmarkStore store, BookmarkNode node, string parentId) =>
        store.Children(parentId).Where(c => c.IsFolder && c.Id != node.Id).ToList();
}
