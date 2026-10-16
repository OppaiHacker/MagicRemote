using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MagicRemote;

/// <summary>
/// Linux host: the same UI served on 127.0.0.1 (WebSocket bridge, random token) and shown in a Chromium-family
/// app window; RDP runs in FreeRDP windows. Single UI thread = one dispatcher loop.
/// </summary>
sealed class LinuxApp : App
{
    readonly BlockingCollection<Action> queue = [];
    readonly List<(WebSocket ws, SemaphoreSlim gate)> clients = [];
    readonly string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
    readonly HttpListener http = new();
    readonly string www = Path.Combine(AppContext.BaseDirectory, "wwwroot");
    int port;
    public string Url => $"http://127.0.0.1:{port}/#k={token}";

    public static int Run(string[] args)
    {
        Store.Load();
        // one backend per data directory: a second launch just opens another window on the running one
        Directory.CreateDirectory(Store.Dir);
        var urlFile = Path.Combine(Store.Dir, "instance.url");
        FileStream? lockFile;
        try { lockFile = new FileStream(Path.Combine(Store.Dir, "instance.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException)
        {
            var url = File.Exists(urlFile) ? File.ReadAllText(urlFile).Trim() : "";
            if (url == "") { Console.Error.WriteLine("MagicRemote is already running."); return 1; }
            if (args.Contains("--no-browser")) Console.WriteLine(url); else Browser.Open(url, Store.Dir);
            return 0;
        }

        var app = new LinuxApp();
        app.Listen(args);
        using (var f = new FileStream(urlFile, new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write, UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite }))
        using (var w = new StreamWriter(f)) w.Write(app.Url);
        Console.WriteLine(app.Url);

        using var term = System.Runtime.InteropServices.PosixSignalRegistration.Create(System.Runtime.InteropServices.PosixSignal.SIGTERM, c => { c.Cancel = true; app.queue.CompleteAdding(); });
        using var intr = System.Runtime.InteropServices.PosixSignalRegistration.Create(System.Runtime.InteropServices.PosixSignal.SIGINT, c => { c.Cancel = true; app.queue.CompleteAdding(); });
        app.StartTimers();
        if (!args.Contains("--no-browser"))
        {
            var p = Browser.Open(app.Url, Store.Dir);
            // our own app-mode browser profile: the window closing ends the app
            if (p != null) Task.Run(() => { p.WaitForExit(); app.queue.CompleteAdding(); });
        }
        foreach (var a in app.queue.GetConsumingEnumerable())
        {
            try { a(); } catch (Exception e) { Log(e); }
        }
        app.Shutdown();
        try { File.Delete(urlFile); } catch { }
        lockFile.Dispose();
        return 0;
    }

    public override void UI(Action a)
    {
        if (!queue.IsAddingCompleted) try { queue.Add(a); } catch (InvalidOperationException) { }
    }

    // ---------- web server ----------
    void Listen(string[] args)
    {
        var want = Environment.GetEnvironmentVariable("MAGICREMOTE_PORT") is { Length: > 0 } ps && int.TryParse(ps, out var pp) ? pp : 0;
        for (int i = 0; ; i++)
        {
            port = want > 0 ? want : Random.Shared.Next(20000, 60000);
            http.Prefixes.Clear();
            http.Prefixes.Add($"http://127.0.0.1:{port}/");
            try { http.Start(); break; }
            catch (HttpListenerException) when (want == 0 && i < 20) { }
        }
        Task.Run(async () =>
        {
            while (http.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = await http.GetContextAsync(); } catch { break; }
                _ = Task.Run(() => Serve(ctx));
            }
        });
    }

    bool Authorized(HttpListenerRequest r) =>
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(r.QueryString["k"] ?? ""), Encoding.ASCII.GetBytes(token));

