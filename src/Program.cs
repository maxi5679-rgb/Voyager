using Microsoft.Web.WebView2.Core;

namespace Voyager;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        if (!WebView2Available(out var detail))
        {
            var answer = MessageBox.Show(
                $"""
                Microsoft Edge WebView2 ランタイムが見つかりません。

                Voyager は Windows に入っている WebView2 を共有して動きます
                （ブラウザ本体を同梱しないので軽量です）。

                ダウンロードページを開きますか？

                {detail}
                """,
                App.Name, MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

            if (answer == DialogResult.Yes)
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                        "https://developer.microsoft.com/microsoft-edge/webview2/") { UseShellExecute = true });
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
            }
            return;
        }

        Application.Run(new MainForm());
    }

    private static bool WebView2Available(out string detail)
    {
        try
        {
            var version = CoreWebView2Environment.GetAvailableBrowserVersionString();
            detail = Strings.DetectedVersion(version);
            return !string.IsNullOrEmpty(version);
        }
        catch (Exception ex)
        {
            detail = ex.Message;
            return false;
        }
    }
}
