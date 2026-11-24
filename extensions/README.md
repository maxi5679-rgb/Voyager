# 同梱の拡張機能

| フォルダ | 中身 | 版 | 取り込み元 |
|---|---|---|---|
| `ublock-lite` | uBlock Origin Lite（Chromium 版、手を加えていない） | 2026.1122.1948 | https://github.com/uBlockOrigin/uBOL-home の `chromium/`（コミット 03490ff） |

`src/AdBlock.cs` が起動時にこのフォルダをデータフォルダへ写し、WebView2 に読み込ませる。
写し直すのは `manifest.json` が変わったときだけ。

## 更新のしかた

1. uBOL-home の `chromium/` フォルダの中身で `ublock-lite/` を丸ごと置き換える（`log.txt` は要らない）
2. 上の表の版とコミットを書き換える
