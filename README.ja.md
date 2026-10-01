# Voyager

Windows 向けの小さな AI ブラウザ。Chromium を同梱せず、**Windows 側にある WebView2 ランタイムを
共有**して動く。だから配布物が小さく済み、エンジンの更新は Windows Update に任せられる。

同じ発想で二度作った「栞（Shiori）」— Chromium / Electron 版（約 200MB）と、薄い WebView2 版
（v0.2.0）— を置き換えるもの。

- 正式名: **Voyager**（Explorer → Navigator → Voyager）
- 裏テーマ: V'Ger

The English README is at [README.md](README.md).

## 何をするもの

アドレス欄は URL でも質問でも受け付ける。URL ならそのまま開き、それ以外は選んでいる AI
（Gemini / ChatGPT / Claude / Meta AI / DeepSeek / Grok / Perplexity）へ送る。`google` `ぐぐる`
`ようつべ` といった語だけは、検索せずにそのサイトを開く。

あとは普通のブラウザ。状態を保つタブ、ブックマークのバーとサイドバー、ダウンロード、
自前の右クリックメニュー。

## 何が変わったか

Chromium 版には毎日使うには辛い欠点が二つあった。どちらもここで直っている。

| | 栞 Chromium 版 | 栞 WebView2 版 0.2.0 | Voyager |
|---|---|---|---|
| エンジン | Electron 37.10.3 同梱 | WebView2 共有 | WebView2 共有 |
| 配布サイズ | 約 200MB | 約 1MB | 約 40MB（self-contained） |
| AI 切り替え | **選び直しても前の AI に戻る** | 未実装 | 修正済み |
| タブ切り替え | **毎回リロード（ログインが飛ぶ）** | — | ビューを保持 |
| インストーラ | 7-Zip SFX + バッチ | — | **MSI（Windows Installer）** |
| アンインストール | アプリ一覧に出ない | — | 設定 →「アプリ」から |

### AI 切り替えのバグについて

Chromium 版 `renderer/app.js` の `paint()` はこうなっていた。

```js
else if (!tab.url) renderStart();
else renderPage(tab);      // ← タブに前の URL が残っているとここに来る
```

AI を選び直しても `tab.url` を消していなかったので、古いページが描き直されていた。
Voyager では `MainForm.OnUiMessage()` の `pickEngine` が必ず `ShowStart()` を通る。

## 機能

- **タブ** は WebView2 を生かしたまま持つので、切り替えてもログインとスクロール位置が残る。
  ドラッグで並べ替えられ、起動時に前回のタブを開き直すこともできる
- **ブックマーク** — バーとサイドバー。Netscape 形式（Chrome も Firefox も使う形式）の
  取り込みと書き出しに対応。ファビコンは取得して保持する
- **ブックマークマネージャー**（専用のタブ）— フォルダの木、検索、複数選択と元に戻す、ドラッグでの
  移動と並べ替え、重複の一覧、押したときだけ動くリンク切れの確認
- **ダウンロード** — 既定の保存先を決めるか、毎回確認するかを選べる
- **右クリックメニュー** はアプリ自身が描く。画像の保存・コピー・アドレスのコピーも入っている
- **拡大率への追従**。字・アイコン・行の高さ・メニューが画面に合わせて伸び、96 / 144 / 192 dpi の
  どれでも、またモニター間を移動しても大きさを保つ
- **日本語と英語**。設定で選ばなければ Windows の表示言語に合わせる
- **About 画面**。V'Ger と画面に出したかったので

## 構成

