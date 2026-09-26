namespace Voyager;

/// <summary>
/// ブックマークに保存されている data URI のファビコンを Image に直す。
///
/// 取り込んだ 1,000 件以上が同じ絵を使い回すので、data URI 文字列そのものを鍵にして
/// 1 枚だけ持つ。壊れた base64 は珍しくないので、失敗したら黙って null を返す
/// （そのブックマークはアイコン無しで出る。例外で落とす価値は無い）。
/// </summary>
internal static class Favicons
{
    /// <summary>96 dpi で描くときの一辺。実際の画素数は拡大率に合わせて呼ぶ側が決める。</summary>
    public const int Size = 16;

    /// <summary>
    /// 鍵に画素数を含める。16px 決め打ちで焼くと、200% の画面でも 16px のまま描かれて
    /// 文字だけ大きくなり、ファビコンが豆粒に見える。拡大率ごとに別の絵を持つ。
    /// </summary>
    private static readonly Dictionary<(string, int), Image?> Cache = [];

    public static Image? Get(string? dataUri, int px)
    {
        if (string.IsNullOrWhiteSpace(dataUri)) return null;
        px = Math.Clamp(px, 8, 128);

        var key = (dataUri, px);
        if (Cache.TryGetValue(key, out var hit)) return hit;

        Image? image = null;
        try
        {
            var comma = dataUri.IndexOf(',');
            if (comma > 0 && dataUri.AsSpan(0, comma).Contains("base64", StringComparison.OrdinalIgnoreCase))
            {
                var bytes = Convert.FromBase64String(dataUri[(comma + 1)..].Trim());
                using var ms = new MemoryStream(bytes);
                using var raw = Image.FromStream(ms);

                // 元が 32px や 48px のこともある。描くたびに縮めないよう、ここで一度だけ揃える。
                var fixedSize = new Bitmap(px, px);
                using (var g = Graphics.FromImage(fixedSize))
                {
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                    g.DrawImage(raw, new Rectangle(0, 0, px, px));
                }
                image = fixedSize;
            }
        }
        catch
        {
            image = null;   // 壊れたアイコンは無かったことにする
        }

        // 上限を決めておかないと、取り込み直後に何千枚も抱えたままになる。
        // 拡大率をまたぐと同じ絵を 2 通り持つので、以前より早く上限に届く。
        if (Cache.Count > 3000) Cache.Clear();
        Cache[key] = image;
        return image;
    }
}
