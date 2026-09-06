using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;
using Wpf.Ui.Controls;

namespace GofileUploader;

public partial class MainWindow : FluentWindow
{
    private static readonly HttpClient Http = new(new HttpClientHandler())
    {
        Timeout = Timeout.InfiniteTimeSpan
    };

    private static readonly Regex TokenLikeRegex = new(@"^[A-Za-z0-9]{20,64}$", RegexOptions.Compiled);

    private static readonly Color AccentColor = Color.FromRgb(0x0A, 0x84, 0xFF);
    private static readonly Color DropZoneRestFill = Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF);
    private static readonly Color DropZoneHoverFill = Color.FromArgb(0x22, 0x0A, 0x84, 0xFF);
    private static readonly Color DropZoneRestStroke = Color.FromArgb(0x28, 0xFF, 0xFF, 0xFF);
    private static readonly Color DropZoneRestIcon = Color.FromRgb(0xAB, 0xAB, 0xB2);

    private string? _selectedFile;
    private DispatcherTimer? _clipboardTimer;
    private string? _lastClipboardSeen;
    private DateTime _clipboardArmUntil;

    public MainWindow()
    {
        InitializeComponent();

        TokenBox.Password = SecureSettings.LoadToken() ?? "";
        FolderBox.Text = SecureSettings.LoadFolder() ?? "";
    }

    private void TokenBox_PasswordChanged(object sender, RoutedEventArgs e)
        => SecureSettings.SaveToken(TokenBox.Password);

    private void FolderBox_TextChanged(object sender, TextChangedEventArgs e)
        => SecureSettings.SaveFolder(FolderBox.Text);

    // ---------- File selection ----------

    private void SetFile(string path)
    {
        if (!File.Exists(path))
        {
            StatusText.Text = "That file could not be found.";
            return;
        }

        _selectedFile = path;
        var info = new FileInfo(path);
        FileInfoText.Text = $"Selected: {info.Name}  ({HumanSize(info.Length)})";
        DropTitleText.Text = info.Name;
        DropSubText.Text = $"{HumanSize(info.Length)} - click or drop to change";
        StatusText.Text = "";
    }

    private void DropZone_Click(object sender, MouseButtonEventArgs e)
    {
        var dlg = new OpenFileDialog { Title = "Select a file to upload" };
        if (dlg.ShowDialog() == true)
            SetFile(dlg.FileName);
    }

    private void DropZone_MouseEnter(object sender, MouseEventArgs e) => SetDropZoneHighlighted(true);

    private void DropZone_MouseLeave(object sender, MouseEventArgs e) => SetDropZoneHighlighted(false);

    private void DropZone_DragEnter(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            SetDropZoneHighlighted(true);
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void DropZone_DragLeave(object sender, DragEventArgs e) => SetDropZoneHighlighted(false);

    private void DropZone_Drop(object sender, DragEventArgs e)
    {
        SetDropZoneHighlighted(false);
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files)
                SetFile(files[0]);
        }
    }

    /// <summary>
    /// Fades the drop zone between its resting and highlighted look. Deliberately color-only
    /// (no scale/size change) - this element runs edge to edge, and WPF-UI's Mica backdrop can
    /// show a rendering seam at the window border when something near it changes size.
    /// </summary>
    private void SetDropZoneHighlighted(bool on)
    {
        var duration = TimeSpan.FromMilliseconds(150);
        AnimateBrushColor(DropZoneFill, on ? DropZoneHoverFill : DropZoneRestFill, duration);
        AnimateBrushColor(DropZoneStroke, on ? AccentColor : DropZoneRestStroke, duration);
        AnimateBrushColor(DropIconBrush, on ? AccentColor : DropZoneRestIcon, duration);
    }

    private static void AnimateBrushColor(SolidColorBrush brush, Color to, TimeSpan duration)
    {
        var animation = new ColorAnimation(to, duration) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
        brush.BeginAnimation(SolidColorBrush.ColorProperty, animation);
    }

    /// <summary>Reveals a collapsed section with a soft fade instead of popping in instantly.</summary>
    private static void RevealWithFade(UIElement element)
    {
        if (element.Visibility == Visibility.Visible && element.Opacity >= 1) return;
        element.Visibility = Visibility.Visible;
        element.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
    }

    private static string HumanSize(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double size = bytes;
        int i = 0;
        while (size >= 1024 && i < units.Length - 1)
        {
            size /= 1024;
            i++;
        }
        return $"{size:0.#}{units[i]}";
    }

    // ---------- Upload ----------

    private async void UploadButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_selectedFile) || !File.Exists(_selectedFile))
        {
            StatusText.Text = "Please choose a file first.";
            return;
        }

        UploadButton.IsEnabled = false;
        CopyButton.IsEnabled = false;
        OpenButton.IsEnabled = false;
        ResultBox.Text = "";
        UploadProgress.Value = 0;
        PercentText.Text = "";
        StatusText.Text = "Requesting best server...";
        RevealWithFade(ProgressSection);

        try
        {
            var serverName = await GetBestServerAsync();
            StatusText.Text = $"Uploading to {serverName}...";

            var uploadUrl = $"https://{serverName}.gofile.io/contents/uploadFile";
            var fileInfo = new FileInfo(_selectedFile);

            using var content = new MultipartFormDataContent();
            await using var fs = File.OpenRead(_selectedFile);

            var progress = new Progress<double>(pct =>
            {
                UploadProgress.Value = pct;
                PercentText.Text = $"{pct:0.0}%";
            });

            var progressStream = new ProgressStream(fs, fileInfo.Length, progress);
            var streamContent = new StreamContent(progressStream);
            streamContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            content.Add(streamContent, "file", fileInfo.Name);

            var token = TokenBox.Password?.Trim();
            var folder = FolderBox.Text?.Trim();

            if (!string.IsNullOrEmpty(folder))
                content.Add(new StringContent(folder), "folderId");
            if (!string.IsNullOrEmpty(token))
                content.Add(new StringContent(token), "token");

            using var request = new HttpRequestMessage(HttpMethod.Post, uploadUrl) { Content = content };
            if (!string.IsNullOrEmpty(token))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await Http.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();
            response.EnsureSuccessStatusCode();

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (!root.TryGetProperty("status", out var statusEl) || statusEl.GetString() != "ok")
                throw new Exception($"upload failed: {body}");

            var data = root.GetProperty("data");
            var link = data.TryGetProperty("downloadPage", out var dp) ? dp.GetString() : null;
            var guestToken = data.TryGetProperty("guestToken", out var gt) ? gt.GetString() : null;

            UploadProgress.Value = 100;
            PercentText.Text = "100%";
            StatusText.Text = "Upload complete!";
            ResultBox.Text = link ?? "";
            CopyButton.IsEnabled = !string.IsNullOrEmpty(link);
            OpenButton.IsEnabled = !string.IsNullOrEmpty(link);
            if (!string.IsNullOrEmpty(link))
                RevealWithFade(ResultCard);

            if (string.IsNullOrEmpty(token) && !string.IsNullOrEmpty(guestToken))
            {
                // No account token was set - gofile created a temporary guest account for this
                // upload. Keep its token so future uploads land in the same guest account
                // instead of creating a new one each time.
                TokenBox.Password = guestToken;
                LoginStatusText.Text = "Saved a guest account token automatically for future uploads.";
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error: {ex.Message}";
        }
        finally
        {
            UploadButton.IsEnabled = true;
        }
    }

    private static async Task<string> GetBestServerAsync()
    {
        using var response = await Http.GetAsync("https://api.gofile.io/servers");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        if (!root.TryGetProperty("status", out var statusEl) || statusEl.GetString() != "ok")
            throw new Exception("could not get gofile server list.");

        var servers = root.GetProperty("data").GetProperty("servers");
        if (servers.GetArrayLength() == 0)
            throw new Exception("gofile returned no available servers.");

        return servers[0].GetProperty("name").GetString()
               ?? throw new Exception("malformed server response.");
    }

    private void RotateTokenButton_Click(object sender, RoutedEventArgs e)
    {
        TokenBox.Password = ""; // triggers PasswordChanged -> SecureSettings.SaveToken(null)
        LoginStatusText.Text = "Token cleared - your next anonymous upload will get a brand new guest account.";
    }

    // ---------- Log in with browser (clipboard token detection) ----------

    private void LoginButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("https://gofile.io/") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            LoginStatusText.Text = $"Could not open browser: {ex.Message}";
            return;
        }

        LoginStatusText.Text = "Log in on gofile's site, open your profile page, then copy your token - it'll be picked up automatically.";

        try
        {
            _lastClipboardSeen = Clipboard.ContainsText() ? Clipboard.GetText() : null;
        }
        catch
        {
            _lastClipboardSeen = null;
        }

        _clipboardArmUntil = DateTime.UtcNow.AddMinutes(5);

        if (_clipboardTimer == null)
        {
            _clipboardTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
            _clipboardTimer.Tick += ClipboardTimer_Tick;
        }
        _clipboardTimer.Start();
    }

    private void ClipboardTimer_Tick(object? sender, EventArgs e)
    {
        if (DateTime.UtcNow > _clipboardArmUntil)
        {
            _clipboardTimer!.Stop();
            LoginStatusText.Text = "Stopped watching the clipboard (timed out). Click the button again if needed.";
            return;
        }

        string? text;
        try
        {
            if (!Clipboard.ContainsText()) return;
            text = Clipboard.GetText();
        }
        catch
        {
            return; // clipboard momentarily locked by another app - try again next tick
        }

        if (string.IsNullOrWhiteSpace(text) || text == _lastClipboardSeen) return;
        _lastClipboardSeen = text;

        text = text.Trim();
        if (TokenLikeRegex.IsMatch(text))
        {
            TokenBox.Password = text;
            LoginStatusText.Text = "Token detected from clipboard and filled in.";
            _clipboardTimer!.Stop();
        }
    }

    // ---------- Result actions ----------

    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(ResultBox.Text))
        {
            Clipboard.SetText(ResultBox.Text);
            StatusText.Text = "Link copied to clipboard!";
        }
    }

    private void OpenButton_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(ResultBox.Text))
            Process.Start(new ProcessStartInfo(ResultBox.Text) { UseShellExecute = true });
    }
}
