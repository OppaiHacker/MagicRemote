using System.Collections.Specialized;
using System.Drawing.Imaging;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Windows.Security.Credentials.UI;

namespace MagicRemote;

/// <summary>Windows host: one WinForms window with WebView2 for the UI and mstscax child windows for RDP pictures.</summary>
public sealed class MainForm : Form
{
    readonly WebView2 web = new() { Dock = DockStyle.Fill };
    readonly WinApp app;
    readonly System.Windows.Forms.Timer clipDebounce = new() { Interval = 200 };
    string clipSrc = "host";

    public MainForm()
    {
        Text = "MagicRemote";
        Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!);
        MinimumSize = new Size(960, 600);
        Size = new Size(1520, 940);
        StartPosition = FormStartPosition.CenterScreen;
        Store.Load();
        app = new WinApp(this);
        BackColor = web.DefaultBackgroundColor = Bg;
        Controls.Add(web);
        Load += async (_, _) => await Init();
        clipDebounce.Tick += (_, _) => { clipDebounce.Stop(); app.ReadClipboard(clipSrc); };
    }

    internal Color Bg => Store.D.Theme == "light" ? Color.FromArgb(247, 247, 248) : Color.FromArgb(13, 14, 16);
    internal CoreWebView2? Core => web.CoreWebView2;

    internal void ApplyTheme()
    {
        Native.SetCaption(Handle, Store.D.Theme != "light", Bg);
        BackColor = web.DefaultBackgroundColor = Bg;
    }

    async Task Init()
    {
        Native.SetCaption(Handle, Store.D.Theme != "light", Bg);
        var env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(Store.Dir, "WebView2"));
        await web.EnsureCoreWebView2Async(env);
        var w = web.CoreWebView2;
        w.SetVirtualHostNameToFolderMapping("app.magicremote", Path.Combine(AppContext.BaseDirectory, "wwwroot"), CoreWebView2HostResourceAccessKind.Deny);
        w.Settings.AreDefaultContextMenusEnabled = false;
        w.Settings.IsZoomControlEnabled = false;
        w.Settings.IsStatusBarEnabled = false;
        w.Settings.AreBrowserAcceleratorKeysEnabled = false;
        w.NewWindowRequested += (_, e) => e.Handled = true;
        w.PermissionRequested += (_, e) => { if (e.PermissionKind == CoreWebView2PermissionKind.ClipboardRead) e.State = CoreWebView2PermissionState.Allow; };
        w.WebMessageReceived += (_, e) =>
        {
            app.Dropped = e.AdditionalObjects?.OfType<CoreWebView2File>().Select(f => f.Path).ToArray() ?? [];
            try { app.Handle(e.WebMessageAsJson); } finally { app.Dropped = []; }
        };
        w.Navigate("https://app.magicremote/index.html");
        Native.AddClipboardFormatListener(Handle);
        app.Started();
    }

    protected override void WndProc(ref Message msg)
    {
        if (msg.Msg == Native.WM_CLIPBOARDUPDATE && !app.IgnoringClipboard)
        {
            // every RDP session syncs with this PC's clipboard, so the focused session is the source
            clipSrc = app.ClipSource(ContainsFocus);
            clipDebounce.Stop();
            clipDebounce.Start();
        }
        base.WndProc(ref msg);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        var live = app.LiveSessions;
        if (live > 0 && e.CloseReason == CloseReason.UserClosing &&
            MessageBox.Show(this, $"Disconnect {live} active session(s) and close?", "MagicRemote", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
        {
            e.Cancel = true;
            return;
        }
        app.Shutdown();
        base.OnFormClosing(e);
    }
}

sealed class WinApp(MainForm form) : App
{
    internal string[] Dropped = [];
    internal bool IgnoringClipboard => Environment.TickCount64 <= ignoreClipUntil;

    internal void Started() => StartTimers();

    protected override void Post(string json) => form.Core?.PostWebMessageAsJson(json);

    public override void UI(Action a)
    {
        if (form.IsDisposed || form.Disposing) return;
        if (form.InvokeRequired) form.BeginInvoke(a); else a();
    }

    protected override Session NewRdp(Machine m) => new RdpSession(this, form, m, stage);
    protected override void OnTheme() => form.ApplyTheme();
    protected override void Quit() { Shutdown(); Environment.Exit(0); }
    public override void FocusActive() { if (!occluded) (active as RdpSession)?.FocusRemote(); }

    public override IFs OpenSmb(string root, string home, Cred cred)
    {
        if (!Directory.Exists(root)) Native.ConnectShare(root, cred.User, Store.Unprotect(cred.Pass)); // reachable with this Windows login (domain SSO) = no extra logon
        return new LocalFs(Directory.Exists(home) ? home : root);
    }

    protected override bool Extra(string t, JsonElement m)
    {
        string S(string k) => m.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";
        var diag = Environment.GetEnvironmentVariable("MAGICREMOTE_DIAG") == "1";
        switch (t)
        {
            case "drop":
                var to = m.GetProperty("to");
                if (Dropped.Length > 0) DropFiles(Dropped, to.GetProperty("loc").GetString()!, to.TryGetProperty("path", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null, m.TryGetProperty("move", out var mv) && mv.ValueKind == JsonValueKind.True);
                return true;
            case "dropdiag" when diag:
                if (Find(S("sid")) is RdpSession dd) Post(JsonSerializer.Serialize(new { t = "dropdiag", r = dd.DiagDrop(S("file")) }, Store.Json));
                return true;
            case "probe" when diag:
                if (Find(S("sid")) is RdpSession probed)
                {
                    var n = m.GetProperty("n").GetInt32();
                    var at = probed.Host.PointToScreen(new Point(probed.Host.Width / 2, probed.Host.Height / 2));
                    _ = probed.ProbeLatency(n, 0x39).ContinueWith(r => UI(() => Post(JsonSerializer.Serialize(new { t = "probe", samples = r.IsFaulted ? [] : r.Result, err = r.Exception?.InnerException?.Message, x = at.X, y = at.Y }, Store.Json))));
                }
                return true;
        }
        return false;
    }

    internal string ClipSource(bool formFocused) =>
        active is RdpSession r && r.Host.ContainsFocus ? r.M.Id
        : active is SshSession && formFocused && !occluded ? active.M.Id : "host";

    internal void ReadClipboard(string src)
    {
        // read on a separate STA thread: the RDP clipboard owner lives on the UI thread and renders on demand
        var th = new Thread(() =>
        {
            string type, text = ""; string[]? names = null; string? img = null; object? payload = null;
            try
            {
                var d = Clipboard.GetDataObject();
                if (d == null) return;
                if (d.GetData(DataFormats.FileDrop) is string[] files)
                {
                    type = "files"; names = files.Select(f => Path.GetFileName(f.TrimEnd('\\'))).ToArray(); payload = files;
                }
                else if (d.GetDataPresent("FileGroupDescriptorW") && d.GetData("FileGroupDescriptorW") is MemoryStream ms)
                {
                    type = "files"; names = ClipFormats.FileNames(ms.ToArray()); // files living in a remote session
                }
                else if (d.GetData(DataFormats.UnicodeText) is string s)
                {
                    type = "text"; payload = s; text = s.Length > 4000 ? s[..4000] : s;
                }
                else if (d.GetData(DataFormats.Bitmap) is Bitmap b)
                {
                    type = "img"; payload = new Bitmap(b); text = $"Image {b.Width} × {b.Height}";
                    using var small = new Bitmap(b, 240, Math.Max(1, b.Height * 240 / Math.Max(1, b.Width)));
                    using var mem = new MemoryStream(); small.Save(mem, ImageFormat.Jpeg);
                    img = "data:image/jpeg;base64," + Convert.ToBase64String(mem.ToArray());
                }
                else return;
            }
            catch { return; }
            UI(() => AddClip(type, text, names, img, payload, src));
        }) { IsBackground = true };
        th.SetApartmentState(ApartmentState.STA);
        th.Start();
    }

    protected override void SetClipboard(object payload)
    {
        switch (payload)
        {
            case string s: Clipboard.SetText(s); break;
            case string[] files: var sc = new StringCollection(); sc.AddRange(files); Clipboard.SetFileDropList(sc); break;
            case Bitmap b: Clipboard.SetImage(b); break;
        }
    }

    protected override void ClearClipboardIf(string text)
    {
        if (Clipboard.ContainsText() && Clipboard.GetText() == text) Clipboard.Clear();
    }

    protected override async Task<bool> Verify()
    {
        try
        {
            if (await UserConsentVerifier.CheckAvailabilityAsync() != UserConsentVerifierAvailability.Available)
            {
                Toast("Windows Hello is unavailable — set up a PIN or biometrics in Windows Settings.", "err");
                return false;
            }
            var r = await UserConsentVerifierInterop.RequestVerificationForWindowAsync(form.Handle, "MagicRemote: confirm access to the vault password");
            if (r == UserConsentVerificationResult.Verified) return true;
            Toast("Verification cancelled", "err");
        }
        catch (Exception e) { Toast("Windows Hello: " + e.Message, "err"); }
        return false;
    }
}
