using System.Net;
using System.Text;

namespace Voyager;

/// <summary>
/// 内部ページ（AI 選択・スタート・設定）の HTML。
/// これらは専用の WebView2（外部サイトを一切読み込まない）でだけ表示し、
/// そこからのメッセージだけをアプリ操作として受け付ける。
/// </summary>
internal static class Pages
{
    private static string E(string s) => WebUtility.HtmlEncode(s);

    private static string Shell(string title, string body, bool settingsOpen = false, bool hasEngine = false) => $$"""
        <!doctype html>
        <html lang="ja"><head><meta charset="utf-8" />
        <meta http-equiv="Content-Security-Policy"
              content="default-src 'none'; style-src 'unsafe-inline'; script-src 'unsafe-inline'; img-src data:;" />
        <title>{{E(title)}}</title>
        <style>{{Theme.PageCss}}</style>
        </head><body>{{body}}
        <script>
          const send = (m) => window.chrome?.webview?.postMessage(m);
          const CTX = { settings: {{(settingsOpen ? "true" : "false")}}, hasEngine: {{(hasEngine ? "true" : "false")}} };

          document.addEventListener('click', (ev) => {
            const el = ev.target.closest('[data-msg]');
            if (!el) return;
            send(JSON.parse(el.dataset.msg));
          });

          // 右クリックメニューはページ内に描く。
          // OS 側のポップアップは WebView2 にフォーカスがあると開いた瞬間に閉じてしまうため。
          let menuEl = null;
          const closeMenu = () => { if (menuEl) { menuEl.remove(); menuEl = null; } };

          function buildMenu(x, y, target) {
            closeMenu();
            const editable = !!(target && (target.tagName === 'INPUT' || target.tagName === 'TEXTAREA' || target.isContentEditable));
            const selection = String(window.getSelection() || '');
            const items = [];

            if (selection) items.push(['コピー', () => { send({ type: 'clip', text: selection }); }, true]);
            if (editable) {
              items.push(['切り取り', () => { send({ type: 'clip', text: selection }); document.execCommand('delete'); }, !!selection]);
              items.push(['貼り付け', () => send({ type: 'paste' }), true]);
            }
            if (items.length) items.push(null);

            items.push(['新しいタブ', () => send({ type: 'newTab' }), true]);
            items.push(['ホーム', () => send({ type: 'home' }), true]);
            items.push([CTX.settings ? '設定を閉じる' : '設定', () => send({ type: 'toggleSettings' }), true]);
            items.push(['AI を選び直す', () => send({ type: 'picker' }), CTX.hasEngine]);

            const box = document.createElement('div');
            box.id = 'ctx';
            for (const item of items) {
              if (!item) { const sep = document.createElement('div'); sep.className = 'sep'; box.append(sep); continue; }
              const [label, action, enabled] = item;
              const row = document.createElement('div');
              row.className = enabled ? 'item' : 'item disabled';
              row.textContent = label;
              if (enabled) row.addEventListener('mousedown', (e) => { e.preventDefault(); closeMenu(); action(); });
              box.append(row);
            }
            document.body.append(box);
            const w = box.offsetWidth, h = box.offsetHeight;
            box.style.left = Math.min(x, window.innerWidth  - w - 8) + 'px';
            box.style.top  = Math.min(y, window.innerHeight - h - 8) + 'px';
            menuEl = box;
          }

          document.addEventListener('contextmenu', (ev) => {
            ev.preventDefault();
            buildMenu(ev.clientX, ev.clientY, ev.target);
          });
          document.addEventListener('mousedown', (ev) => { if (menuEl && !ev.target.closest('#ctx')) closeMenu(); });
          document.addEventListener('keydown', (ev) => { if (ev.key === 'Escape') closeMenu(); });
          window.addEventListener('blur', closeMenu);

          document.addEventListener('change', (ev) => {
            const el = ev.target;
            if (el.dataset.change) {
              const m = JSON.parse(el.dataset.change);
              m.value = el.type === 'checkbox' ? el.checked : el.value;
              send(m);
            }
          });
        </script>
        </body></html>
        """;

