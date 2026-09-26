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

    /// <summary>ページ内メニューは JavaScript の '…' の中に入るので、引用符だけ潰しておく。</summary>
    private static string Js(string s) => s.Replace("\\", "\\\\").Replace("'", "\\'");

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

            if (selection) items.push(['{{Js(Strings.Copy)}}', () => { send({ type: 'clip', text: selection }); }, true]);
            if (editable) {
              items.push(['{{Js(Strings.Cut)}}', () => { send({ type: 'clip', text: selection }); document.execCommand('delete'); }, !!selection]);
              items.push(['{{Js(Strings.Paste)}}', () => send({ type: 'paste' }), true]);
            }
            if (items.length) items.push(null);

            items.push(['{{Js(Strings.NewTab)}}', () => send({ type: 'newTab' }), true]);
            items.push(['{{Js(Strings.Home)}}', () => send({ type: 'home' }), true]);
            items.push([CTX.settings ? '{{Js(Strings.CloseSettings)}}' : '{{Js(Strings.Settings)}}', () => send({ type: 'toggleSettings' }), true]);
            items.push(['{{Js(Strings.PickAiMenu)}}', () => send({ type: 'picker' }), CTX.hasEngine]);

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
              <p class="lead">{{E(Strings.PickerLead)}}</p>
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
              <h1>{{E(Strings.StartTitle)}}</h1>
              <p class="lead">{{E(Strings.StartLead)}}</p>
              <div class="grid">
                <button class="card center" data-msg='{"type":"open","url":"{{engine.Home}}"}'>
                  {{E(Strings.OpenEngine(engine.Name))}}
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
                       alt="{E(Strings.AboutImageAlt)}">
                  <figcaption>{Strings.AboutImageCaption}</figcaption>
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

        return Shell(Strings.AboutTitle, $$"""
            <div class="panel">
              <p class="kicker">about</p>
              <div class="brand" style="margin:8px 0 4px">
                <h1 style="margin:0">Voyager</h1>
                <span class="sub" style="font-size:22px">V'Ger</span>
              </div>
              <p class="lead">{{E(Strings.Version(App.Version))}}</p>

              {{AboutImage()}}

              <h2>{{E(Strings.Build)}}</h2>
              <p class="meta">
                {{E(Strings.Runtime(browserVersion ?? Strings.NotDetected))}}<br>
                .NET {{E(Environment.Version.ToString())}} / {{E(System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString())}}<br>
                {{E(Strings.BuiltAt(built))}}
              </p>

              <h2>{{E(Strings.Places)}}</h2>
              <p class="meta">
                {{E(Strings.PlaceApp)}}: {{E(exe)}}<br>
                {{E(Strings.PlaceSettings)}}: {{E(AppSettings.Dir)}}<br>
                {{E(Strings.PlaceData)}}: {{E(AppSettings.UserDataDir)}}
              </p>

              <div class="grid" style="margin-top:28px">
                <button class="card center" data-msg='{"type":"toggleSettings"}'>{{E(Strings.ToSettings)}}</button>
              </div>

              <p class="meta" style="margin-top:32px">
                {{Strings.Nameplate}}
              </p>
            </div>
            """, settingsOpen: false, hasEngine: false);
    }

    /// <summary>設定画面。</summary>
    public static string Settings(AppSettings s, Engine? current)
    {
        (string id, string label, string hint)[] homes =
        [
            ("start",  Strings.HomeStart,  Strings.HomeStartHint),
            ("ai",     Strings.HomeAi,     Strings.HomeAiHint),
            ("google", "Google",           "https://www.google.co.jp/"),
            ("custom", Strings.HomeCustom, Strings.HomeCustomHint),
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

        var options = new StringBuilder($"<option value=\"\">{E(Strings.ChooseAtStartup)}</option>");
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

        return Shell($"{Strings.SettingsTitle} - Voyager", $$"""
            <div class="panel">
              <p class="kicker">Settings</p>
              <h1>{{E(Strings.SettingsTitle)}}</h1>
              <p class="lead">{{E(Strings.SettingsLead)}}</p>

              <h2>{{E(Strings.LanguageHeading)}}</h2>
              <div class="grid">
                <select class="field" data-change='{"type":"setLanguage"}'>
                  <option value="ja"{{(Strings.Current == "ja" ? " selected" : "")}}>日本語</option>
                  <option value="en"{{(Strings.Current == "en" ? " selected" : "")}}>English</option>
                </select>
              </div>
              <p class="meta">{{E(Strings.LanguageNote)}}</p>

              <h2>{{E(Strings.HomeHeading)}}</h2>
              <div class="grid">{{choices}}</div>
              <div class="grid">{{customRow}}</div>
              <h2>{{E(Strings.AiHeading)}}</h2>
              <label class="check">
                <input type="checkbox" {{(s.RememberEngine ? "checked" : "")}} data-change='{"type":"setRemember"}' />
                {{E(Strings.RememberEngine)}}
              </label>
              <div class="grid" style="margin-top:12px">
                <select class="field" data-change='{"type":"setEngine"}'>{{options}}</select>
              </div>
              <h2>{{E(Strings.DownloadsHeading)}}</h2>
              <div class="grid">
                <div class="choice">
                  <div style="flex:1">
                    <strong>{{E(Strings.SaveLocation)}}</strong><br>
                    <small>{{E(string.IsNullOrWhiteSpace(s.DownloadDir) ? Strings.WindowsDefaultFolder : s.DownloadDir)}}</small>
                  </div>
                  <button class="field" style="width:auto;padding:8px 14px;cursor:pointer"
                          data-msg='{"type":"pickDownloadDir"}'>{{E(Strings.Change)}}</button>
                  {{(string.IsNullOrWhiteSpace(s.DownloadDir) ? "" : """
                  <button class="field" style="width:auto;padding:8px 14px;margin-left:8px;cursor:pointer"
                          data-msg='{"type":"resetDownloadDir"}'>{{E(Strings.ResetToDefault)}}</button>
                  """)}}
                </div>
              </div>
              <label class="check" style="margin-top:12px">
                <input type="checkbox" {{(s.AskDownloadDir ? "checked" : "")}} data-change='{"type":"setAskDownloadDir"}' />
                {{E(Strings.AskEveryDownload)}}
              </label>
              <p class="meta">{{Strings.DownloadsNote}}</p>

              <h2>{{E(Strings.ContextMenuHeading)}}</h2>
              <label class="check">
                <input type="checkbox" {{(s.PageContextMenu ? "checked" : "")}} data-change='{"type":"setPageContextMenu"}' />
                {{E(Strings.PreferPageMenu)}}
              </label>
              <p class="meta">{{Strings.ContextMenuNote}}</p>
              <h2>{{E(Strings.DataHeading)}}</h2>
              <p class="meta">{{E(Strings.DataNote)}}<br>{{E(AppSettings.UserDataDir)}}</p>
              <div class="grid" style="margin-top:8px">
                <button class="card center" data-msg='{"type":"about"}'>{{E(Strings.AboutButton)}}</button>
              </div>
            </div>
            """, settingsOpen: true, hasEngine: current is not null);
    }
}
