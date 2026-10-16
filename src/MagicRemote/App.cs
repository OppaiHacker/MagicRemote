using System.Drawing;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Renci.SshNet.Common;

namespace MagicRemote;

/// <summary>
/// Everything the UI talks to, shared by both hosts: Windows (WinForms + WebView2 + mstscax) and Linux (local web server + FreeRDP).
/// The host supplies the window, the UI thread, the clipboard, the vault check and the RDP engine.
/// </summary>
public abstract class App
{
    protected readonly List<Session> sessions = [];
    protected readonly LocalFs hostFs = new(HostHome());
    readonly Dictionary<int, Transfer> xfers = [];
    readonly Dictionary<int, object> clipStore = [];
    protected Session? active;
    protected Rectangle stage;
    protected bool occluded = true, ready;
    int xferSeq, clipSeq;
    protected long ignoreClipUntil;
    string lastClipText = "";
    System.Threading.Timer? tick, reach;

    // ---------- host services ----------
    protected abstract void Post(string json);
    /// <summary>Runs on the UI thread; all session/transfer state is only touched there.</summary>
    public abstract void UI(Action a);
    protected abstract Session NewRdp(Machine m);
    /// <summary>OS user-presence check before a vault password is shown or copied (Windows Hello / polkit).</summary>
    protected abstract Task<bool> Verify();
    /// <summary>Puts a clipboard payload (string, string[] file paths or a host image object) on this PC's clipboard.</summary>
    protected abstract void SetClipboard(object payload);
    /// <summary>Clears the clipboard if it still holds this text (copied password).</summary>
    protected abstract void ClearClipboardIf(string text);
    /// <summary>SMB access to a Windows host as a local path (UNC on Windows, GVFS mount on Linux).</summary>
    public abstract IFs OpenSmb(string root, string home, Cred cred);
    protected virtual void OnTheme() { }
    /// <summary>Ends the app (after an update was staged).</summary>
    protected abstract void Quit();
    /// <summary>Host-specific messages (Windows: file drops with paths, diagnostics).</summary>
    protected virtual bool Extra(string t, JsonElement m) => false;
    public virtual void FocusActive() { }