    /// <summary>使う AI を選ぶ画面。</summary>
    public static string Picker()
    {
        var cards = new StringBuilder();
        foreach (var e in Engines.All)
        {
            cards.Append($$"""
                <button class="card" data-msg='{"type":"pickEngine","id":"{{e.Id}}"}'>
                  <strong>{{E(e.Name)}}</strong><small>{{E(e.Maker)}}</small>
                  <span class="meta">WebView2</span>
                </button>
                """);
        }

        return Shell("Voyager", $$"""
            <div class="panel">
              <p class="kicker">AI browser v{{App.Version}}</p>
              <div class="brand"><h1>Voyager</h1><span class="sub">V'Ger</span></div>
              <p class="lead">使う AI の公式サイトを開きます。Google や YouTube もそのまま見られます。</p>
              <div class="grid two">{{cards}}</div>
            </div>
            """, settingsOpen: false, hasEngine: false);
    }

    /// <summary>AI 選択後の行き先画面。</summary>
    public static string Start(Engine engine)
    {
        var sites = new StringBuilder();
        foreach (var s in QuickSites.All)
        {
            sites.Append($$"""
                <button class="card center" data-msg='{"type":"open","url":"{{s.Url}}"}'>{{E(s.Label)}}</button>
                """);
        }

        return Shell("Voyager", $$"""
            <div class="panel">
              <p class="kicker">{{E(engine.Name)}}</p>
              <h1>どこへ行く</h1>
              <p class="lead">アドレスを貼るか、下のサイトを開きます。質問は選んだ AI 本体に送ります。</p>
              <div class="grid">
                <button class="card center" data-msg='{"type":"open","url":"{{engine.Home}}"}'>
                  {{E(engine.Name)}} を開く
                </button>
                <div class="grid three">{{sites}}</div>
              </div>
            </div>
            """, settingsOpen: false, hasEngine: true);
    }