    async Task Serve(HttpListenerContext ctx)
    {
        var req = ctx.Request;
        var res = ctx.Response;
        try
        {
            // DNS rebinding: only our own origin
            if (req.Headers["Host"] != $"127.0.0.1:{port}") { res.StatusCode = 403; res.Close(); return; }
            var path = req.Url!.AbsolutePath;
            if (path == "/ws")
            {
                if (!req.IsWebSocketRequest || !Authorized(req) || req.Headers["Origin"] is { } o && o != $"http://127.0.0.1:{port}") { res.StatusCode = 403; res.Close(); return; }
                var ws = (await ctx.AcceptWebSocketAsync(null, TimeSpan.FromSeconds(20))).WebSocket;
                await Pump(ws);
                return;
            }
            if (path == "/upload" && req.HttpMethod == "POST")
            {
                if (!Authorized(req) || req.Headers["Origin"] is { } o2 && o2 != $"http://127.0.0.1:{port}") { res.StatusCode = 403; res.Close(); return; }
                await Upload(req);
                res.StatusCode = 204;
                res.Close();
                return;
            }
            if (req.HttpMethod != "GET") { res.StatusCode = 405; res.Close(); return; }
            var rel = path == "/" ? "index.html" : Uri.UnescapeDataString(path.TrimStart('/'));
            var file = Path.GetFullPath(Path.Combine(www, rel));
            if (!file.StartsWith(www + Path.DirectorySeparatorChar) || !File.Exists(file)) { res.StatusCode = 404; res.Close(); return; }
            res.ContentType = Path.GetExtension(file) switch
            {
                ".html" => "text/html; charset=utf-8", ".js" => "text/javascript; charset=utf-8", ".css" => "text/css; charset=utf-8",
                ".svg" => "image/svg+xml", ".png" => "image/png", ".ico" => "image/x-icon", ".woff2" => "font/woff2", _ => "application/octet-stream",
            };
            res.Headers["Cache-Control"] = "no-cache";
            res.Headers["X-Content-Type-Options"] = "nosniff";
            res.Headers["Content-Security-Policy"] = "default-src 'self'; img-src 'self' data: blob:; style-src 'self' 'unsafe-inline'; connect-src 'self' ws://127.0.0.1:" + port;
            await using (var fs = File.OpenRead(file)) await fs.CopyToAsync(res.OutputStream);
            res.Close();
        }
        catch (Exception e) { Log(e); try { res.StatusCode = 500; res.Close(); } catch { } }
    }

    async Task Pump(WebSocket ws)
    {
        var entry = (ws, new SemaphoreSlim(1));
        lock (clients) clients.Add(entry);
        var buf = new byte[1 << 16];
        var msg = new MemoryStream();
        try
        {
            while (ws.State == WebSocketState.Open)
            {
                var r = await ws.ReceiveAsync(buf, CancellationToken.None);
                if (r.MessageType == WebSocketMessageType.Close) break;
                msg.Write(buf, 0, r.Count);
                if (!r.EndOfMessage) continue;
                var json = Encoding.UTF8.GetString(msg.GetBuffer(), 0, (int)msg.Length);
                msg.SetLength(0);
                UI(() => Handle(json));
            }
        }
        catch (WebSocketException) { }
        finally
        {
            lock (clients) clients.Remove(entry);
            try { ws.Dispose(); } catch { }
        }
    }

