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
    public const int Size = 16;

    private static readonly Dictionary<string, Image?> Cache = new(StringComparer.Ordinal);

    public static Image? Get(string? dataUri)
    {
        if (string.IsNullOrWhiteSpace(dataUri)) return null;
        if (Cache.TryGetValue(dataUri, out var hit)) return hit;

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
                var fixedSize = new Bitmap(Size, Size);
                using (var g = Graphics.FromImage(fixedSize))
                {
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                    g.DrawImage(raw, new Rectangle(0, 0, Size, Size));
                }
                image = fixedSize;
            }
        }
        catch
        {
            image = null;   // 壊れたアイコンは無かったことにする
        }

        // 上限を決めておかないと、取り込み直後に何千枚も抱えたままになる。
        if (Cache.Count > 3000) Cache.Clear();
        Cache[dataUri] = image;
        return image;
    }
}
