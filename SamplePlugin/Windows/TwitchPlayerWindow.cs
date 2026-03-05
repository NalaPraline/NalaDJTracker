using System;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Web.WebView2.WinForms;

namespace Nala.Windows;

public class TwitchPlayerWindow : IDisposable
{
    private Thread? formThread;
    private Form? form;
    private WebView2? webView;
    private string? currentUsername;
    private bool disposed;

    public bool IsOpen => form != null && !form.IsDisposed;

    public void OpenStream(string username)
    {
        if (IsOpen)
        {
            // Navigate to the new stream and bring window to front
            form!.Invoke(() =>
            {
                currentUsername = username;
                webView!.CoreWebView2?.Navigate(BuildPlayerUrl(username));
                form.BringToFront();
                form.Activate();
            });
            return;
        }

        currentUsername = username;
        formThread = new Thread(() => RunForm(username));
        formThread.SetApartmentState(ApartmentState.STA);
        formThread.IsBackground = true;
        formThread.Start();
    }

    private void RunForm(string username)
    {
        Application.SetHighDpiMode(HighDpiMode.SystemAware);

        form = new Form
        {
            Text = $"Twitch - {username}",
            Width = 960,
            Height = 560,
            StartPosition = FormStartPosition.CenterScreen,
        };

        webView = new WebView2
        {
            Dock = DockStyle.Fill,
        };

        form.Controls.Add(webView);

        form.Load += async (_, _) =>
        {
            try
            {
                await webView.EnsureCoreWebView2Async();
                webView.CoreWebView2.Navigate(BuildPlayerUrl(username));
                form.Text = $"Twitch - {username}";
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Could not load WebView2.\n\nMake sure the Microsoft Edge WebView2 Runtime is installed.\n" +
                    $"Download it from: https://developer.microsoft.com/microsoft-edge/webview2/\n\nDetails: {ex.Message}",
                    "WebView2 Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                form.Close();
            }
        };

        form.FormClosed += (_, _) =>
        {
            webView?.Dispose();
            webView = null;
            form = null;
        };

        Application.Run(form);
    }

    public void Close()
    {
        if (IsOpen)
        {
            form!.Invoke(() => form.Close());
        }
    }

    private static string BuildPlayerUrl(string username)
    {
        return $"https://player.twitch.tv/?channel={Uri.EscapeDataString(username)}&parent=localhost&autoplay=true";
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        Close();
    }
}
