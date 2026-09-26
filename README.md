# Voyager

Windows 向けの AI ブラウザ。Chromium を同梱せず、Windows 側の **WebView2 ランタイムを共有**して動く。
旧「栞（Shiori）」の Chromium 版（Electron・約 200MB）と WebView2 版（v0.2.0）を統合したもの。

- 正式名: **Voyager**（Explorer → Navigator → Voyager）
- 裏テーマ: V'Ger

## 何が変わったか

| | 栞 Chromium 版 | 栞 WebView2 版 0.2.0 | Voyager 1.0.0 |
|---|---|---|---|
| エンジン | Electron 37.10.3 同梱 | WebView2 共有 | WebView2 共有 |
| 配布サイズ | 約 200MB | 約 1MB | 約 50MB（self-contained）/ 約 1MB（軽量版） |
| AI 切り替え | **選び直しても前の AI に戻る** | 未実装 | 修正済み |
| タブ切り替え | **毎回リロード（ログインが飛ぶ）** | なし | ビューを保持（ログイン・スクロール維持） |
| 右クリック | 日本語メニューあり | なし | 日本語メニューあり |
| インストーラ | 7-Zip SFX + バッチ | なし | **MSI（Windows Installer）** |
| アンインストール | アプリ一覧に出ない | — | 「アプリと機能」から可能 |

### AI 切り替えのバグについて

Chromium 版 `renderer/app.js` の `paint()` は

```js
else if (!tab.url) renderStart();
else renderPage(tab);      // ← タブに前の URL が残っているとここに来る
```

となっており、AI を選び直しても `tab.url` を消していなかった。
Voyager では `MainForm.OnUiMessage()` の `pickEngine` で必ず `ShowStart()` を通す。

## 構成

```
Voyager.csproj          .NET 10 / WinForms / WebView2
app.manifest            asInvoker・PerMonitorV2
assets/Voyager.ico      アイコン（16〜256px）
src/
  Program.cs            起動と WebView2 ランタイム確認
  MainForm.cs           タブ・ツールバー・右クリック・ショートカット
  BrowserTab.cs         タブ 1 個分の状態（WebView2 は破棄しない）
  TabItem.cs            タブの見た目
  Pages.cs              内部ページ（AI 選択 / スタート / 設定）の HTML
  Engines.cs            AI の一覧とクエリ URL
  UrlHelper.cs          URL 判定（Chromium 版 parseUrl の移植）
  AppSettings.cs        設定の保存
  Theme.cs / DarkMenu.cs  配色
installer/Voyager.wxs   MSI 定義
build.ps1 / build.sh    ビルドスクリプト
```

## ビルド

Windows（.NET 10 SDK + WiX v3）:

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1
```

Linux / macOS（.NET 10 SDK + msitools）:

```bash
./build.sh
FRAMEWORK_DEPENDENT=1 ./build.sh   # 約 1MB。動作には .NET 10 Desktop Runtime が必要
```

## インストール

MSI は 2 種類ある。どちらもユーザー単位インストールで UAC は出ない。

| ファイル | サイズ | 前提 |
|---|---|---|
| `Voyager-1.0.0-x64.msi` | 約 43MB | なし（.NET ランタイム同梱） |
| `Voyager-1.0.0-x64-lite.msi` | 約 750KB | .NET 10 Desktop Runtime が必要 |

どちらも UpgradeCode が同じなので、一方を入れるともう一方は自動で置き換わる。
lite 版はランタイム未導入の PC では Windows の「.NET をインストールしてください」画面が出る。

- インストール先: `%LOCALAPPDATA%\Programs\Voyager`
- ショートカット: デスクトップ / スタートメニュー
- アンインストール: 設定 →「アプリ」から

## データの置き場所

| 内容 | 場所 |
|---|---|
| 設定 | `%APPDATA%\Voyager\settings.json` |
| ログイン状態・Cookie | `%LOCALAPPDATA%\Voyager\WebView2` |

アンインストールしてもこの 2 つは残る。完全に消すならフォルダごと削除する。

## 操作

| キー | 動作 |
|---|---|
| Ctrl+T | 新しいタブ |
| Ctrl+W | タブを閉じる |
| Ctrl+L | アドレス欄へ |
| Ctrl+R / F5 | 再読み込み |
| Alt+← / → | 戻る / 進む |
| Ctrl+Tab | 次のタブ |

アドレス欄は URL ならそのまま開き、URL でなければ選んでいる AI に質問として送る。
`google` `ぐぐる` `ようつべ` などの語はそのままサイトを開く。

## セキュリティ上の作り

- 内部ページ（AI 選択・設定）は専用の WebView2 で表示し、そこだけ `postMessage` を有効にしている。
  サイトを表示する WebView2 は `IsWebMessageEnabled = false` なので、外部サイトからアプリを操作できない。
- `NavigationStarting` で `http` / `https` 以外（`file:` など）を遮断。
- 別ウィンドウ要求（`target="_blank"`）はアプリ内のタブとして開く。

## 残っている宿題

- **コード署名**。未署名なので初回起動時に SmartScreen が出る。
  `signtool sign /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 /a Voyager-1.0.0-x64.msi`
- 履歴・ブックマーク・ダウンロード一覧の UI
- タブのドラッグ並び替え、セッション復元
- 小さいサイズ（16/20px）用のアイコン簡略版