```
Voyager.csproj          .NET 10 / WinForms / WebView2
app.manifest            asInvoker・PerMonitorV2
assets/Voyager.ico      アイコン（16〜256px）
assets/voyager.jpg      About 画面に出す想像図
src/
  Program.cs            起動と WebView2 ランタイム確認
  MainForm.cs           タブ・ツールバー・右クリック・ショートカット・ダウンロード
  BrowserTab.cs         タブ 1 個分の状態（WebView2 は破棄しない）
  TabItem.cs            タブの見た目
  Pages.cs              内部ページ（AI 選択 / スタート / 設定 / About）の HTML
  Engines.cs            AI の一覧とクエリ URL
  UrlHelper.cs          URL 判定（Chromium 版 parseUrl の移植）
  AppSettings.cs        設定の保存
  Bookmarks.cs          ブックマークの保持
  BookmarkBar.cs        ツールバー下のバー
  BookmarkSidebar.cs    左側のツリー
  NetscapeBookmarks.cs  ブックマーク HTML の取り込みと書き出し
  Favicons.cs           ファビコンの復元と拡大率ごとのキャッシュ
  Log.cs                デバッグログ（URL は伏せる）
  Theme.cs / DarkMenu.cs  配色とメニューの見た目
installer/Voyager.ui.wxs  MSI 定義（ファイル・ウィザード・日英両方の文言）
installer/ui/           ウィザードの横と上の絵
build.cmd               ビルド・パッケージ・インストール
nextbuild.ps1           次のバージョン番号を決める
```

## ビルド

Windows、.NET 10 SDK と WiX が要る。

```
build.cmd
```

self-contained の win-x64 で publish し、stage して、同じソースから MSI を 2 本作り、英語版の方の
インストーラを起動する。経過は `make-log.txt` に出る。`build.cmd` に渡した引数はそのまま
インストーラへ流れるので、`build.cmd UILANG=1033` とすれば日本語 Windows でも英語のウィザードが見られる。

バージョン番号は `nextbuild.ps1` が決める。レジストリ、インストール済みの exe、`buildno.txt`、フォルダ内の成果物を見て、見つかった最大値 + 1 を取るので、フォルダを
掃除しても番号が巻き戻らない。

## インストール

インストーラは 2 本ある。中身は同じで、違うのは Windows Installer 自身が出す文言
（進行状況に流れる説明など）の言語だけ。

| | |
|---|---|
| `Voyager-x.y.z-x64.msi` | English |
| `Voyager-x.y.z-x64-ja.msi` | 日本語 |

ウィザードの文言そのものは、どちらでも Windows の表示言語に従う。決め打ちしたいときは
`UILANG` を渡す。英語なら `msiexec /i Voyager-x.y.z-x64-ja.msi UILANG=1033`、日本語なら `UILANG=1041`。

どちらもユーザー単位インストールなので UAC は出ない。

- インストール先: `%LOCALAPPDATA%\Programs\Voyager`
- ショートカット: デスクトップ / スタートメニュー
- アンインストール: 設定 →「アプリ」から

WebView2 が要る。Windows 11 と更新済みの Windows 10 なら既に入っている。無ければ起動時に
その旨とダウンロード先を出す。

## データの置き場所

| | |
|---|---|
| 設定 | `%APPDATA%\Voyager\settings.json` |
| ブックマーク | `%APPDATA%\Voyager\bookmarks.json` |
| ログイン状態・Cookie | `%LOCALAPPDATA%\Voyager\WebView2` |

アンインストールしてもこの 3 つは残る。完全に消すならフォルダごと削除する。

## 操作

| | |
|---|---|
| Ctrl+T | 新しいタブ |
| Ctrl+W | タブを閉じる |
| Ctrl+L | アドレス欄へ |
| Ctrl+R / F5 | 再読み込み |
| Alt+← / → | 戻る / 進む |
| Ctrl+Tab | 次のタブ |

フォーカスが外れているアドレス欄を押すと URL が丸ごと選ばれる。もう一度押せば普通にキャレットが
置ける。

## セキュリティ上の作り

- 内部ページ（AI 選択・スタート・設定・About）は**専用の WebView2** で表示し、そこだけ
  `postMessage` を有効にしている。サイトを表示する WebView2 は `IsWebMessageEnabled = false`
  なので、外部サイトからアプリを操作できない。
