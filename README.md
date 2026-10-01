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

- **Tabs** that keep their WebView2 alive, so logins and scroll position survive a switch.
  Drag a tab to reorder; optionally reopen last session's tabs on startup
- **Bookmarks** — a bar and a sidebar, with import and export of Netscape bookmark files
  (the format Chrome and Firefox both use). Favicons are fetched and cached
- **A bookmark manager** in its own tab: folder tree, search, multi-select with undo,
  drag to move and reorder, a list of duplicates, and a broken-link check that runs only
  when you start it
- **Downloads** — a default folder you can set, or a prompt for every download
- **Context menus** drawn by the app, in the app's own colors, including
  save / copy / copy-address for images
- **Per-monitor DPI**. Fonts, icons, row heights and menus all follow the display, and
  keep their size across 96 / 144 / 192 dpi and when a window moves between monitors
- **Japanese and English**, following the Windows display language unless you choose one
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

## Privacy

Voyager has no telemetry, no analytics and no account of its own. It contacts other computers
only as part of something you asked it to do:

- opening the websites you visit, which receive what any browser sends them
- sending a question to the AI service you selected, when you type one into the address bar
- downloading the files and saving the images you choose
- checking bookmark links, and only after you press "Start checking" in the bookmark manager

Favicons come from the sites you open. Settings, bookmarks, logins and cookies stay on your
computer (see [Where your data lives](#where-your-data-lives)). The debug log is off unless a
`debug.on` file is created, and it never leaves the computer.

Web pages are rendered by Microsoft Edge WebView2, which is part of Windows. What WebView2
itself reports to Microsoft follows your Windows diagnostic-data settings and the
[Microsoft Privacy Statement](https://privacy.microsoft.com/privacystatement).

## Still to do

- **Code signing.** The build is unsigned, so SmartScreen warns on first run. The plan is to
  sign releases built by GitHub Actions through SignPath Foundation
- History and a downloads list

## License

Copyright (C) 2026 K-S System

Voyager is free software: you can redistribute it and/or modify it under the terms of the
GNU General Public License as published by the Free Software Foundation, either version 3 of
the License, or (at your option) any later version. It is distributed in the hope that it
will be useful, but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See [LICENSE](LICENSE) for the full text.

In short: if you give a modified Voyager to anyone else, you must publish the complete source
of your version under the same license.

The installer also contains components under their own licenses:

| | |
|---|---|
| Microsoft.Web.WebView2 (the SDK, including `WebView2Loader.dll`) | BSD 3-Clause, © Microsoft Corporation |
| .NET runtime (self-contained build) | MIT, © .NET Foundation and contributors |

The WebView2 runtime itself is part of Windows and is not included.

The picture on the About page is an artist's concept of Voyager 1 entering interstellar space,
credit NASA/JPL-Caltech (PIA17462, 2013).

## Name and icon

The license covers the code. It does not cover the name or the icon.

The name "Voyager" for this browser and the Voyager icon mark the official builds: those made
from this repository by its GitHub Actions workflow and published on its Releases page. If
you distribute a modified version, please give it a different name and icon. Saying that it
is based on Voyager is fine.

## Code signing policy

Free code signing provided by [SignPath.io](https://about.signpath.io/), certificate by
[SignPath Foundation](https://signpath.org/)

- Developer: maxi5679-rgb
- Reviewer: maxi5679-rgb
- Code signing approver: maxi5679-rgb

This program will not transfer any information to other networked systems unless specifically
requested by the user or the person installing or operating it. See the
[Privacy](README.md#privacy) section.