    static string HostHome()
    {
        var desk = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        return Directory.Exists(desk) ? desk : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    protected void StartTimers()
    {
        tick = new System.Threading.Timer(_ => UI(PushXfers), null, 120, 120);
        reach = new System.Threading.Timer(_ => UI(CheckReach), null, 30000, 30000);
    }

    // ---------- bridge ----------
    void Send(object o) => Post(JsonSerializer.Serialize(o, Store.Json));
    public void Toast(string msg, string kind = "") => UI(() => Send(new { t = "toast", msg, kind }));
    public void TermOut(string sid, string data) => UI(() => Send(new { t = "term", sid, d = data }));
    public void SendNet(Session s) => Send(new { t = "net", sid = s.Id, rtt = s.Rtt });

    void PushData() => Send(new
    {
        t = "data", Store.D.Profiles, Store.D.Machines, Store.D.Profile, Store.D.Theme, Store.D.Sidebar,
        creds = Store.D.Creds.Select(c => new { c.Id, c.Label, c.User }),
        platform = OperatingSystem.IsWindows() ? "win" : "linux",
    });

    public void PushSessions() => Send(new
    {
        t = "sessions", active = active?.Id,
        list = sessions.Select(s => new { id = s.Id, mid = s.M.Id, kind = s.Kind, state = s.State, rtt = s.Rtt, w = s.W, h = s.H, scale = s.Scale, error = s.Error, external = s.External }),
    });

    /// <summary>One message from the UI. Call on the UI thread.</summary>
    public void Handle(string json)
    {
        JsonElement m;
        try { m = JsonDocument.Parse(json).RootElement; } catch { return; }
        string S(string k) => m.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";
        string? Opt(JsonElement o, string k) => o.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        bool B(string k) => m.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.True;
        try
        {
            var t = S("t");
            if (Extra(t, m)) return;
            switch (t)
            {
                case "ready": ready = true; PushData(); PushSessions(); CheckReach(); CheckUpdate(); break;
                case "doUpdate": _ = DoUpdate(); break;
                case "stage":
                    var dpr = m.GetProperty("dpr").GetDouble();
                    int P(string k) => (int)Math.Round(m.GetProperty(k).GetDouble() * dpr);
                    stage = new Rectangle(P("x"), P("y"), P("w"), P("h"));
                    Relayout();
                    break;
                case "occlude":
                    occluded = B("on");
                    Relayout();
                    if (!occluded) FocusActive();
                    break;
                case "connect": Connect(S("id")); break;
                case "activate": Activate(Find(S("sid"))); break;
                case "close": Find(S("sid"))?.Close(); break;
                case "reconnect": Find(S("sid"))?.Reconnect(); break;
                case "scale": Find(S("sid"))?.SetScale(S("v")); break;
                case "fullscreen": Find(S("sid"))?.GoFullScreen(); break;
                case "cad": Find(S("sid"))?.SendCtrlAltDel(); break;
                case "saveMachine": SaveMachine(m.GetProperty("m").Deserialize<Machine>(Store.Json)!, B("connect")); break;
                case "deleteMachine": DeleteMachine(S("id")); break;
                case "importRdp": ImportRdp(S("name"), S("text")); break;
                case "exportRdp":
                    if (Store.D.Machines.FirstOrDefault(x => x.Id == S("id")) is { } em)
                        Send(new { t = "download", name = RdpFile.SafeName(em.Name) + ".rdp", text = RdpFile.Export(em) });
                    break;
                case "addProfile":
                    var p = new Profile { Id = Guid.NewGuid().ToString("n"), Name = S("name").Trim(), Color = S("color") };
                    if (p.Name == "") break;
                    Store.D.Profiles.Add(p); Store.D.Profile = p.Id; Save();
                    break;
                case "selectProfile": if (Store.D.Profiles.Any(x => x.Id == S("id"))) { Store.D.Profile = S("id"); Save(); } break;
                case "theme":
                    Store.D.Theme = S("v") == "light" ? "light" : "dark";
                    OnTheme();
                    Store.Save();
                    break;
                case "sidebar": Store.D.Sidebar = B("v"); Store.Save(); break;
                case "addCred":
                    if (S("user").Trim() == "") break;
                    Store.D.Creds.Add(new Cred { Id = Guid.NewGuid().ToString("n"), Label = S("label").Trim(), User = S("user").Trim(), Pass = Store.Protect(S("pass")) });
                    Save();
                    break;
                case "deleteCred":
                    Store.D.Creds.RemoveAll(c => c.Id == S("id"));
                    foreach (var mm in Store.D.Machines.Where(x => x.Cred == S("id"))) mm.Cred = null;
                    Save();
                    break;
                case "revealCred": _ = UseCred(S("id"), copy: false); break;
                case "copyCred": _ = UseCred(S("id"), copy: true); break;
                case "thumbs":
                    active?.Snap();
                    Send(new { t = "thumbs", map = sessions.ToDictionary(s => s.Id, s => s.Thumb) });
                    break;
                case "list": List(m.GetProperty("req").GetInt32(), S("k"), S("loc"), Opt(m, "path"), Opt(m, "cd")); break;
                case "xfer":
                    var from = m.GetProperty("from"); var to = m.GetProperty("to");
                    foreach (var n in m.GetProperty("names").EnumerateArray())
                        StartTransfer(Opt(from, "loc")!, Opt(from, "path"), n.GetString()!, Opt(to, "loc")!, Opt(to, "path"), B("move"));
                    break;
                case "cancel": if (xfers.TryGetValue(m.GetProperty("id").GetInt32(), out var x)) x.Cts.Cancel(); break;
                case "mkdir": Mkdir(m.GetProperty("req").GetInt32(), S("k"), S("loc"), S("path"), S("name")); break;
                case "delete": DeleteItems(S("loc"), S("path"), m.GetProperty("names").EnumerateArray().Select(n => n.GetString()!).ToArray()); break;
                case "clipUse": ClipUse(m.GetProperty("id").GetInt32()); break;
                case "wake": Wake(S("id")); break;
                case "termIn": (Find(S("sid")) as SshSession)?.Input(S("d")); break;
                case "termSize": (Find(S("sid")) as SshSession)?.Resize(m.GetProperty("cols").GetUInt32(), m.GetProperty("rows").GetUInt32()); break;
            }
        }
        catch (Exception ex) { Log(ex); Toast(Friendly(ex), "err"); }
    }

    public static void Log(Exception ex)
    {
        try { Directory.CreateDirectory(Store.Dir); File.AppendAllText(Path.Combine(Store.Dir, "error.log"), $"{DateTime.Now:s} {ex}{Environment.NewLine}{Environment.NewLine}"); } catch { }
    }

    void Save() { Store.Save(); PushData(); }

    // ---------- sessions ----------
    protected Session? Find(string id) => sessions.FirstOrDefault(s => s.Id == id);

    public void Relayout()
    {
        foreach (var s in sessions) s.Place(stage, s == active && !occluded);
    }

    protected void Activate(Session? s)
    {
        if (s == null) return;
        if (active != null && active != s) active.Snap();
        active = s;
        Relayout();
        PushSessions();
    }

    void Connect(string id)
    {
        var m = Store.D.Machines.FirstOrDefault(x => x.Id == id);
        if (m == null) return;
        var existing = sessions.FirstOrDefault(s => s.M.Id == id);
        if (existing != null)
        {
            Activate(existing);
            if (existing.State == "disconnected") existing.Reconnect();
            return;
        }
        m.Last = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        Store.Save();
        Session s = m.Proto switch
        {
            "ssh" => new SshSession(this, m),
            "sftp" or "ftp" => new FilesSession(this, m),
            _ => NewRdp(m),
        };
        active?.Snap();
        sessions.Add(s);
        active = s;
        PushSessions();
        s.Start();
    }

    public void Remove(Session s)
    {
        var i = sessions.IndexOf(s);
        if (i < 0) return;
        sessions.RemoveAt(i);
        foreach (var t in xfers.Values.Where(t => t.State == "run" && (t.FromLoc == s.Id || t.ToLoc == s.Id))) t.Cts.Cancel();
        if (active == s) active = sessions.Count == 0 ? null : sessions[Math.Min(i, sessions.Count - 1)];
        s.Dispose();
        Relayout();
        PushSessions();
    }

    /// <summary>Ctrl+Alt+Left/Right inside a remote session releases focus — use it to switch tabs.</summary>
    public void Cycle(Session from, int direction)
    {
        if (sessions.Count < 2) return;
        var i = sessions.IndexOf(from);
        var next = sessions[(i + (direction == 1 ? -1 : 1) + sessions.Count) % sessions.Count];
        Activate(next);
        FocusActive();
    }

    void SaveMachine(Machine m, bool connect)
    {
        m.Host = m.Host.Trim();
        if (m.Host == "") throw new ArgumentException("Enter a host or IP address.");
        if (m.Proto is not ("rdp" or "ssh" or "sftp" or "ftp")) m.Proto = "rdp";
        if (m.Port is <= 0 or > 65535) m.Port = m.Proto switch { "ssh" or "sftp" => 22, "ftp" => 21, _ => 3389 };
        if (m.SshPort is <= 0 or > 65535) m.SshPort = 22;
        if (string.IsNullOrWhiteSpace(m.Id)) m.Id = Guid.NewGuid().ToString("n");
        m.Name = string.IsNullOrWhiteSpace(m.Name) ? m.Host : m.Name.Trim();
        m.Group = string.IsNullOrWhiteSpace(m.Group) ? "No group" : m.Group.Trim();
        m.App = m.App.Trim();
        if (!Store.D.Profiles.Any(p => p.Id == m.Profile)) m.Profile = Store.D.Profile;
        if (m.Cred != null && !Store.D.Creds.Any(c => c.Id == m.Cred)) m.Cred = null;
        var i = Store.D.Machines.FindIndex(x => x.Id == m.Id);
        if (i >= 0)
        {
            m.Last = Store.D.Machines[i].Last;
            Store.D.Machines[i] = m;
            foreach (var s in sessions.Where(s => s.M.Id == m.Id)) s.M = m; // takes effect on next connect
        }
        else Store.D.Machines.Add(m);
        Store.D.Profile = m.Profile;
        Save();
        if (connect) Connect(m.Id);
        CheckReach();
    }

    void DeleteMachine(string id)
    {
        foreach (var s in sessions.Where(s => s.M.Id == id).ToList()) s.Close();
        Store.D.Machines.RemoveAll(m => m.Id == id);
        Save();
    }

    void ImportRdp(string name, string text)
    {
        var m = RdpFile.Import(text, Path.GetFileNameWithoutExtension(name));
        m.Profile = Store.D.Profile;
        m.Hue = Random.Shared.Next(360);
        SaveMachine(m, false);
        Toast($"Imported {m.Name}" + (m.Cred == null && RdpFile.User(text) is { Length: > 0 } u ? $" — add account '{u}' to the vault" : ""));
    }

    // ---------- files ----------
    protected IFs FsOf(string loc) => loc == "host" ? hostFs : (Find(loc) ?? throw new InvalidOperationException("Session has been closed.")).Fs();
    string LocName(string loc) => loc == "host" ? "This computer" : Find(loc)?.M.Name ?? "—";

    void List(int req, string k, string loc, string? path, string? cd)
    {
        Task.Run(() =>
        {
            object res;
            try
            {
                var fs = FsOf(loc);
                var p = path ?? fs.Home();
                if (cd == "..") p = fs.Parent(p);
                else if (!string.IsNullOrEmpty(cd)) p = p == "" ? cd : fs.Join(p, cd);
                var items = fs.List(p).OrderByDescending(i => i.Dir).ThenBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
                res = new { t = "list", req, k, loc, path = p, items };
            }
            catch (Exception ex) { res = new { t = "list", req, k, loc, path, error = Friendly(ex) }; }
            UI(() => Send(res));
        });
    }

    void Mkdir(int req, string k, string loc, string path, string name)
    {
        Task.Run(() =>
        {
            try { var fs = FsOf(loc); fs.MkDir(fs.Join(path, name)); UI(() => List(req, k, loc, path, null)); }
            catch (Exception ex) { Toast(Friendly(ex), "err"); }
        });
    }

    void DeleteItems(string loc, string path, string[] names)
    {
        Task.Run(() =>
        {
            try { var fs = FsOf(loc); foreach (var n in names) fs.Delete(fs.Join(path, n)); Toast($"Deleted: {names.Length}"); }
            catch (Exception ex) { Toast(Friendly(ex), "err"); }
            UI(() => Send(new { t = "changed", loc }));
        });
    }

    protected void StartTransfer(string fromLoc, string? fromPath, string name, string toLoc, string? toPath, bool move, IFs? fromFs = null, Action? after = null)
    {
        var t = new Transfer { Id = ++xferSeq, Name = name, FromLoc = fromLoc, ToLoc = toLoc, FromName = LocName(fromLoc), ToName = LocName(toLoc), Move = move };
        xfers[t.Id] = t;
        SendXfer(t);
        Task.Run(() =>
        {
            try
            {
                var src = fromFs ?? FsOf(fromLoc);
                var dst = FsOf(toLoc);
                Transfers.Run(t, src, src.Join(fromPath ?? src.Home(), name), dst, toPath ?? dst.Home());
                t.State = "done";
            }
            catch (OperationCanceledException) { t.State = "cancel"; }
            catch (Exception ex) { t.State = "fail"; t.Error = Friendly(ex); }
            after?.Invoke();
            UI(() =>
            {
                SendXfer(t);
                Send(new { t = "changed", loc = toLoc });
                if (move) Send(new { t = "changed", loc = fromLoc });
                if (t.State == "done") Toast($"{(move ? "Moved" : "Copied")} {name} → {t.ToName}");
                else if (t.State == "fail") Toast($"{name}: {t.Error}", "err");
            });
        });
    }

    /// <summary>Files from the desktop file manager (dropped on a tab, the remote picture or a file pane).</summary>
    public void DropFiles(string[] paths, string toLoc, string? toPath, bool move)
    {
        static string Clean(string p) => p.TrimEnd('\\', '/');
        void Go() { foreach (var p in paths) StartTransfer("host", Path.GetDirectoryName(Clean(p)), Path.GetFileName(Clean(p)), toLoc, toPath, move); }
        if (toLoc == "host") { Go(); return; }
        Task.Run(() =>
        {
            try { FsOf(toLoc).Home(); }
            catch (Exception ex)
            {
                // no SMB/SFTP access to that machine: hand the files to the RDP clipboard instead
                UI(() =>
                {
                    SetClipboard(paths);
                    Toast($"No direct access ({Friendly(ex)}). The files are on the clipboard — paste with Ctrl+V in the session.", "err");
                });
                return;
            }
            UI(Go);
        });
    }

    static object Dto(Transfer t) => new
    {
        t.Id, t.Name, from = t.FromName, to = t.ToName, t.FromLoc, t.ToLoc, t.Move, t.Total,
        done = Interlocked.Read(ref t.Done), state = t.State, t.Error, ms = Environment.TickCount64 - t.Started,
    };
    void SendXfer(Transfer t) => Send(new { t = "xfers", list = new[] { Dto(t) } });

    void PushXfers()
    {
        var run = xfers.Values.Where(t => t.State == "run").Select(Dto).ToList();
        if (run.Count > 0) Send(new { t = "xfers", list = run });
    }

    // ---------- shared clipboard ----------
    /// <summary>The clipboard changed; the focused session (or this PC) is the source. Call on the UI thread.</summary>
    protected void AddClip(string type, string text, string[]? names, string? img, object? payload, string src)
    {
        var key = type + text + string.Join("|", names ?? []);
        if (key == lastClipText) return;
        if (payload is string p && copiedSecrets.Contains(Hash(p))) return; // vault passwords never enter the history
        lastClipText = key;
        var id = ++clipSeq;
        if (payload != null) clipStore[id] = payload;
        foreach (var old in clipStore.Keys.Where(k => k <= id - 40).ToList()) clipStore.Remove(old);
        Send(new { t = "clip", e = new { id, type, text, names, img, src, t = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), reusable = payload != null } });
    }