    protected override void Post(string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        List<(WebSocket ws, SemaphoreSlim gate)> now;
        lock (clients) now = [.. clients];
        foreach (var (ws, gate) in now)
            _ = Task.Run(async () =>
            {
                await gate.WaitAsync();
                try { if (ws.State == WebSocketState.Open) await ws.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None); }
                catch { }
                finally { gate.Release(); }
            });
    }

    /// <summary>A file dropped from the desktop file manager: browsers give contents, not paths, so it is uploaded first.</summary>
    async Task Upload(HttpListenerRequest req)
    {
        var name = Path.GetFileName(req.QueryString["name"] ?? "");
        if (name is "" or "." or "..") throw new ArgumentException("No file name.");
        string to = req.QueryString["to"] ?? "host", toPath = req.QueryString["path"] ?? "";
        var dir = Path.Combine(Store.Dir, "upload", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        var tmp = Path.Combine(dir, name);
        await using (var f = File.Create(tmp)) await req.InputStream.CopyToAsync(f);
        var tmpFs = new LocalFs(dir);
        UI(() => StartTransfer("host", dir, name, to, toPath == "" ? null : toPath, false, tmpFs, after: () => { try { Directory.Delete(dir, true); } catch { } }));
    }

    // ---------- host services ----------
    protected override Session NewRdp(Machine m) => new FreeRdpSession(this, m);
    protected override void Quit() => Task.Delay(300).ContinueWith(_ => queue.CompleteAdding()); // let the "quit" message reach the window

    protected override void SetClipboard(object payload)
    {
        switch (payload)
        {
            case string s: Clip.Set("text/plain;charset=utf-8", Encoding.UTF8.GetBytes(s)); break;
            case string[] files: Clip.Set("text/uri-list", Encoding.UTF8.GetBytes(string.Join("\r\n", files.Select(f => new Uri(f).AbsoluteUri)) + "\r\n")); break;
            case ClipImage img: Clip.Set(img.Mime, img.Bytes); break;
        }
    }

    protected override void ClearClipboardIf(string text)
    {
        if (Clip.GetText() == text) Clip.Set("text/plain;charset=utf-8", []);
    }

    protected override bool Extra(string t, JsonElement m)
    {
        if (t != "clipSeen") return false;
        // the UI reads the system clipboard whenever the window gains focus (copies made in FreeRDP windows or other apps)
        if (Environment.TickCount64 <= ignoreClipUntil) return true;
        var type = m.GetProperty("type").GetString();
        if (type == "text" && m.GetProperty("text").GetString() is { Length: > 0 } s)
            AddClip("text", s.Length > 4000 ? s[..4000] : s, null, null, s, "host");
        else if (type == "img" && m.GetProperty("img").GetString() is { } url && url.StartsWith("data:image/") && url.IndexOf(";base64,") is var i and > 0)
        {
            var bytes = Convert.FromBase64String(url[(i + 8)..]);
            AddClip("img", m.TryGetProperty("text", out var tx) ? tx.GetString() ?? "Image" : "Image", null, url, new ClipImage(url[5..i], bytes), "host");
        }
        return true;
    }

    /// <summary>polkit authentication (the desktop's own password dialog) before a vault password is shown.</summary>
    protected override async Task<bool> Verify()
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("pkexec", "--disable-internal-agent /bin/true") { RedirectStandardError = true })!;
            await p.WaitForExitAsync();
            if (p.ExitCode == 0) return true;
            Toast(p.ExitCode == 126 ? "Verification cancelled" : "polkit verification failed — a desktop authentication agent (pkexec) is required.", "err");
        }
        catch (Exception e) { Toast("Verification: " + e.Message, "err"); }
        return false;
    }

    /// <summary>SMB share mounted through GVFS (gio mount), then used as a local path.</summary>
    public override IFs OpenSmb(string root, string home, Cred cred)
    {
        var parts = root.TrimStart('\\').Split('\\', 2);
        string host = parts[0], share = parts.Length > 1 ? parts[1] : "C$";
        var mount = Gvfs.Mount(host, share, cred);
        var rest = home.Length > root.Length ? home[root.Length..].TrimStart('\\').Replace('\\', '/') : "";
        var h = rest == "" ? mount : Path.Combine(mount, rest);
        return new LocalFs(Directory.Exists(h) ? h : mount);
    }
}

sealed record ClipImage(string Mime, byte[] Bytes);

/// <summary>System clipboard writes through wl-copy (Wayland) or xclip (X11).</summary>
static class Clip
{
    static bool Wayland => Environment.GetEnvironmentVariable("WAYLAND_DISPLAY") is { Length: > 0 } && Tool.Exists("wl-copy");

    public static void Set(string mime, byte[] data)
    {
        var psi = Wayland ? new ProcessStartInfo("wl-copy", data.Length == 0 ? "--clear" : $"--type {mime}")
            : Tool.Exists("xclip") ? new ProcessStartInfo("xclip", $"-selection clipboard -t {mime.Split(';')[0]} -i")
            : throw new InvalidOperationException("Clipboard: install wl-clipboard (Wayland) or xclip (X11).");
        psi.RedirectStandardInput = true;
        psi.RedirectStandardOutput = psi.RedirectStandardError = true; // forked owners keep no pipe of ours alive
        using var p = Process.Start(psi)!;
        p.StandardInput.BaseStream.Write(data);
        p.StandardInput.Close();
        p.WaitForExit(3000);
    }