    /// <summary>
    /// 「Voyager について」。版と、いま何の上で動いているかを見せるだけの画面。
    /// 更新の確認はまだ入れていない（見に行く先の Releases が非公開のため）。
    /// </summary>
    /// <summary>
    /// About に出す想像図。publish した assets\voyager.jpg を読んで data URI にする。
    /// ソースに base64 を焼き込まないのは、絵を差し替えるのにビルドし直さなくて済むようにするため。
    /// NavigateToString で出すページなので相対パスの &lt;img&gt; は解決されない。
    /// 読めなければ黙って絵なしにする（About が開かない方が困る）。
    /// </summary>
    private static string AboutImage()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "assets", "voyager.jpg");
            if (!File.Exists(path)) return "";
            var data = Convert.ToBase64String(File.ReadAllBytes(path));
            return $"""
                <figure class="shot">
                  <img src="data:image/jpeg;base64,{data}"
                       alt="星間空間へ出るボイジャー1号の想像図">
                  <figcaption>
                    星間空間に入るボイジャー1号の想像図。実写ではありません。<br>
                    Credit: NASA/JPL-Caltech（PIA17462, 2013）
                  </figcaption>
                </figure>
                """;
        }
        catch (Exception ex)
        {
            Log.Write($"about: image not shown ({ex.GetType().Name})");
            return "";
        }
    }

    public static string About(string? browserVersion)
    {
        var exe = Environment.ProcessPath ?? AppContext.BaseDirectory;
        var built = File.Exists(exe) ? File.GetLastWriteTime(exe).ToString("yyyy-MM-dd HH:mm") : "-";

        return Shell("Voyager について", $$"""
            <div class="panel">
              <p class="kicker">about</p>
              <div class="brand" style="margin:8px 0 4px">
                <h1 style="margin:0">Voyager</h1>
                <span class="sub" style="font-size:22px">V'Ger</span>
              </div>
              <p class="lead">バージョン {{E(App.Version)}}</p>

              {{AboutImage()}}

              <h2>構成</h2>
              <p class="meta">
                WebView2 ランタイム {{E(browserVersion ?? "未検出")}}<br>
                .NET {{E(Environment.Version.ToString())}} / {{E(System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString())}}<br>
                ビルド {{E(built)}}
              </p>

              <h2>場所</h2>
              <p class="meta">
                本体　　{{E(exe)}}<br>
                設定　　{{E(AppSettings.Dir)}}<br>
                データ　{{E(AppSettings.UserDataDir)}}
              </p>

              <div class="grid" style="margin-top:28px">
                <button class="card center" data-msg='{"type":"toggleSettings"}'>設定へ</button>
              </div>

              <p class="meta" style="margin-top:32px">
                銘板の汚れで <strong>VOYAGER</strong> の三文字が隠れ、残った <strong>V GER</strong> を
                自分の名だと思い込んで還ってきた探査機がいました。こちらは開くたびに名乗ります。
              </p>
            </div>
            """, settingsOpen: false, hasEngine: false);
    }

    /// <summary>設定画面。</summary>
    public static string Settings(AppSettings s, Engine? current)
    {
        (string id, string label, string hint)[] homes =
        [
            ("start",  "スタート画面",  "AI と行き先を選ぶ画面"),
            ("ai",     "選んでいる AI", "公式サイトを開く"),
            ("google", "Google",        "https://www.google.co.jp/"),
            ("custom", "指定した URL",  "ホームボタンでこのアドレスを開く"),
        ];

        var choices = new StringBuilder();
        foreach (var (id, label, hint) in homes)
        {
            var on = s.HomeKind == id ? " on" : "";
            choices.Append($$"""
                <div class="choice{{on}}" data-msg='{"type":"setHome","kind":"{{id}}"}'>
                  <span><strong>{{E(label)}}</strong><br><small>{{E(hint)}}</small></span>
                </div>
                """);
        }

        var options = new StringBuilder("<option value=\"\">起動時に選ぶ</option>");
        foreach (var e in Engines.All)
        {
            var sel = current?.Id == e.Id ? " selected" : "";
            options.Append($"<option value=\"{e.Id}\"{sel}>{E(e.Name)}</option>");
        }

        var customRow = s.HomeKind == "custom"
            ? $$"""
                <input class="field" type="url" placeholder="https://www.google.co.jp/"
                       value="{{E(s.HomeUrl)}}" data-change='{"type":"setHomeUrl"}' />
                """
            : "";

        return Shell("設定 - Voyager", $$"""
            <div class="panel">
              <p class="kicker">Settings</p>
              <h1>設定</h1>
              <p class="lead">ホームボタンの行き先と、使う AI を決めます。</p>
              <h2>ホーム</h2>
              <div class="grid">{{choices}}</div>
              <div class="grid">{{customRow}}</div>
              <h2>AI</h2>
              <label class="check">
                <input type="checkbox" {{(s.RememberEngine ? "checked" : "")}} data-change='{"type":"setRemember"}' />
                前回選んだ AI を覚えておく
              </label>
              <div class="grid" style="margin-top:12px">
                <select class="field" data-change='{"type":"setEngine"}'>{{options}}</select>
              </div>
              <h2>ダウンロード</h2>
              <div class="grid">
                <div class="choice">
                  <div style="flex:1">
                    <strong>保存先</strong><br>
                    <small>{{E(string.IsNullOrWhiteSpace(s.DownloadDir) ? "Windows の既定（ダウンロード フォルダー）" : s.DownloadDir)}}</small>
                  </div>
                  <button class="field" style="width:auto;padding:8px 14px;cursor:pointer"
                          data-msg='{"type":"pickDownloadDir"}'>変更</button>
                  {{(string.IsNullOrWhiteSpace(s.DownloadDir) ? "" : """
                  <button class="field" style="width:auto;padding:8px 14px;margin-left:8px;cursor:pointer"
                          data-msg='{"type":"resetDownloadDir"}'>既定に戻す</button>
                  """)}}
                </div>
              </div>
              <label class="check" style="margin-top:12px">
                <input type="checkbox" {{(s.AskDownloadDir ? "checked" : "")}} data-change='{"type":"setAskDownloadDir"}' />
                ダウンロードするたびに保存先を確認する
              </label>
              <p class="meta">
                保存先は次回起動時も引き継ぎます。C ドライブではなく F ドライブへ直接落とす、といった指定もできます。<br>
                指定した場所が見つからないとき（外付けを外した後など）は、黙って Windows の既定に戻ります。
              </p>

              <h2>右クリック</h2>
              <label class="check">
                <input type="checkbox" {{(s.PageContextMenu ? "checked" : "")}} data-change='{"type":"setPageContextMenu"}' />
                ページ側のメニューを優先する
              </label>
              <p class="meta">
                入れておくと、Google スプレッドシートや claude.ai など自前のメニューを持つサイトでは
                そちらが出ます。代わりに AI の入力欄のように、ページがメニューを出さずに
                右クリックだけ握り潰す場所では何も出ません。<br>
                外すと、どこでも Voyager のメニューが出ます。<br>
                どちらの設定でも <strong>Shift + 右クリック</strong> は必ず Voyager のメニューです。
              </p>
              <h2>データ</h2>
              <p class="meta">ログイン状態と Cookie は次の場所に保存されます。<br>{{E(AppSettings.UserDataDir)}}</p>
              <div class="grid" style="margin-top:8px">
                <button class="card center" data-msg='{"type":"about"}'>Voyager について</button>
              </div>
            </div>
            """, settingsOpen: true, hasEngine: current is not null);
    }
}