    void ClipUse(int id)
    {
        if (!clipStore.TryGetValue(id, out var p)) { Toast("Files from the session must be copied again in the source session.", "err"); return; }
        ignoreClipUntil = Environment.TickCount64 + 500;
        SetClipboard(p);
        Toast("Clipboard set — Ctrl+V on this computer or in any session");
    }

    // ---------- vault ----------
    DateTime verifiedUntil;
    readonly HashSet<string> copiedSecrets = [];
    static string Hash(string s) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(s)));

    async Task UseCred(string id, bool copy)
    {
        var c = Store.D.Creds.FirstOrDefault(x => x.Id == id);
        if (c == null) return;
        if (DateTime.UtcNow >= verifiedUntil)
        {
            if (!await Verify()) return;
            verifiedUntil = DateTime.UtcNow.AddMinutes(5);
        }
        var pass = Store.Unprotect(c.Pass);
        if (!copy) { Send(new { t = "cred", id, pass }); return; }
        ignoreClipUntil = Environment.TickCount64 + 600; // never lands in the clipboard history
        copiedSecrets.Add(Hash(pass));
        SetClipboard(pass);
        Toast("Password copied — the clipboard will be cleared in 30 s");
        _ = Task.Delay(30000).ContinueWith(_ => UI(() =>
        {
            ignoreClipUntil = Environment.TickCount64 + 600;
            try { ClearClipboardIf(pass); } catch { }
        }));
    }

    // ---------- updates ----------
    (string version, string url, string? sha256)? update;
    bool updateChecked;

    async void CheckUpdate()
    {
        if (update is { } known) { Send(new { t = "update", version = known.version, current = Updater.Current }); return; } // UI reloaded
        if (updateChecked || Environment.GetEnvironmentVariable("MAGICREMOTE_NOUPDATE") == "1") return;
        updateChecked = true;
        try
        {
            update = await Updater.Check();
            if (update is { } u) UI(() => Send(new { t = "update", version = u.version, current = Updater.Current }));
        }
        catch (Exception e) { Log(e); } // offline or rate-limited: try again next start
    }

    async Task DoUpdate()
    {
        if (update is not { } u) return;
        Toast($"Downloading MagicRemote {u.version}…");
        try { await Updater.Apply(u.url, u.sha256); }
        catch (Exception e) { Log(e); Toast("Update failed: " + Friendly(e), "err"); return; }
        UI(() => { Send(new { t = "quit" }); Quit(); });
    }

    // ---------- reachability & Wake-on-LAN ----------
    protected async void CheckReach()
    {
        if (!ready) return;
        var res = await Task.WhenAll(Store.D.Machines.ToList().Select(async m =>
        {
            try
            {
                using var tc = new TcpClient();
                using var cts = new CancellationTokenSource(1500);
                await tc.ConnectAsync(m.Host, m.Port, cts.Token);
                return (m.Id, true);
            }
            catch { return (m.Id, false); }
        }));
        UI(() => Send(new { t = "reach", map = res.ToDictionary(r => r.Item1, r => r.Item2) }));
    }

    void Wake(string id)
    {
        var m = Store.D.Machines.FirstOrDefault(x => x.Id == id);
        var mac = Convert.FromHexString(new string((m?.Mac ?? "").Where(Uri.IsHexDigit).ToArray()));
        if (mac.Length != 6) { Toast("Set the MAC address in the machine editor to use Wake-on-LAN.", "err"); return; }
        var packet = Enumerable.Repeat((byte)0xFF, 6).Concat(Enumerable.Repeat(mac, 16).SelectMany(b => b)).ToArray();
        using var udp = new UdpClient { EnableBroadcast = true };
        udp.Send(packet, packet.Length, new IPEndPoint(IPAddress.Broadcast, 9));
        Toast($"Sent magic packet to {m!.Name}");
    }

    public static string Friendly(Exception e) => e switch
    {
        System.ComponentModel.Win32Exception { NativeErrorCode: 5 or 86 or 1326 } => "SMB access denied — the account needs access to the share (default C$) or set a custom share path in the editor.",
        System.ComponentModel.Win32Exception { NativeErrorCode: 53 or 67 or 1231 } => "SMB share unavailable (port 445 blocked?).",
        SshAuthenticationException => "SSH: credentials rejected.",
        SocketException s => $"Connection failed: {s.Message}",
        UnauthorizedAccessException => "No permission for this path.",
        AggregateException a when a.InnerException != null => Friendly(a.InnerException),
        _ => e.Message,
    };

    public int LiveSessions => sessions.Count(s => s.State != "disconnected");

    public void Shutdown()
    {
        tick?.Dispose(); reach?.Dispose();
        foreach (var t in xfers.Values) t.Cts.Cancel();
        foreach (var s in sessions.ToList()) s.Dispose();
        sessions.Clear();
    }
}
