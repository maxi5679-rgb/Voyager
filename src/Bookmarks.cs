using System.Text.Json;
using System.Text.Json.Serialization;

namespace Voyager;

/// <summary>
/// ブックマーク1件。フォルダもリンクも同じ型で、Kind で区別する。
///
/// 木を入れ子のまま持たず、親を指す平らな一覧にしてある。
/// 将来の同期で「id 単位で新しいほうを採る」という単純な突き合わせができるのと、
/// 移動が ParentId と Index の書き換えだけで済むため。
/// </summary>
internal sealed class BookmarkNode
{
    public string Id { get; set; } = "";
    public string? ParentId { get; set; }

    /// <summary>"folder" または "link"</summary>
    public string Kind { get; set; } = "link";

    public string Title { get; set; } = "";
    public string? Url { get; set; }

    /// <summary>data:image/png;base64,... 形式のファビコン。無ければ null。</summary>
    public string? Icon { get; set; }

    /// <summary>同じ親の中での並び順。名前順ではなく、自分で並べた順を保つ。</summary>
    public int Index { get; set; }

    /// <summary>追加日時。インポート元が壊れた値を持っていることがあるので null を許す。</summary>
    public DateTimeOffset? AddedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>消したものは即座に捨てず、印だけ付ける（同期で復活させないため）。</summary>
    public DateTimeOffset? DeletedAt { get; set; }

    [JsonIgnore] public bool IsFolder => Kind == "folder";
    [JsonIgnore] public bool IsLink => Kind == "link";
    [JsonIgnore] public bool IsDeleted => DeletedAt is not null;
}

/// <summary>ブックマーク全体。%APPDATA%\Voyager\bookmarks.json に置く。</summary>
internal sealed class BookmarkStore
{
    /// <summary>自分で選んで置いた常用のもの。★では増えない。</summary>
    public const string RootBar = "bar";

    /// <summary>★で放り込む未整理。既定の行き先で、既定の並びは追加順。</summary>
    public const string RootOther = "other";

    public int Version { get; set; } = 1;
    public List<BookmarkNode> Nodes { get; set; } = [];

    [JsonIgnore] private Dictionary<string, BookmarkNode>? _byId;

    [JsonIgnore]
    public static string FilePath { get; } = Path.Combine(AppSettings.Dir, "bookmarks.json");

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    // ---------- 出し入れ ----------