    public static string? GetText()
    {
        try
        {
            var psi = Wayland ? new ProcessStartInfo("wl-paste", "--no-newline --type text") : new ProcessStartInfo("xclip", "-selection clipboard -o");
            psi.RedirectStandardOutput = psi.RedirectStandardError = true;
            using var p = Process.Start(psi)!;
            var s = p.StandardOutput.ReadToEnd();
            p.WaitForExit(3000);
            return p.ExitCode == 0 ? s : null;
        }
        catch { return null; }
    }
}

static class Tool
{
    public static bool Exists(string name) => Find(name) != null;

    public static string? Find(string name) =>
        (Environment.GetEnvironmentVariable("PATH") ?? "/usr/bin:/bin").Split(':', StringSplitOptions.RemoveEmptyEntries)
            .Select(d => Path.Combine(d, name)).FirstOrDefault(File.Exists);
}

static class Browser
{
    /// <summary>Opens the UI as an app window (own profile = its own process); falls back to the default browser.</summary>
    public static Process? Open(string url, string dataDir)
    {
        var exe = Environment.GetEnvironmentVariable("MAGICREMOTE_BROWSER") is { Length: > 0 } b ? b
            : new[] { "chromium", "chromium-browser", "google-chrome-stable", "google-chrome", "microsoft-edge-stable", "microsoft-edge", "brave-browser", "brave", "vivaldi-stable" }.Select(Tool.Find).FirstOrDefault(p => p != null);
        if (exe == null)
        {
            Process.Start(new ProcessStartInfo("xdg-open", url) { UseShellExecute = false })?.Dispose();
            return null;
        }
        var psi = new ProcessStartInfo(exe) { UseShellExecute = false };
        foreach (var a in new[] { "--app=" + url, "--user-data-dir=" + Path.Combine(dataDir, "browser"), "--class=MagicRemote", "--name=MagicRemote",
                     "--no-first-run", "--no-default-browser-check", "--window-size=1520,940", "--disable-features=Translate" })
            psi.ArgumentList.Add(a);
        return Process.Start(psi);
    }
}

static class Gvfs
{
    [System.Runtime.InteropServices.DllImport("libc")] static extern uint getuid();

    public static string Mount(string host, string share, Cred cred)
    {
        var dir = Path.Combine(Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") ?? $"/run/user/{getuid()}", "gvfs");
        string? Existing() => Directory.Exists(dir)
            ? Directory.EnumerateDirectories(dir).FirstOrDefault(d => string.Equals(Path.GetFileName(d), $"smb-share:server={host},share={share}", StringComparison.OrdinalIgnoreCase))
            : null;
        if (Existing() is { } m) return m;
        if (!Tool.Exists("gio")) throw new InvalidOperationException("Files over SMB require gio + gvfs-smb (or set the machine to Linux/SFTP).");
        var (domain, user) = Session.SplitUser(cred.User);
        var psi = new ProcessStartInfo("gio") { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        psi.ArgumentList.Add("mount");
        psi.ArgumentList.Add($"smb://{host}/{Uri.EscapeDataString(share)}");
        using var p = Process.Start(psi)!;
        // gio asks: User, Domain, Password (only what it needs; extra lines are ignored)
        p.StandardInput.Write($"{user}\n{(domain == "" ? "WORKGROUP" : domain)}\n{Store.Unprotect(cred.Pass)}\n");
        p.StandardInput.Close();
        var err = p.StandardError.ReadToEndAsync();
        p.StandardOutput.ReadToEnd();
        if (!p.WaitForExit(30000)) { p.Kill(); throw new TimeoutException("SMB: mounting the share timed out."); }
        return Existing() ?? throw new InvalidOperationException("SMB: " + (err.Result.Trim() is { Length: > 0 } e ? e : "could not mount the share"));
    }
}
