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
        <html lang="{{Strings.Current}}"><head><meta charset="utf-8" />
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
    /// <param name="historyNote">閲覧履歴を消したあとの一言（「19:45 に消しました。」など）。無ければ出さない。</param>
    public static string Settings(AppSettings s, Engine? current, int historyKeepDays, string? historyNote = null)
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
                  <option value="auto"{{(s.Language is not ("ja" or "en") ? " selected" : "")}}>{{E(Strings.LanguageAuto)}}</option>
                  <option value="ja"{{(s.Language == "ja" ? " selected" : "")}}>日本語</option>
                  <option value="en"{{(s.Language == "en" ? " selected" : "")}}>English</option>
                </select>
              </div>
              <p class="meta">{{E(Strings.LanguageNote)}}</p>

              <h2>{{E(Strings.HomeHeading)}}</h2>
              <div class="grid">{{choices}}</div>
              <div class="grid">{{customRow}}</div>
              <h2>{{E(Strings.StartupHeading)}}</h2>
              <label class="check">
                <input type="checkbox" {{(s.RestoreTabs ? "checked" : "")}} data-change='{"type":"setRestoreTabs"}' />
                {{E(Strings.RestoreTabs)}}
              </label>
              <p class="meta">{{E(Strings.RestoreTabsNote)}}</p>
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
              <h2>{{E(Strings.HistoryHeading)}}</h2>
              <div class="grid">
                <div class="choice">
                  <div style="flex:1">
                    <strong>{{E(Strings.ClearHistory)}}</strong>
                    {{(historyNote is null ? "" : $"<br><small>{E(historyNote)}</small>")}}
                  </div>
                  <button class="field" style="width:auto;padding:8px 14px;cursor:pointer"
                          data-msg='{"type":"clearHistory"}'>{{E(Strings.ClearHistory)}}</button>
                </div>
              </div>
              <p class="meta">{{E(Strings.HistoryNote(historyKeepDays))}}</p>

              <h2>{{E(Strings.DataHeading)}}</h2>
              <p class="meta">{{E(Strings.DataNote)}}<br>{{E(AppSettings.UserDataDir)}}</p>
              <div class="grid" style="margin-top:8px">
                <button class="card center" data-msg='{"type":"about"}'>{{E(Strings.AboutButton)}}</button>
              </div>
            </div>
            """, settingsOpen: true, hasEngine: current is not null);
    }

    /// <summary>
    /// ブックマークマネージャー。左にフォルダの木、右に中身の一覧。
    /// 中身はここには入れず、開いたあとに bm:init で頼んで bm:tree / bm:items で受け取る
    /// （BookmarkManager の説明を参照）。
    /// </summary>
    public static string Bookmarks()
    {
        var labels = System.Text.Json.JsonSerializer.Serialize(new
        {
            search = Strings.SearchBookmarks,
            newFolder = Strings.NewFolder,
            newFolderName = Strings.NewFolderName,
            open = Strings.Open,
            openNewTab = Strings.OpenInNewTab,
            copyUrl = Strings.BmCopyUrl,
            edit = Strings.BmEdit,
            rename = Strings.BmRename,
            delete = Strings.Delete,
            name = Strings.BookmarkName,
            url = Strings.BmUrl,
            save = Strings.BmSave,
            cancel = Strings.BmCancel,
            empty = Strings.BmEmpty,
            noHits = Strings.BmNoHits,
            confirmFolder = Strings.BmConfirmFolder,
            confirmMany = Strings.BmConfirmMany,
            hits = Strings.BmHitsFormat,
            moveTo = Strings.BmMoveTo,
            moveN = Strings.BmMoveN,
            deleteN = Strings.BmDeleteN,
            move = Strings.BmMove,
            selected = Strings.BmSelected,
            dragN = Strings.BmDragN,
            dups = Strings.BmDuplicates,
            noDups = Strings.BmNoDuplicates,
            dupNote = Strings.BmDupNote,
            selectExtras = Strings.BmSelectExtras,
            showInFolder = Strings.BmShowInFolder,
            dead = Strings.BmDeadLinks,
            checkStart = Strings.BmCheckStart,
            checkStop = Strings.BmCheckStop,
            checkAgain = Strings.BmCheckAgain,
            checkIdle = Strings.BmCheckIdle,
            checkRunning = Strings.BmCheckRunning,
            checkDone = Strings.BmCheckDone,
            checkStopped = Strings.BmCheckStopped,
            checkNetwork = Strings.BmCheckNetwork,
            checkProgress = Strings.BmCheckProgress,
            checkSummary = Strings.BmCheckSummary,
            checkNone = Strings.BmCheckNone,
            groupDead = Strings.BmGroupDead,
            groupUnsure = Strings.BmGroupUnsure,
            selectDead = Strings.BmSelectDead,
            deleted = Strings.BmDeleted,
            undo = Strings.BmUndo,
        });

        return Shell(Strings.BookmarkManager, $$"""
            <style>
              html, body { height:100%; }
              body { overflow:hidden; font-size:14px; }
              .bm { display:flex; flex-direction:column; height:100vh; }
              .bm-top { display:flex; align-items:center; gap:12px; padding:14px 18px; border-bottom:1px solid #2d3228; }
              .bm-top h1 { font-size:18px; margin:0 12px 0 0; white-space:nowrap; }
              .bm-top .field { max-width:520px; padding:8px 12px; border-radius:10px; }
              .bm-top .count { color:#9aa190; font-size:12px; white-space:nowrap; }
              .btn { padding:8px 14px; border-radius:10px; border:1px solid #2d3228; background:#171912;
                     color:inherit; font:inherit; cursor:pointer; white-space:nowrap; }
              .btn:hover { border-color:#7d9a4c; background:#1d2218; }
              .btn.primary { background:#7d9a4c; border-color:#7d9a4c; color:#0c0d0b; }
              .btn.danger { border-color:#9a4c4c; }
              .bm-body { flex:1; display:flex; min-height:0; }
              #tree { width:300px; flex:none; overflow:auto; padding:8px 6px; border-right:1px solid #2d3228; }
              #list { flex:1; overflow:auto; padding:6px 10px 40px; }
              .t-row { display:flex; align-items:center; gap:4px; padding:4px 8px; border-radius:8px; cursor:pointer; white-space:nowrap; }
              .t-row:hover { background:#1d2218; }
              .t-row.on { background:#26301b; }
              .t-tog { width:16px; flex:none; color:#9aa190; text-align:center; }
              .t-name { overflow:hidden; text-overflow:ellipsis; }
              .t-count { color:#9aa190; font-size:12px; margin-left:4px; }
              .row { display:grid; grid-template-columns:22px minmax(0,2fr) minmax(0,3fr); gap:10px; align-items:center;
                     padding:5px 10px; border-radius:8px; cursor:default; }
              .row.with-path { grid-template-columns:22px minmax(0,2fr) minmax(0,2fr) minmax(0,1.4fr); }
              .row:hover { background:#1a1e15; }
              .row.on { background:#26301b; }
              .row img { width:16px; height:16px; }
              .row .ttl, .row .url, .row .path { overflow:hidden; text-overflow:ellipsis; white-space:nowrap; }
              .row .url, .row .path { color:#9aa190; font-size:12px; }
              .note { color:#9aa190; padding:18px 10px; }
              .fold { width:16px; height:12px; border-radius:2px; background:#7d9a4c; opacity:.8; }
              /* 右クリックメニュー（#ctx, z-index 9999）より奥に置く。小窓の入力欄でも貼り付けメニューが見えるように。 */
              #modal { position:fixed; inset:0; background:rgba(0,0,0,.55); display:flex; align-items:center; justify-content:center; z-index:9000; }
              #modal[hidden] { display:none; }
              .dlg { width:min(520px, 90vw); background:#171912; border:1px solid #2d3228; border-radius:14px; padding:20px; }
              .dlg h2 { margin:0 0 14px; color:#f3f4ef; font-size:16px; }
              .dlg label { display:block; color:#9aa190; font-size:12px; margin:10px 0 4px; }
              .dlg .field { padding:9px 12px; border-radius:10px; -webkit-user-select:text; user-select:text; }
              .dlg p { margin:0 0 6px; color:#c6cbb8; }
              .dlg .err { color:#d98c8c; font-size:12px; min-height:16px; margin-top:8px; }
              .dlg .acts { display:flex; justify-content:flex-end; gap:8px; margin-top:14px; }
              .dlg .pick { max-height:50vh; overflow:auto; border:1px solid #2d3228; border-radius:10px; padding:6px; }
              #toast { position:fixed; left:50%; bottom:24px; transform:translateX(-50%); z-index:8000;
                       display:flex; align-items:center; gap:14px; padding:10px 16px; border-radius:12px;
                       background:#171912; border:1px solid #2d3228; box-shadow:0 12px 32px rgba(0,0,0,.55); }
              #toast[hidden] { display:none; }
              /* ドラッグ */
              .row.drop-before { box-shadow:inset 0 2px 0 #9ec25f; }
              .row.drop-after { box-shadow:inset 0 -2px 0 #9ec25f; }
              .row.drop-into, .t-row.drop-into { background:#33421f; box-shadow:inset 0 0 0 1px #7d9a4c; }
              #list.drop-end { box-shadow:inset 0 0 0 1px #7d9a4c; }
              #ghost { position:fixed; z-index:9500; pointer-events:none; max-width:320px; overflow:hidden; text-overflow:ellipsis;
                       white-space:nowrap; padding:4px 10px; border-radius:8px; background:#26301b; border:1px solid #7d9a4c; font-size:12px; }
              body.dragging, body.dragging * { cursor:grabbing !important; }
              body.dragging.no-drop, body.dragging.no-drop * { cursor:no-drop !important; }
              /* 重複などの見方 */
              .t-sep { border-top:1px solid #2d3228; margin:8px 6px; }
              .grp { color:#c6cbb8; font-size:12px; padding:12px 10px 4px; overflow:hidden; text-overflow:ellipsis; white-space:nowrap; border-top:1px solid #23271e; }
              .grp b { color:#9ec25f; font-weight:normal; margin-left:8px; }
              .vbar { display:flex; align-items:center; gap:12px; padding:10px 10px 6px; color:#9aa190; font-size:12px; }
              .vbar span { flex:1; }
              .vbar .btn { flex:none; }
              .row .why { color:#d9a066; margin-right:8px; }
            </style>
            <div class="bm">
              <div class="bm-top">
                <h1>{{E(Strings.BookmarkManager)}}</h1>
                <input id="q" class="field" type="search" autocomplete="off" />
                <button id="nf" class="btn">{{E(Strings.NewFolder)}}</button>
                <span id="count" class="count"></span>
              </div>
              <div class="bm-body">
                <nav id="tree"></nav>
                <section id="list"></section>
              </div>
            </div>
            <div id="modal" hidden></div>
            <div id="toast" hidden></div>
            <script>
              const L = {{labels}};
              let folders = [], current = null, searching = null, items = [], icons = [];
              let view = null, dupCount = 0;   // view: フォルダでも検索でもない見方（'dups' ＝ 重複、'dead' ＝ リンク切れ）
              let deadCount = null, check = null;
              let listAsked = 0;   // フォルダを押した時刻。一覧が描き終わるまでの時間を測る   // リンク切れ: 木に出す数（未確認なら null）と、確認の進み具合
              let picked = new Set(), anchor = -1;   // 選んでいる id と、Shift で範囲を取るときの起点
              const open = new Set(['bar', 'other']);

              const q = document.getElementById('q');
              const tree = document.getElementById('tree');
              const list = document.getElementById('list');
              const modal = document.getElementById('modal');
              const toast = document.getElementById('toast');
              const count = document.getElementById('count');
              q.placeholder = L.search;

              const el = (tag, cls, text) => { const e = document.createElement(tag); if (cls) e.className = cls; if (text != null) e.textContent = text; return e; };
              const fmt = (s, n) => s.replace('{0}', Number(n).toLocaleString());
              const isRoot = (id) => id === 'bar' || id === 'other';
              const parentsOf = (id) => {
                const stack = [];
                for (const f of folders) {
                  stack.length = f.depth; stack.push(f.id);
                  if (f.id === id) return stack.slice(0, -1);
                }
                return [];
              };

              // ---- 木
              function drawTree() {
                tree.replaceChildren();
                let hideBelow = Infinity;
                for (const f of folders) {
                  if (f.depth > hideBelow) continue;
                  hideBelow = Infinity;
                  const row = el('div', 't-row' + (f.id === current && !searching && !view ? ' on' : ''));
                  row.dataset.id = f.id;
                  row.style.paddingLeft = (8 + f.depth * 14) + 'px';
                  const tog = el('span', 't-tog', f.hasSub ? (open.has(f.id) ? '▾' : '▸') : '');
                  tog.addEventListener('click', (e) => { e.stopPropagation(); if (!f.hasSub) return; open.has(f.id) ? open.delete(f.id) : open.add(f.id); drawTree(); });
                  row.append(tog, el('span', 't-name', f.title), el('span', 't-count', '(' + f.count.toLocaleString() + ')'));
                  row.addEventListener('click', () => selectFolder(f.id));
                  if (!isRoot(f.id)) row.addEventListener('mousedown', (e) => {
                    if (e.button === 0 && e.target !== tog) pressRow(e, [{ id: f.id, kind: 'folder', title: f.title, count: f.count }], null);
                  });
                  row.addEventListener('contextmenu', (e) => { e.preventDefault(); e.stopPropagation(); folderMenu(e.clientX, e.clientY, f); });
                  tree.append(row);
                  if (f.hasSub && !open.has(f.id)) hideBelow = f.depth;
                }
                tree.append(el('div', 't-sep'));
                const dup = el('div', 't-row' + (view === 'dups' ? ' on' : ''));
                dup.append(el('span', 't-tog', '⧉'), el('span', 't-name', L.dups), el('span', 't-count', '(' + dupCount.toLocaleString() + ')'));
                dup.addEventListener('click', () => openView('dups'));
                tree.append(dup);
                const dead = el('div', 't-row' + (view === 'dead' ? ' on' : ''));
                dead.append(el('span', 't-tog', '⚠'), el('span', 't-name', L.dead));
                if (deadCount != null) dead.append(el('span', 't-count', '(' + deadCount.toLocaleString() + ')'));
                dead.addEventListener('click', () => openView('dead'));
                tree.append(dead);
              }

              // リンク切れの上の帯。進み具合と、始める・止めるボタン。
              const fmt2 = (s, a, b) => s.replace('{0}', Number(a).toLocaleString()).replace('{1}', Number(b).toLocaleString());
              function checkBar() {
                const bar = el('div', 'vbar'); bar.id = 'checkbar';
                const c = check || { state: 'idle', scope: '' };
                const btn = (label, fn) => { const b = el('button', 'btn', label); b.addEventListener('click', fn); bar.append(b); };
                let text;
                if (c.state === 'idle') text = L.checkIdle.replace('{0}', c.scope);
                else {
                  const head = { running: L.checkRunning, done: L.checkDone, stopped: L.checkStopped, network: L.checkNetwork }[c.state] || '';
                  text = head.replace('{0}', c.scope) + '　' + fmt2(L.checkProgress, c.done, c.total) + '　' + fmt2(L.checkSummary, c.dead, c.unsure);
                }
                bar.append(el('span', null, text));
                if (c.state === 'running') { btn(L.checkStop, () => send({ type: 'bm:checkStop' })); return bar; }
                if (items.some(i => i.group === 0))
                  btn(L.selectDead, () => { picked = new Set(items.filter(i => i.group === 0).map(i => i.id)); anchor = -1; mark(); });
                const label = c.state === 'idle' ? L.checkStart
                  : (c.next && c.next !== c.scope ? L.checkStart + '：' + c.next : L.checkAgain);
                btn(label, () => send({ type: 'bm:checkStart' }));
                return bar;
              }

              function openView(v) {
                view = v; searching = null; q.value = ''; picked.clear(); anchor = -1;
                drawTree();
                send({ type: 'bm:view', view: v });
              }

              function selectFolder(id) {
                current = id; searching = null; view = null; q.value = ''; picked.clear(); anchor = -1;
                listAsked = performance.now();
                for (const p of parentsOf(id)) open.add(p);
                drawTree();
                send({ type: 'bm:list', folder: id });
              }

              // ---- 一覧
              function drawList() {
                list.replaceChildren();
                const flat = searching || view;
                if (view === 'dead') {
                  list.append(checkBar());
                  if (!items.length) {
                    if (check && (check.state === 'done' || check.state === 'stopped')) list.append(el('div', 'note', L.checkNone));
                    showCount(); return;
                  }
                }
                if (!items.length) { list.append(el('div', 'note', view === 'dups' ? L.noDups : searching ? L.noHits : L.empty)); showCount(); return; }
                if (searching) list.append(el('div', 'note', fmt(L.hits, items.length)));
                if (view === 'dups') {
                  const bar = el('div', 'vbar');
                  const pickExtras = el('button', 'btn', L.selectExtras);
                  pickExtras.addEventListener('click', () => {
                    picked = new Set(items.filter((it, i) => i > 0 && items[i - 1].group === it.group).map(it => it.id));
                    anchor = -1; mark();
                  });
                  bar.append(el('span', null, L.dupNote), pickExtras);
                  list.append(bar);
                }
                items.forEach((it, i) => {
                  if ((view === 'dups' || view === 'dead') && (i === 0 || items[i - 1].group !== it.group)) {
                    let n = 0; for (let k = i; k < items.length && items[k].group === it.group; k++) n++;
                    const head = el('div', 'grp', view === 'dead' ? (it.group === 0 ? L.groupDead : L.groupUnsure) : (it.url || ''));
                    head.append(el('b', null, fmt(L.dragN, n)));
                    list.append(head);
                  }
                  const row = el('div', 'row' + (flat ? ' with-path' : ''));
                  row.dataset.id = it.id;
                  let ic;
                  if (it.kind === 'folder') ic = el('div', 'fold');
                  else if (it.icon >= 0) { ic = el('img'); ic.src = icons[it.icon]; ic.alt = ''; }
                  else ic = el('span');
                  row.append(ic, el('div', 'ttl', it.kind === 'folder' ? it.title + '  (' + it.count + ')' : it.title));
                  const urlCell = el('div', 'url', it.kind === 'folder' ? '' : (it.url || ''));
                  if (it.why) urlCell.prepend(el('span', 'why', it.why));
                  row.append(urlCell);
                  if (flat) row.append(el('div', 'path', it.path || ''));
                  if (it.url) row.title = it.title + '\n' + it.url;
                  row.addEventListener('mousedown', (e) => {
                    if (e.button !== 0) return;
                    if (e.ctrlKey || e.shiftKey) { choose(i, e.ctrlKey, e.shiftKey); if (picked.has(it.id)) pressRow(e, pickedItems(), null); return; }
                    // 選んでいる束の上なら束ごとつかむ。動かさずに離したら、その 1 件だけの選択にする。
                    if (picked.has(it.id)) { pressRow(e, pickedItems(), i); return; }
                    choose(i, false, false);
                    pressRow(e, [it], null);
                  });
                  row.addEventListener('dblclick', () => activate(it, true));
                  row.addEventListener('contextmenu', (e) => {
                    e.preventDefault(); e.stopPropagation();
                    if (!picked.has(it.id)) choose(i, false, false);
                    itemMenu(e.clientX, e.clientY, it);
                  });
                  list.append(row);
                });
                mark();
              }

              // クリックでの選び方。普通は 1 件、Ctrl で足し引き、Shift で起点からの範囲。
              function choose(i, ctrl, shift) {
                const id = items[i].id;
                if (shift && anchor >= 0) {
                  if (!ctrl) picked.clear();
                  const [a, b] = anchor < i ? [anchor, i] : [i, anchor];
                  for (let k = a; k <= b; k++) picked.add(items[k].id);
                } else if (ctrl) {
                  picked.has(id) ? picked.delete(id) : picked.add(id);
                  anchor = i;
                } else {
                  picked = new Set([id]);
                  anchor = i;
                }
                mark();
              }
              function mark() {
                for (const r of list.querySelectorAll('.row')) r.classList.toggle('on', picked.has(r.dataset.id));
                showCount();
              }
              function showCount() { count.textContent = picked.size > 1 ? fmt(L.selected, picked.size) : ''; }
              const pickedItems = () => items.filter(i => picked.has(i.id));

              function activate(it, newTab) {
                if (it.kind === 'folder') { selectFolder(it.id); return; }
                send({ type: 'bm:open', id: it.id, newTab });
              }

              // ---- メニュー（Shell の #ctx と同じ見た目・同じ閉じ方）
              function showMenu(x, y, rows) {
                closeMenu();
                const box = el('div'); box.id = 'ctx';
                for (const r of rows) {
                  if (!r) { box.append(el('div', 'sep')); continue; }
                  const item = el('div', 'item', r[0]);
                  item.addEventListener('mousedown', (e) => { e.preventDefault(); closeMenu(); r[1](); });
                  box.append(item);
                }
                document.body.append(box);
                box.style.left = Math.min(x, innerWidth - box.offsetWidth - 8) + 'px';
                box.style.top = Math.min(y, innerHeight - box.offsetHeight - 8) + 'px';
                menuEl = box;
              }

              function itemMenu(x, y, it) {
                const sel = pickedItems();
                if (sel.length > 1) {
                  showMenu(x, y, [[fmt(L.moveN, sel.length), () => moveDialog(sel)], null, [fmt(L.deleteN, sel.length), () => remove(sel)]]);
                } else if (it.kind === 'folder') {
                  showMenu(x, y, [[L.open, () => selectFolder(it.id)], null,
                    [L.rename, () => editDialog(it)], [L.moveTo, () => moveDialog([it])], [L.delete, () => remove([it])]]);
                } else {
                  const rows = [[L.open, () => activate(it, false)], [L.openNewTab, () => activate(it, true)], null,
                    [L.copyUrl, () => send({ type: 'clip', text: it.url || '' })]];
                  if ((searching || view) && it.parent) rows.push([L.showInFolder, () => selectFolder(it.parent)]);
                  rows.push(null, [L.edit, () => editDialog(it)], [L.moveTo, () => moveDialog([it])], [L.delete, () => remove([it])]);
                  showMenu(x, y, rows);
                }
              }
              function folderMenu(x, y, f) {
                const rows = [[L.newFolder, () => newFolderDialog(f.id)]];
                if (!isRoot(f.id)) {
                  const it = { id: f.id, kind: 'folder', title: f.title, count: f.count };
                  rows.push(null, [L.rename, () => editDialog(it)], [L.moveTo, () => moveDialog([it])], [L.delete, () => remove([it])]);
                }
                showMenu(x, y, rows);
              }

              // ---- 小窓
              function dialog(title, fields, okLabel, onOk, danger, message, extra) {
                modal.replaceChildren();
                const box = el('div', 'dlg');
                box.append(el('h2', null, title));
                if (message) box.append(el('p', null, message));
                const inputs = {};
                for (const f of fields) {
                  box.append(el('label', null, f.label));
                  const inp = el('input', 'field'); inp.value = f.value || ''; inp.spellcheck = false;
                  inputs[f.key] = inp; box.append(inp);
                }
                if (extra) box.append(extra);
                const err = el('div', 'err'); box.append(err);
                const acts = el('div', 'acts');
                const cancel = el('button', 'btn', L.cancel);
                const ok = el('button', 'btn ' + (danger ? 'danger' : 'primary'), okLabel);
                acts.append(cancel, ok); box.append(acts);
                modal.append(box); modal.hidden = false;
                const close = () => { modal.hidden = true; modal.replaceChildren(); };
                const submit = () => {
                  const v = {}; for (const k in inputs) v[k] = inputs[k].value.trim();
                  const bad = onOk(v); if (bad) { err.textContent = bad; return; }
                  close();
                };
                cancel.addEventListener('click', close);
                ok.addEventListener('click', submit);
                box.addEventListener('keydown', (e) => {
                  if (e.key === 'Enter') { e.preventDefault(); submit(); }
                  if (e.key === 'Escape') { e.preventDefault(); close(); }
                });
                const first = Object.values(inputs)[0];
                if (first) { first.focus(); first.select(); } else ok.focus();
              }

              function editDialog(it) {
                const fields = [{ key: 'title', label: L.name, value: it.title }];
                if (it.kind !== 'folder') fields.push({ key: 'url', label: L.url, value: it.url });
                dialog(it.kind === 'folder' ? L.rename : L.edit, fields, L.save, (v) => {
                  if (!v.title) return L.name;
                  send({ type: 'bm:edit', id: it.id, title: v.title, url: v.url });
                });
              }
              function newFolderDialog(parent) {
                dialog(L.newFolder, [{ key: 'title', label: L.name, value: L.newFolderName }], L.save, (v) => {
                  if (!v.title) return L.name;
                  send({ type: 'bm:newFolder', parent, title: v.title });
                });
              }

              // 行き先を選ぶ小窓。動かすフォルダ自身とその下は出さない（自分の中へは入れられない）。
              function moveDialog(sel) {
                const moving = new Set(sel.filter(i => i.kind === 'folder').map(i => i.id));
                const pick = el('div', 'pick');
                let target = null, skipBelow = Infinity;
                for (const f of folders) {
                  if (f.depth > skipBelow) continue;
                  skipBelow = Infinity;
                  if (moving.has(f.id)) { skipBelow = f.depth; continue; }
                  const row = el('div', 't-row');
                  row.style.paddingLeft = (8 + f.depth * 14) + 'px';
                  row.append(el('span', 't-name', f.title));
                  row.addEventListener('click', () => { target = f.id; for (const r of pick.children) r.classList.remove('on'); row.classList.add('on'); });
                  row.addEventListener('dblclick', () => { target = f.id; modal.querySelector('.btn.primary').click(); });
                  pick.append(row);
                }
                const title = sel.length > 1 ? fmt(L.moveN, sel.length) : L.moveTo;
                dialog(title, [], L.move, () => {
                  if (!target) return L.moveTo;
                  send({ type: 'bm:move', ids: sel.map(i => i.id), to: target });
                }, false, null, pick);
              }

              function remove(sel) {
                sel = sel.filter(i => !isRoot(i.id));
                if (!sel.length) return;
                const heavy = sel.filter(i => i.kind === 'folder' && i.count > 0);
                const go = () => send({ type: 'bm:delete', ids: sel.map(i => i.id) });
                if (!heavy.length) { go(); return; }
                const msg = sel.length === 1 ? L.confirmFolder.replace('{0}', sel[0].title) : fmt(L.confirmMany, sel.length);
                dialog(L.delete, [], L.delete, () => { go(); }, true, msg);
              }

              // ---- 消したあとの「元に戻す」
              let toastTimer = 0;
              function showUndo(token, n) {
                clearTimeout(toastTimer);
                toast.replaceChildren(el('span', null, fmt(L.deleted, n)));
                const b = el('button', 'btn', L.undo);
                b.addEventListener('click', () => { send({ type: 'bm:undo', token }); toast.hidden = true; });
                toast.append(b);
                toast.hidden = false;
                toastTimer = setTimeout(() => { toast.hidden = true; }, 10000);
              }

              // ---- ドラッグ（移動と並べ替え）
              // HTML5 のドラッグ＆ドロップは使わず、マウスの動きで自前に行う。
              // アプリの窓が外からのドロップを受ける仕組みと混ざらないようにするため。
              let press = null;   // 押した所。少し動いたらドラッグを始める
              let drag = null;    // ドラッグ中の状態
              let swallowClick = false;

              function pressRow(e, sel, single) { press = { x: e.clientX, y: e.clientY, sel, single }; }

              function startDrag() {
                const sel = press.sel, ids = new Set(sel.map(i => i.id));
                // 動かすフォルダ自身とその下には落とせない
                const banned = new Set();
                let below = Infinity;
                for (const f of folders) {
                  if (f.depth > below) { banned.add(f.id); continue; }
                  below = Infinity;
                  if (ids.has(f.id)) { banned.add(f.id); below = f.depth; }
                }
                const ghost = el('div', null, sel.length > 1 ? fmt(L.dragN, sel.length) : sel[0].title);
                ghost.id = 'ghost';
                document.body.append(ghost);
                closeMenu();
                drag = { sel, ids, banned, ghost, target: null, hoverId: null, hoverTimer: 0, scrollEl: null, scrollDy: 0, raf: 0, x: 0, y: 0 };
                document.body.classList.add('dragging');
                press = null;
              }

              // 指している所から、落とし先を決める。落とせない所なら null。
              function findTarget(x, y) {
                const hit = document.elementFromPoint(x, y);
                if (!hit) return null;
                const t = hit.closest('.t-row');
                if (t && tree.contains(t)) {
                  if (drag.banned.has(t.dataset.id)) return null;
                  return { to: t.dataset.id, before: null, el: t, cls: 'drop-into', hover: t.dataset.id };
                }
                // 一覧での並べ替えはフォルダを開いているときだけ。検索結果の中では木へ落とすだけ。
                if (searching || view || !current || drag.banned.has(current) || !list.contains(hit)) return null;
                const r = hit.closest('.row');
                if (!r) {
                  if (hit !== list && !hit.classList.contains('note')) return null;
                  const rows = list.querySelectorAll('.row'), last = rows[rows.length - 1];
                  return { to: current, before: null, el: last || list, cls: last ? 'drop-after' : 'drop-end' };
                }
                const i = items.findIndex(x => x.id === r.dataset.id);
                if (i < 0 || drag.ids.has(items[i].id)) return null;
                const it = items[i], b = r.getBoundingClientRect(), f = (y - b.top) / b.height;
                if (it.kind === 'folder' && f > 0.25 && f < 0.75) return { to: it.id, before: null, el: r, cls: 'drop-into' };
                if (f < 0.5) return { to: current, before: it.id, el: r, cls: 'drop-before' };
                let next = null;   // 後ろに入れる ＝ 次の（動かさない）ものの前に入れる
                for (let k = i + 1; k < items.length; k++) if (!drag.ids.has(items[k].id)) { next = items[k].id; break; }
                return { to: current, before: next, el: r, cls: 'drop-after' };
              }

              function setTarget(t) {
                const old = drag.target;
                if (!(old && t && old.el === t.el && old.cls === t.cls)) {
                  if (old) old.el.classList.remove(old.cls);
                  if (t) t.el.classList.add(t.cls);
                }
                drag.target = t;
                document.body.classList.toggle('no-drop', !t);
                // 閉じている木のフォルダの上で少し待つと開く
                const hover = (t && t.hover) || null;
                if (hover !== drag.hoverId) {
                  clearTimeout(drag.hoverTimer);
                  drag.hoverId = hover;
                  const f = hover && folders.find(x => x.id === hover);
                  if (f && f.hasSub && !open.has(f.id))
                    drag.hoverTimer = setTimeout(() => { if (!drag) return; open.add(f.id); drawTree(); setTarget(findTarget(drag.x, drag.y)); }, 700);
                }
              }

              function moveDrag(x, y) {
                drag.x = x; drag.y = y;
                drag.ghost.style.left = (x + 14) + 'px';
                drag.ghost.style.top = (y + 12) + 'px';
                setTarget(findTarget(x, y));
                // 上下の端に来たら転がす
                drag.scrollEl = null;
                for (const box of [list, tree]) {
                  const r = box.getBoundingClientRect();
                  if (x < r.left || x > r.right) continue;
                  if (y < r.top + 28) { drag.scrollEl = box; drag.scrollDy = -Math.min(20, Math.ceil((r.top + 28 - y) / 3)); }
                  else if (y > r.bottom - 28) { drag.scrollEl = box; drag.scrollDy = Math.min(20, Math.ceil((y - r.bottom + 28) / 3)); }
                }
                if (drag.scrollEl && !drag.raf) drag.raf = requestAnimationFrame(autoScroll);
              }
              function autoScroll() {
                if (!drag) return;
                drag.raf = 0;
                if (!drag.scrollEl) return;
                drag.scrollEl.scrollTop += drag.scrollDy;
                setTarget(findTarget(drag.x, drag.y));
                drag.raf = requestAnimationFrame(autoScroll);
              }

              function finishDrag(drop) {
                const d = drag; drag = null;
                clearTimeout(d.hoverTimer); cancelAnimationFrame(d.raf);
                d.ghost.remove();
                if (d.target) d.target.el.classList.remove(d.target.cls);
                document.body.classList.remove('dragging', 'no-drop');
                if (drop && d.target) send({ type: 'bm:drop', ids: d.sel.map(i => i.id), to: d.target.to, before: d.target.before });
              }

              window.addEventListener('mousemove', (e) => {
                if (press && !drag) {
                  if (!(e.buttons & 1)) { press = null; return; }
                  if (Math.abs(e.clientX - press.x) + Math.abs(e.clientY - press.y) < 6) return;
                  startDrag();
                }
                if (drag) { e.preventDefault(); moveDrag(e.clientX, e.clientY); }
              });
              window.addEventListener('mouseup', () => {
                if (drag) { finishDrag(true); swallowClick = true; setTimeout(() => { swallowClick = false; }, 0); }
                else if (press && press.single != null) choose(press.single, false, false);
                press = null;
              });
              // 落とした直後のクリック（木の行を開く、など）は無かったことにする
              document.addEventListener('click', (e) => { if (swallowClick) { e.stopPropagation(); e.preventDefault(); } }, true);
              window.addEventListener('blur', () => { press = null; if (drag) finishDrag(false); });

              // ---- 操作
              document.getElementById('nf').addEventListener('click', () => newFolderDialog(current || 'bar'));
              list.addEventListener('contextmenu', (e) => { e.preventDefault(); e.stopPropagation(); if (!searching && !view) showMenu(e.clientX, e.clientY, [[L.newFolder, () => newFolderDialog(current)]]); });
              list.addEventListener('mousedown', (e) => { if (e.button === 0 && e.target === list) { picked.clear(); anchor = -1; mark(); } });
              let timer = 0;
              q.addEventListener('input', () => {
                clearTimeout(timer);
                timer = setTimeout(() => {
                  const text = q.value.trim();
                  if (!text) { selectFolder(current || 'bar'); return; }
                  searching = text; view = null; picked.clear(); anchor = -1; drawTree();
                  send({ type: 'bm:search', q: text });
                }, 200);
              });
              document.addEventListener('keydown', (e) => {
                if (drag) { if (e.key === 'Escape') { e.preventDefault(); finishDrag(false); } return; }
                if (!modal.hidden || e.target === q) return;
                if (e.key === 'a' && e.ctrlKey) { e.preventDefault(); picked = new Set(items.map(i => i.id)); mark(); return; }
                const sel = pickedItems(); if (!sel.length) return;
                if (e.key === 'Delete') remove(sel);
                else if (sel.length === 1 && e.key === 'Enter') activate(sel[0], true);
                else if (sel.length === 1 && e.key === 'F2') editDialog(sel[0]);
              });

              // ---- アプリから
              window.chrome?.webview?.addEventListener('message', (e) => {
                const m = e.data;
                if (m.type === 'bm:tree') {
                  folders = m.folders; dupCount = m.dups || 0; deadCount = m.dead ?? null;
                  if (!current) { current = m.selected; for (const p of parentsOf(current)) open.add(p); }
                  if (!folders.some(f => f.id === current)) current = 'bar';
                  drawTree();
                } else if (m.type === 'bm:items') {
                  if ((m.view || null) !== view || (m.search || null) !== searching ||
                      (!m.search && !m.view && m.folder !== current)) return; // 古い返事
                  items = m.items; icons = m.icons;
                  if (m.check) check = m.check;
                  const alive = new Set(items.map(i => i.id));
                  picked = new Set([...picked].filter(id => alive.has(id)));
                  anchor = -1;
                  const t0 = performance.now();
                  drawList();
                  if (listAsked && !m.search && !m.view) {
                    const asked = listAsked, draw = Math.round(performance.now() - t0);
                    listAsked = 0;
                    // 描いた結果が画面に出たあと（次の描画の後）で測る
                    requestAnimationFrame(() => setTimeout(() =>
                      send({ type: 'bm:perf', rows: items.length, ms: Math.round(performance.now() - asked), draw }), 0));
                  }
                } else if (m.type === 'bm:check') {
                  check = m.check;
                  const old = document.getElementById('checkbar');
                  if (old && view === 'dead') old.replaceWith(checkBar());
                } else if (m.type === 'bm:deleted') {
                  showUndo(m.token, m.count);
                } else if (m.type === 'bm:error') {
                  dialog(m.title || '', [], 'OK', () => {}, false, m.text);
                }
              });
              window.addEventListener('DOMContentLoaded', () => send({ type: 'bm:init' }));
            </script>
            """);
    }
}