    public static BookmarkStore Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var s = JsonSerializer.Deserialize<BookmarkStore>(File.ReadAllText(FilePath));
                if (s is not null) { s.EnsureRoots(); return s; }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // 壊れていたら空で続行する。既存ファイルは上書きせずに残す。
            Log.Write("bookmarks: load failed, starting empty");
        }

        var fresh = new BookmarkStore();
        fresh.EnsureRoots();
        return fresh;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(AppSettings.Dir);
            // 書き込み中に落ちても元が消えないよう、一度別名で書いてから差し替える
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, Options));
            File.Move(tmp, FilePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Write("bookmarks: save failed");
        }
    }

    // ---------- 検索と取り出し ----------

    private Dictionary<string, BookmarkNode> ById =>
        _byId ??= Nodes.ToDictionary(n => n.Id);

    public BookmarkNode? Get(string id) => ById.GetValueOrDefault(id);

    /// <summary>指定フォルダの中身を並び順で。消したものは返さない。</summary>
    public List<BookmarkNode> Children(string parentId) =>
        Nodes.Where(n => n.ParentId == parentId && !n.IsDeleted)
             .OrderBy(n => n.Index)
             .ToList();

    /// <summary>タイトルと URL の部分一致。2,400 件規模なら素直に総当たりで足りる。</summary>
    public List<BookmarkNode> Search(string text, int limit = 200)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        var q = text.Trim();
        return Nodes
            .Where(n => n.IsLink && !n.IsDeleted &&
                        (n.Title.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                         (n.Url?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)))
            .OrderByDescending(n => n.AddedAt ?? DateTimeOffset.MinValue)
            .Take(limit)
            .ToList();
    }

    /// <summary>フォルダのパスを「親 / 子 / 孫」の形で返す。検索結果の所在表示用。</summary>
    public string PathOf(BookmarkNode node)
    {
        var parts = new List<string>();
        var cur = node.ParentId is null ? null : Get(node.ParentId);
        var guard = 0;
        while (cur is not null && guard++ < 64)
        {
            parts.Insert(0, cur.Title);
            cur = cur.ParentId is null ? null : Get(cur.ParentId);
        }
        return string.Join(" / ", parts);
    }

    // ---------- 書き換え ----------

    public void EnsureRoots()
    {
        _byId = null;
        if (Get(RootBar) is null)
            Nodes.Insert(0, new BookmarkNode
            {
                Id = RootBar, Kind = "folder", Title = "ブックマーク バー",
                Index = 0, AddedAt = DateTimeOffset.UtcNow,
            });
        if (Get(RootOther) is null)
            Nodes.Add(new BookmarkNode
            {
                Id = RootOther, Kind = "folder", Title = "未整理",
                Index = 1, AddedAt = DateTimeOffset.UtcNow,
            });
        _byId = null;
    }

    /// <summary>そのフォルダの配下にあるリンクの総数（入れ子も数える）。</summary>
    public int CountLinks(string id)
    {
        var n = 0;
        var stack = new Stack<string>();
        stack.Push(id);
        var guard = 0;
        while (stack.Count > 0 && guard++ < 100_000)
        {
            foreach (var c in Children(stack.Pop()))
            {
                if (c.IsLink) n++; else stack.Push(c.Id);
            }
        }
        return n;
    }

    public int NextIndex(string parentId)
    {
        var used = Nodes.Where(n => n.ParentId == parentId).Select(n => n.Index).ToList();
        return used.Count == 0 ? 0 : used.Max() + 1;
    }

    public BookmarkNode AddFolder(string parentId, string title, DateTimeOffset? addedAt = null)
    {
        var n = new BookmarkNode
        {
            Id = NewId(), ParentId = parentId, Kind = "folder",
            Title = title, Index = NextIndex(parentId),
            AddedAt = addedAt ?? DateTimeOffset.UtcNow,
        };
        Nodes.Add(n); _byId = null;
        return n;
    }

    public BookmarkNode AddLink(string parentId, string title, string url,
                                string? icon = null, DateTimeOffset? addedAt = null)
    {
        var n = new BookmarkNode
        {
            Id = NewId(), ParentId = parentId, Kind = "link",
            Title = title, Url = url, Icon = icon, Index = NextIndex(parentId),
            AddedAt = addedAt ?? DateTimeOffset.UtcNow,
        };
        Nodes.Add(n); _byId = null;
        return n;
    }

    /// <summary>まとめて消した記録。RestoreRemoved で元の位置へ戻せる。</summary>
    public sealed class Removal
    {
        /// <summary>選ばれて消したもの。親と、消す直前の並び位置（0 から）。</summary>
        public List<(string Id, string Parent, int Position)> Tops { get; } = [];

        /// <summary>印を付けたもの全部（フォルダの中身も含む）。</summary>
        public List<string> All { get; } = [];
    }

    /// <summary>
    /// まとめて消す。根（バー・未整理）と、既に消えているもの、ほかの選択の中に入っているものは飛ばす。
    /// 並び位置を覚えておくので、RestoreRemoved で同じ場所に戻せる。
    /// </summary>
    public Removal RemoveMany(IEnumerable<string> ids)
    {
        var r = new Removal();
        var chosen = ids.Where(id => id is not (RootBar or RootOther)).ToHashSet();

        // 選んだフォルダの中身まで個別に選ばれていたら、中身の方は数えない（フォルダごと消える）。
        bool InsideChosen(BookmarkNode n)
        {
            for (var p = n.ParentId is null ? null : Get(n.ParentId); p is not null; p = p.ParentId is null ? null : Get(p.ParentId))
                if (chosen.Contains(p.Id)) return true;
            return false;
        }

        var tops = chosen.Select(Get)
            .Where(n => n is { IsDeleted: false, ParentId: not null } && !InsideChosen(n))
            .Cast<BookmarkNode>()
            .ToList();

        foreach (var group in tops.GroupBy(n => n.ParentId!))
        {
            var order = Children(group.Key);
            foreach (var n in group) r.Tops.Add((n.Id, group.Key, order.IndexOf(n)));
        }

        var now = DateTimeOffset.UtcNow;
        foreach (var top in tops)
        {
            var stack = new Stack<string>();
            stack.Push(top.Id);
            while (stack.Count > 0)
            {
                var cur = stack.Pop();
                if (Get(cur) is not { IsDeleted: false } n) continue;
                n.DeletedAt = now;
                n.UpdatedAt = now;
                r.All.Add(cur);
                foreach (var c in Nodes.Where(x => x.ParentId == cur && !x.IsDeleted)) stack.Push(c.Id);
            }
        }
        foreach (var parent in r.Tops.Select(t => t.Parent).Distinct()) Renumber(parent);
        return r;
    }

    /// <summary>RemoveMany を取り消す。消したものを元の親の、元の並び位置へ戻す。</summary>
    public void RestoreRemoved(Removal r)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var id in r.All)
            if (Get(id) is { } n) { n.DeletedAt = null; n.UpdatedAt = now; }

        foreach (var group in r.Tops.GroupBy(t => t.Parent))
        {
            if (Get(group.Key) is not { IsDeleted: false }) continue;   // 親ごと消えていたら、位置は諦める
            var restored = group.Select(t => t.Id).ToHashSet();
            var order = Children(group.Key).Where(n => !restored.Contains(n.Id)).ToList();
            foreach (var t in group.OrderBy(t => t.Position))
                if (Get(t.Id) is { } n) order.Insert(Math.Clamp(t.Position, 0, order.Count), n);
            for (var i = 0; i < order.Count; i++) order[i].Index = i;
        }
    }

    /// <summary>消す。子も一緒に印を付ける。</summary>
    public void Remove(string id)
    {
        var now = DateTimeOffset.UtcNow;
        var stack = new Stack<string>();
        stack.Push(id);
        while (stack.Count > 0)
        {
            var cur = stack.Pop();
            if (Get(cur) is { } n) { n.DeletedAt = now; n.UpdatedAt = now; }
            foreach (var c in Nodes.Where(x => x.ParentId == cur && !x.IsDeleted))
                stack.Push(c.Id);
        }
    }

    public void Move(string id, string newParentId, int newIndex)
    {
        if (Get(id) is not { } n) return;
        n.ParentId = newParentId;
        n.Index = newIndex;
        n.UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// 別のフォルダの末尾へ移す。元のフォルダは詰め直す。
    /// フォルダを自分の下へ入れる（輪になる）移動は黙って断る。
    /// </summary>
    public bool MoveToEnd(string id, string newParentId)
    {
        if (Get(id) is not { } n || n.ParentId == newParentId) return false;
        for (var p = Get(newParentId); p is not null; p = p.ParentId is null ? null : Get(p.ParentId))
            if (p.Id == id) return false;

        var oldParent = n.ParentId;
        Move(id, newParentId, NextIndex(newParentId));
        if (oldParent is not null) Renumber(oldParent);
        return true;
    }

    /// <summary>並びを 0,1,2... に振り直す。ドラッグで入れ替えたあとに呼ぶ。</summary>
    public void Renumber(string parentId)
    {
        var i = 0;
        foreach (var n in Children(parentId)) n.Index = i++;
    }

    private static readonly char[] IdChars = "abcdefghijkmnopqrstuvwxyz23456789".ToCharArray();

    public static string NewId()
    {
        var buf = new char[12];
        var bytes = new byte[12];
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
        for (var i = 0; i < buf.Length; i++) buf[i] = IdChars[bytes[i] % IdChars.Length];
        return new string(buf);
    }
}