- 内部ページの Content-Security-Policy は `default-src 'none'`。画像も `data:` しか許していないので、
  内部ページが外へ出ていくことはない。
- `NavigationStarting` で `http` / `https` 以外（`file:` など）を遮断。
- 別ウィンドウ要求（`target="_blank"`）はアプリ内のタブとして開く。
- デバッグログは URL のクエリを落とし、長いパスを畳む。画像 CDN はパスそのものに何百文字もの
  トークンを載せてくるため。

## プライバシー

Voyager には、利用状況の送信（テレメトリー）も、解析も、独自のアカウントもない。外と通信するのは、
利用者が頼んだことの一部としてだけ。

- 開いたサイトを表示する（サイトには、どのブラウザでも送る情報が届く）
- アドレス欄に質問を打ったとき、選んでいる AI にその質問を送る
- 選んだファイルのダウンロードと、画像の保存
- ブックマークのリンク切れ確認。マネージャーで［確認を始める］を押したときだけ

ファビコンは開いたサイトから取る。設定・ブックマーク・ログイン状態・Cookie はこの PC の中に
残る（[データの置き場所](#データの置き場所)）。デバッグログは `debug.on` を置かない限り出ず、
PC の外へは出ない。

ページの表示には Windows の一部である Microsoft Edge WebView2 を使っている。WebView2 自体が
Microsoft に送る情報は、Windows の診断データの設定と
[Microsoft のプライバシーに関する声明](https://privacy.microsoft.com/ja-jp/privacystatement)に従う。

## 残っている宿題

- **コード署名**。未署名なので初回起動時に SmartScreen が出る。GitHub Actions で作ったリリースを、
  SignPath Foundation で署名する予定
- 履歴とダウンロード一覧

## ライセンス

Copyright (C) 2026 K-S System

Voyager はフリーソフトウェアです。Free Software Foundation が公表した GNU General Public License
のバージョン 3、または（選択により）それ以降のバージョンの条件に従って、再配布や改変ができます。
役に立つことを願って配布していますが、商品性や特定の目的への適合性を含め、いかなる保証もありません。
全文は [LICENSE](LICENSE)（英語、こちらが正本）を見てください。

ひと言でいえば、改造した Voyager を他人に渡すなら、その版のソースを全部、同じライセンスで公開する
必要があります。

インストーラーには、別のライセンスの部品も入っています。

| | |
|---|---|
| Microsoft.Web.WebView2（SDK。`WebView2Loader.dll` を含む） | BSD 3-Clause、© Microsoft Corporation |
| .NET ランタイム（self-contained ビルド） | MIT、© .NET Foundation and contributors |

WebView2 ランタイム本体は Windows の一部で、同梱していません。

About ページの画像は、星間空間に入るボイジャー1号の想像図です。Credit: NASA/JPL-Caltech
（PIA17462, 2013）。

## 名前とアイコン

ライセンスが対象にしているのはコードで、名前とアイコンは含みません。

このブラウザの名前としての「Voyager」と、Voyager のアイコンは、公式版の目印です。公式版とは、
このリポジトリから GitHub Actions で作り、Releases ページで配っているものです。改造版を配るときは、
別の名前とアイコンにしてください。「Voyager をもとにした」と書くのはかまいません。

## Code signing policy

Free code signing provided by [SignPath.io](https://about.signpath.io/), certificate by
[SignPath Foundation](https://signpath.org/)
（コード署名は SignPath.io が無償で提供し、証明書は SignPath Foundation が発行しています。）

- 開発者: maxi5679-rgb
- レビュアー: maxi5679-rgb
- コード署名の承認者: maxi5679-rgb

This program will not transfer any information to other networked systems unless specifically
requested by the user or the person installing or operating it.
（利用者、またはインストール・操作する人がはっきり求めない限り、このプログラムはネットワーク上の他のシステムへ情報を送りません。）
[プライバシー](README.ja.md#プライバシー)の節も参照してください。
