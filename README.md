# Voyager

A small AI-first web browser for Windows. It does not bundle Chromium — it runs on the
**WebView2 runtime that Windows already has**, so the installer stays modest and the engine
gets updated by Windows Update rather than by me.

It replaces two earlier attempts at the same idea, both called 栞 (Shiori): a Chromium/Electron
build (~200 MB) and a thin WebView2 build (v0.2.0).

- Name: **Voyager** — Explorer → Navigator → Voyager
- Codename: V'Ger

日本語版の README は [README.ja.md](README.ja.md) にあります。

## What it does

The address bar takes either a URL or a question. A URL opens as you would expect; anything
else is sent to whichever AI you have selected (Gemini, ChatGPT, Claude, Meta AI, DeepSeek,
Grok, Perplexity). A handful of words — `google`, `ぐぐる`, `ようつべ` and friends — open the
site instead of searching for it.

Everything else is an ordinary browser: tabs that keep their state, a bookmarks bar and
sidebar, downloads, and a context menu of its own.

## Why it exists

The Chromium build had two faults that made it unpleasant to use every day, and both are
fixed here.

| | Shiori (Chromium) | Shiori (WebView2) 0.2.0 | Voyager |
|---|---|---|---|
| Engine | Electron 37.10.3, bundled | WebView2, shared | WebView2, shared |
| Download size | ~200 MB | ~1 MB | ~40 MB (self-contained) |
| Switching AI | **fell back to the previous one** | not implemented | fixed |
| Switching tabs | **reloaded every time, losing logins** | — | view is kept alive |
| Installer | 7-Zip SFX + batch file | — | **MSI (Windows Installer)** |
| Uninstall | did not appear in the app list | — | Settings → Apps |

### About the AI-switching bug

`paint()` in the Chromium build's `renderer/app.js` read:

```js
else if (!tab.url) renderStart();
else renderPage(tab);      // ← reached whenever the tab still held its old URL
```

Picking a different AI never cleared `tab.url`, so the old page was re-rendered. In Voyager,
`pickEngine` in `MainForm.OnUiMessage()` always goes through `ShowStart()`.

## Features

- **Tabs** that keep their WebView2 alive, so logins and scroll position survive a switch
- **Bookmarks** — a bar and a sidebar, with import and export of Netscape bookmark files
  (the format Chrome and Firefox both use). Favicons are fetched and cached
- **Downloads** — a default folder you can set, or a prompt for every download
- **Context menus** drawn by the app, in the app's own colors, including
  save / copy / copy-address for images
- **Per-monitor DPI**. Fonts, icons, row heights and menus all follow the display, and
  keep their size across 96 / 144 / 192 dpi and when a window moves between monitors
- **An About page**, because the point was to see the name V'Ger on screen

## Layout

```
Voyager.csproj          .NET 10 / WinForms / WebView2
app.manifest            asInvoker, PerMonitorV2
assets/Voyager.ico      icon (16–256 px)
assets/voyager.jpg      artist's concept shown on the About page
src/
  Program.cs            startup, WebView2 runtime check
  MainForm.cs           tabs, toolbar, context menus, shortcuts, downloads
  BrowserTab.cs         per-tab state (the WebView2 is never disposed)
  TabItem.cs            how a tab is drawn
  Pages.cs              internal pages (AI picker / start / settings / about) as HTML
  Engines.cs            the AI list and their query URLs
  UrlHelper.cs          URL detection (ported from the Chromium build's parseUrl)
  AppSettings.cs        settings persistence
  Bookmarks.cs          the bookmark store
  BookmarkBar.cs        the bar under the toolbar
  BookmarkSidebar.cs    the tree on the left
  NetscapeBookmarks.cs  import and export of bookmark HTML
  Favicons.cs           favicon decoding and per-DPI cache
  Log.cs                debug log, with URLs redacted
  Theme.cs / DarkMenu.cs  colors and menu styling
installer/Voyager.ui.wxs  MSI definition: files, the wizard, and its text in both languages
installer/ui/           the wizard's side and banner bitmaps
build.cmd               build, package and install
nextbuild.ps1           picks the next version number
```

## Building

Windows, with the .NET 10 SDK and WiX:

```
build.cmd
```

It publishes self-contained win-x64, stages the output, builds two MSIs from the same source
and then runs the English one. Progress goes to `make-log.txt`. Anything you pass to
`build.cmd` is handed on to the installer, so `build.cmd UILANG=1033` shows the wizard in
English even on a Japanese Windows.

The version number is chosen by `nextbuild.ps1`, which takes the highest number it can find — in the registry, in the installed executable, in
`buildno.txt` and among the artifacts in the folder — and adds one, so cleaning the folder
cannot make the version go backwards.

## Installing

There are two installers. They are the same program; what differs is the language of the
text Windows Installer itself shows, such as the progress messages.

| | |
|---|---|
| `Voyager-x.y.z-x64.msi` | English |
| `Voyager-x.y.z-x64-ja.msi` | Japanese / 日本語 |

The wizard's own text follows the Windows display language in either one. To force it, pass
`UILANG`: `msiexec /i Voyager-x.y.z-x64.msi UILANG=1033` for English, `UILANG=1041` for
Japanese.

Both install per user, so neither asks for elevation.

- Installed to `%LOCALAPPDATA%\Programs\Voyager`
- Shortcuts on the desktop and in the Start menu
- Uninstall from Settings → Apps

WebView2 must be present. On Windows 11 and up-to-date Windows 10 it already is; if it is
missing, Voyager says so at startup and offers the download link.

## Where your data lives

| | |
|---|---|
| Settings | `%APPDATA%\Voyager\settings.json` |
| Bookmarks | `%APPDATA%\Voyager\bookmarks.json` |
| Logins and cookies | `%LOCALAPPDATA%\Voyager\WebView2` |

Uninstalling leaves all three in place. Delete the folders yourself if you want them gone.

## Keys

| | |
|---|---|
| Ctrl+T | new tab |
| Ctrl+W | close tab |
| Ctrl+L | focus the address bar |
| Ctrl+R / F5 | reload |
| Alt+← / → | back / forward |
| Ctrl+Tab | next tab |

Clicking an unfocused address bar selects the whole URL; clicking again puts the caret where
you clicked.

## How it is kept safe

- The internal pages (AI picker, start, settings, about) are shown in a **separate WebView2**,
  and only that one has `postMessage` enabled. The WebView2 that shows websites runs with
  `IsWebMessageEnabled = false`, so a website cannot drive the application.
- Those pages carry a Content-Security-Policy of `default-src 'none'` — the only image source
  allowed is `data:`, so an internal page cannot reach the network at all.
- `NavigationStarting` blocks anything that is not `http` or `https` (`file:` and the rest).
- Requests for a new window (`target="_blank"`) open as a tab inside the application.
- The debug log strips query strings and folds long paths, because image CDNs put
  hundreds of characters of token into the path itself.

## Still to do

- **Code signing.** The build is unsigned, so SmartScreen warns on first run.
  `signtool sign /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 /a <msi>`
- History and a downloads list
- Dragging tabs to reorder, restoring the last session
- Following the Windows display language on its own. For now the language is chosen in Settings

## License

Not decided yet. Until it is, treat this as source you can read rather than source you can
reuse.
