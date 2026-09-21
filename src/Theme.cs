using System.Drawing;

namespace Voyager;

/// <summary>Chromium 版（栞）の app.css から移植した配色。</summary>
internal static class Theme
{
    public static readonly Color Background = ColorTranslator.FromHtml("#0c0d0b");
    public static readonly Color Surface = ColorTranslator.FromHtml("#141712");
    public static readonly Color Card = ColorTranslator.FromHtml("#171912");
    public static readonly Color CardHover = ColorTranslator.FromHtml("#1d2218");
    public static readonly Color Border = ColorTranslator.FromHtml("#2d3228");
    public static readonly Color Accent = ColorTranslator.FromHtml("#7d9a4c");
    public static readonly Color Text = ColorTranslator.FromHtml("#f3f4ef");
    public static readonly Color Muted = ColorTranslator.FromHtml("#9aa190");
    public static readonly Color Lead = ColorTranslator.FromHtml("#c6cbb8");

    public const string FontStack = "Segoe UI,Meiryo,system-ui,sans-serif";

    public static Font Ui(float size = 9f, FontStyle style = FontStyle.Regular)
        => new("Segoe UI", size, style, GraphicsUnit.Point);

    /// <summary>内部ページ（選択画面・設定）の共通 CSS。</summary>
    public static string PageCss => $$"""
        :root { color-scheme: dark; }
        * { box-sizing: border-box; }
        body { margin:0; font:16px/1.6 {{FontStack}}; background:#0c0d0b; color:#f3f4ef;
               -webkit-user-select:none; user-select:none; }
        .panel { max-width:920px; margin:0 auto; padding:48px 24px 64px; }
        .kicker { letter-spacing:.12em; text-transform:uppercase; color:#9aa190; font-size:12px; margin:0; }
        h1 { font-size:42px; margin:8px 0 12px; font-weight:600; }
        h2 { font-size:15px; margin:28px 0 10px; color:#c6cbb8; font-weight:600; }
        .lead { color:#c6cbb8; margin:0 0 28px; }
        .grid { display:grid; gap:12px; }
        .grid.two { grid-template-columns:repeat(2,minmax(0,1fr)); }
        .grid.three { grid-template-columns:repeat(3,minmax(0,1fr)); }
        button.card, .choice {
            display:flex; align-items:center; gap:4px; text-align:left; padding:16px;
            border-radius:14px; border:1px solid #2d3228; background:#171912; color:inherit;
            cursor:pointer; font:inherit; transition:border-color .12s, background .12s;
        }
        button.card { flex-direction:column; align-items:stretch; }
        button.card:hover, .choice:hover { border-color:#7d9a4c; background:#1d2218; }
        .choice.on { border-color:#7d9a4c; background:#1d2218; }
        .center { align-items:center; justify-content:center; text-align:center; font-size:18px; }
        strong { font-weight:600; }
        small, .meta { color:#9aa190; font-size:12px; }
        .meta { margin-top:8px; }
        .field {
            width:100%; padding:12px 14px; border-radius:12px; border:1px solid #2d3228;
            background:#101309; color:#f3f4ef; font:inherit;
        }
        .field:focus { outline:none; border-color:#7d9a4c; }
        label.check { display:flex; align-items:center; gap:10px; color:#c6cbb8; cursor:pointer; }
        .brand { display:flex; align-items:baseline; gap:12px; }
        .brand .sub { color:#9aa190; font-size:13px; letter-spacing:.08em; }

        /* 右クリックメニュー（ページ内に描く） */
        #ctx {
            position:fixed; z-index:9999; min-width:200px; padding:6px;
            border:1px solid #2d3228; border-radius:10px; background:#171912;
            box-shadow:0 12px 32px rgba(0,0,0,.55); font-size:14px;
        }
        #ctx .item { padding:8px 12px; border-radius:6px; cursor:pointer; white-space:nowrap; }
        #ctx .item:hover { background:#1d2218; }
        #ctx .item.disabled { color:#5c6155; cursor:default; }
        #ctx .item.disabled:hover { background:transparent; }
        #ctx .sep { height:1px; margin:6px 4px; background:#2d3228; }
        """;
}
