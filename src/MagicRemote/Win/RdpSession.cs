using System.ComponentModel;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using MSTSCLib;

namespace MagicRemote;

/// <summary>The Microsoft Remote Desktop ActiveX control (mstscax.dll) — same engine as mstsc: UDP transport, AVC444, GPU decode.</summary>
sealed class RdpAx(string clsid) : AxHost(clsid)
{
    public object Ocx => GetOcx()!;
}

public sealed class RdpSession : Session
{
    FileDropTarget drops = null!;
    // newest first: MsRdpClient12 / 11 / 10 / 9 NotSafeForScripting (NotSafe = allows setting the password)
    static readonly string Clsid = new[]
    {
        "3f859aa3-c2d4-4faa-b0e4-fd0c9c4e5e3a", "1df7c823-b2d4-4b54-975a-f2ac5d7cf8b8",
        "a0c63c30-f08d-4ab4-907c-34905d770c7d", "8b918b82-7985-4c24-89df-c33ad2bbfbcd",
    }.First(CanCreate);

    // registered is not enough: some Windows builds register MsRdpClient12 but the class factory refuses it
    static bool CanCreate(string clsid)
    {
        try
        {
            var o = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid(clsid), true)!);
            Marshal.ReleaseComObject(o!);
            return true;
        }
        catch { return false; }
    }

    public override string Kind => "rdp";
    public override bool External => M.App != ""; // RemoteApp windows live on the desktop, not in the stage
    public readonly Panel Host;
    readonly RdpAx ax;
    readonly IMsRdpClient10 c;
    readonly System.Windows.Forms.Timer resize = new() { Interval = 160 };
    (int w, int h, int s) asked;
    readonly System.Windows.Forms.Timer settle = new() { Interval = 1200 };
    int retries, settles;
    bool launched; // RemoteApp started in this connection (auto-reconnect keeps it running)

    readonly Form form;

    public RdpSession(App f, Form form, Machine m, Rectangle stage) : base(f, m)
    {
        this.form = form;
        Host = new Panel { Bounds = stage.Width > 0 ? stage : new Rectangle(0, 0, 1280, 720), BackColor = Color.Black, AllowDrop = true };
        ax = new RdpAx(Clsid) { Dock = DockStyle.Fill };
        ((ISupportInitialize)ax).BeginInit();
        Host.Controls.Add(ax);
        form.Controls.Add(Host);
        Host.BringToFront();
        ((ISupportInitialize)ax).EndInit();
        c = (IMsRdpClient10)ax.Ocx;
        Host.Visible = false;
        resize.Tick += (_, _) => { resize.Stop(); ApplySize(); };
        settle.Tick += (_, _) => Settle();
        Hook();
        // files dropped from Explorer onto the remote picture (OLE walks up to this panel)
        Host.DragEnter += (_, e) => e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true
            ? (e.KeyState & 4) != 0 ? DragDropEffects.Move : DragDropEffects.Copy : DragDropEffects.None;
        drops = new FileDropTarget((files, move) => F.DropFiles(files, Id, null, move));
        Host.DragDrop += (_, e) => { if (e.Data?.GetData(DataFormats.FileDrop) is string[] files) F.DropFiles(files, Id, null, (e.KeyState & 4) != 0); };
    }

    public override void Start()
    {
        Configure(Store.PasswordOf(M));
        launched = false;
        Set("connecting");
        c.Connect();
    }

    void Configure(string password)
    {
        var m = M;
        c.Server = m.Host;
        var cred = Store.CredOf(m);
        if (cred != null) { var (d, u) = SplitUser(cred.User); c.Domain = d; c.UserName = u; }

        var ns = (IMsRdpClientNonScriptable5)c;
        if (password != "") ns.ClearTextPassword = password;
        ns.AllowPromptingForCredentials = true;
        ns.PromptForCredsOnClient = true;
        ns.NegotiateSecurityLayer = true;
        ns.ShowRedirectionWarningDialog = false;
        ns.WarnAboutClipboardRedirection = false;
        ns.WarnAboutSendingCredentials = false;
        ns.WarnAboutPrinterRedirection = false;
        ns.RedirectDynamicDrives = m.Drives;
        ns.RedirectDynamicDevices = m.Usb;
        ns.UseMultimon = m.Mon == "all";
        ns.ConnectionBarText = m.Name;

        var a = c.AdvancedSettings9;
        a.RDPPort = m.Port;
        a.EnableCredSspSupport = true;
        a.AuthenticationLevel = m.CertCheck ? 2u : 0u; // 2 = warn and let the user decide, 0 = connect without checking
        a.RedirectClipboard = m.Clip;   // text, images AND files, both directions; every session shares this PC's clipboard
        a.RedirectDrives = m.Drives;
        a.RedirectPrinters = m.Print;
        a.RedirectSmartCards = m.Smart;
        a.AudioRedirectionMode = m.Audio ? 0u : 2u;
        a.AudioCaptureRedirectionMode = m.Mic;
        a.AudioQualityMode = m.Lowlat ? 2u : 0u; // 2 = high quality, 0 = dynamic
        a.VideoPlaybackMode = 1;                 // multimedia redirection: video streams decoded here, not re-encoded as screen
        a.RedirectDirectX = true;
        a.RedirectDevices = m.Usb;
        a.RedirectPorts = m.Usb;
        a.RedirectPOSDevices = m.Usb;
        a.ConnectToAdministerServer = m.Admin;
        a.minInputSendInterval = 1;              // mouse moves leave every 1 ms (default 100 ms coalescing)
        a.EnableAutoReconnect = true;
        a.MaxReconnectAttempts = 20;
        // low latency: LAN profile = no adaptive throttling, max frame rate; detection still runs so RTT gets reported
        a.BandwidthDetection = true;
        a.NetworkConnectionType = (uint)(m.Lowlat ? 6 : 7);
        a.PerformanceFlags = 0x80 | 0x100; // font smoothing + desktop composition
        a.BitmapPersistence = 1;
        a.GrabFocusOnConnect = true;
        a.SmartSizing = Scale == "fit";
        a.EnableWindowsKey = 1;
        a.DisplayConnectionBar = true;
        a.ConnectionBarShowRestoreButton = true;
        a.ConnectionBarShowMinimizeButton = true;
        a.RelativeMouseMode = m.RelMouse;         // games / 3D: raw mouse deltas instead of absolute positions
        c.SecuredSettings3.KeyboardHookMode = m.Rawin ? 1 : 2;
        c.ColorDepth = m.Depth;

        // RemoteApp: only the program's own windows appear here, as separate windows (server needs the app published or unlisted programs allowed)
        var rp = c.RemoteProgram2;
        rp.RemoteProgramMode = m.App != "";
        if (m.App != "")
        {
            rp.RemoteApplicationProgram = m.App;
            rp.RemoteApplicationName = m.Name;
            rp.RemoteApplicationArgs = m.AppArgs;
        }

        if (m.Gateway != "")
        {
            var t = c.TransportSettings4;
            t.GatewayHostname = m.Gateway;
            t.GatewayUsageMethod = 1;
            t.GatewayProfileUsageMethod = 1;
            t.GatewayCredSharing = 1;
        }

        var (w, h) = Desired();
        c.DesktopWidth = w;
        c.DesktopHeight = h;
        var (ds, dev) = ScaleFactors();
        var ext = (IMsRdpExtendedSettings)c;
        object v1 = (uint)ds, v2 = (uint)dev;
        ext.set_Property("DesktopScaleFactor", ref v1);
        ext.set_Property("DeviceScaleFactor", ref v2);
        if (m.Auth != "")
        {
            object t = true;
            // Restricted Admin: no credentials ever reach the host; Remote Credential Guard: Kerberos requests are redirected back here
            ext.set_Property(m.Auth == "rcg" ? "RedirectedAuthentication" : "RestrictedLogon", ref t);
            ext.set_Property("DisableCredentialsDelegation", ref t);
        }

        if (m.Usb)
        {
            var dc = ns.DeviceCollection;
            dc.RescanDevices(false);
            for (uint i = 0; i < dc.DeviceCount; i++) dc.get_DeviceByIndex(i).RedirectionState = true;
        }
        var cam = ((IMsRdpClientNonScriptable7)c).CameraRedirConfigCollection;
        cam.RedirectByDefault = m.Cam;
        cam.EncodeVideo = true;
        cam.EncodingQuality = CameraRedirEncodingQuality.encodingQualityHigh;
        if (m.Cam) cam.Rescan();
    }

    void Hook()
    {
        var ev = (IMsTscAxEvents_Event)c;
        ev.OnConnecting += () => Set("connecting");
        // show the picture as soon as the transport is up (xrdp and others never send the logon-info PDU behind OnLoginComplete)
        // mstscax draws into its own child windows, which are not drop targets: register ours on them
        ev.OnConnected += () => { Set("connected"); Debounce(); Host.BeginInvoke(() => Native.AcceptFileDrops(ax.Handle, drops)); };
        ev.OnLoginComplete += () => { asked = default; Set("connected"); Debounce(); StartApp(); };
        ev.OnRemoteProgramResult += (name, result, _) =>
        {
            if (result != RemoteProgramResult.remoteAppResultOk) F.Toast($"RemoteApp {name}: {result} — publish the application on the server or allow unpublished programs.", "err");
        };
        ev.OnDisconnected += OnDisconnected;
        ev.OnNetworkStatusChanged += (_, _, rtt) => { Rtt = rtt; F.SendNet(this); };
        ev.OnAutoReconnecting2 += (_, _, _, _) => Set("reconnecting");
        ev.OnAutoReconnected += () => Set("connected");
        ev.OnRemoteDesktopSizeChange += (w, h) =>
        {
            W = w; H = h;
            F.PushSessions();
        };
        ev.OnFocusReleased += dir => F.Cycle(this, dir);
        ev.OnEnterFullScreenMode += () => { var b = Screen.FromControl(form).Bounds; Resize(b.Width, b.Height); };
        ev.OnLeaveFullScreenMode += () => { asked = default; ApplySize(); F.Relayout(); };
        ev.OnRequestContainerMinimize += () => form.WindowState = FormWindowState.Minimized;
        ev.OnFatalError += code => { Error = $"Fatal RDP control error ({code})"; Set("disconnected"); };
    }

    // the RemoteApplication* settings only name the app: start it once per connection (auto-reconnect keeps it running)
    void StartApp()
    {
        if (launched || M.App == "") return;
        launched = true;
        try { c.RemoteProgram2.ServerStartProgram(M.App, "", "%HOMEDRIVE%%HOMEPATH%", true, M.AppArgs, false); }
        catch (Exception e) { F.Toast($"RemoteApp {M.App}: {e.Message}", "err"); }
    }

    void OnDisconnected(int reason)
    {
        if (closing) { F.Remove(this); return; }
        string desc = "";
        try { desc = c.GetErrorDescription((uint)reason, (uint)c.ExtendedDisconnectReason); } catch { }
        // 1 = local, not an error: while connecting that is Cancel in the "credentials did not work" prompt (the control calls it an internal error)
        Error = reason == 1 ? (State == "connecting" ? "Logon cancelled — credentials rejected? Check the password in the vault." : "Disconnected")
              : reason == 2055 ? "Logon failed — check the user name and password in the vault."
              : string.IsNullOrWhiteSpace(desc) ? $"Disconnected (code {reason})" : desc;
        asked = default;
        retries = 0;
        Set("disconnected");
        F.Toast($"{M.Name}: {Error}", "err");
    }

    public override void Reconnect()
    {
        if (State != "disconnected") return;
        Error = null;
        Start();
    }

    public override void Close()
    {
        closing = true;
        if (c.Connected == 0) { F.Remove(this); return; }
        try { c.Disconnect(); } catch { F.Remove(this); return; }
        // the control normally answers with OnDisconnected; don't leave a zombie tab if it doesn't
        var t = new System.Windows.Forms.Timer { Interval = 4000 };
        t.Tick += (_, _) => { t.Dispose(); F.Remove(this); };
        t.Start();
    }

    public override void Place(Rectangle stage, bool show)
    {
        show &= State is "connected" or "reconnecting" && stage.Width > 0 && !c.FullScreen && !External;
        if (stage.Width > 0 && Host.Bounds != stage) { Host.Bounds = stage; Debounce(); }
        if (Host.Visible == show) return;
        Host.Visible = show;
        if (show) { Host.BringToFront(); Debounce(); }
    }

    void Debounce()
    {
        if (Scale != "dynamic" || External) return;
        resize.Stop(); resize.Start();
        settles = 0; settle.Stop(); settle.Start();
    }

    /// <summary>Some servers (xrdp) silently drop a resize sent right after connect and round sizes: re-send until the remote matches the window.</summary>
    void Settle()
    {
        var (w, h) = Desired();
        if (State != "connected" || Scale != "dynamic" || c.FullScreen || W == 0 || (Math.Abs(W - w) <= 4 && Math.Abs(H - h) <= 4) || settles++ >= 5) { settle.Stop(); return; }
        asked = default;
        ApplySize();
    }

    public void FocusRemote() { if (Host.Visible) ax.Focus(); }

    public override void SetScale(string mode)
    {
        if (mode is not ("dynamic" or "fit" or "native")) return;
        Scale = mode;
        c.AdvancedSettings9.SmartSizing = mode == "fit";
        asked = default;
        ApplySize();
        F.PushSessions();
    }

    public override void GoFullScreen() { if (State == "connected") c.FullScreen = true; }

    (int w, int h) Desired() => Scale == "dynamic"
        ? (Math.Clamp(Host.Width, 200, 8192) & ~1, Math.Clamp(Host.Height, 200, 8192))
        : (1920, 1080);

    (int desktop, int device) ScaleFactors()
    {
        if (!M.Hidpi) return (100, 100);
        var s = Math.Clamp(Host.DeviceDpi * 100 / 96, 100, 500);
        return (s, s >= 175 ? 180 : s >= 125 ? 140 : 100);
    }

    void ApplySize() { var (w, h) = Desired(); Resize(w, h); }

    /// <summary>Dynamic resolution (RDP Display Control channel): the host re-renders at the new size, no scaling blur.</summary>
    void Resize(int w, int h)
    {
        if (State != "connected") return;
        var (ds, dev) = ScaleFactors();
        if (asked == (w, h, ds)) return;
        asked = (w, h, ds);
        try
        {
            uint mmW = (uint)(w * 25.4 / Host.DeviceDpi), mmH = (uint)(h * 25.4 / Host.DeviceDpi);
            c.UpdateSessionDisplaySettings((uint)w, (uint)h, mmW, mmH, 0, (uint)ds, (uint)dev);
            retries = 0;
        }
        catch
        {
            asked = default;
            // the Display Control channel opens a moment after connect: retry for ~8 s
            if (retries++ < 50) { resize.Stop(); resize.Start(); return; }
            // still refused: the server has no Display Control, so scale the picture instead
            if (Scale == "dynamic") { Scale = "fit"; c.AdvancedSettings9.SmartSizing = true; F.PushSessions(); }
        }
    }

    // straight into the session through the control (mRemoteNG does the same): synthesized local keys depend on focus and can leak to this desktop
    public override void SendCtrlAltDel()
    {
        FocusRemote();
        ((IRdpKeys)c).SendKeys(6, [0, 0, 0, -1, -1, -1], [0x1D, 0x38, 0x53, 0x53, 0x38, 0x1D]);
    }

    /// <summary>IMsRdpClientNonScriptable with SendKeys declared as arrays (the stock interop marshals only the first element).</summary>
    [ComImport, Guid("2F079C4C-87B2-4AFD-97AB-20CDB43038AE"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IRdpKeys
    {
        void _1(); void _2(); void _3(); void _4(); void _5(); void _6(); void _7(); void _8(); void _9(); void _10(); void _11(); // IMsTscNonScriptable + NotifyRedirectDeviceChange
        void SendKeys(int numKeys, [MarshalAs(UnmanagedType.LPArray)] short[] keyUp,
                      [MarshalAs(UnmanagedType.LPArray)] int[] keyData);
    }

    /// <summary>
    /// Diagnostics (MAGICREMOTE_DIAG=1): real key press (same path as the keyboard) → remote repaint → first changed pixel on this screen.
    /// Measures the whole chain: client send, network, server render + encode, client decode, desktop composition.
    /// </summary>
    public async Task<double[]> ProbeLatency(int count, byte scancode)
    {
        FocusRemote();
        await Task.Delay(300);
        var at = Host.PointToScreen(new Point(Host.Width / 2, Host.Height / 2));
        var res = new List<double>();
        for (int i = 0; i < count; i++)
        {
            if (!Native.OwnsForeground()) break; // never type into someone else's window
            var before = Pixel(at);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Native.Key(scancode, false);
            Native.Key(scancode, true);
            var ms = await Task.Run(() =>
            {
                while (sw.Elapsed.TotalMilliseconds < 2000)
                    if (Pixel(at) != before) return sw.Elapsed.TotalMilliseconds;
                return -1;
            });
            if (ms > 0) res.Add(ms);
            await Task.Delay(120);
        }
        return res.ToArray();
    }

    // diagnostics: the window really under the picture's center accepts drops, and our target turns an Explorer-style data object into a transfer
    public object DiagDrop(string file)
    {
        var at = Host.PointToScreen(new Point(Host.Width / 2, Host.Height / 2));
        IOleDropTarget t = drops;
        var data = (System.Runtime.InteropServices.ComTypes.IDataObject)new DataObject(DataFormats.FileDrop, new[] { file });
        int enter = 3, drop = 3;
        t.DragEnter(data, 1, 0, ref enter);
        t.Drop(data, 1, 0, ref drop);
        return new { registered = Native.DropTargetAt(at), enter, drop };
    }

    static int Pixel(Point p)
    {
        using var b = new Bitmap(1, 1);
        using (var g = Graphics.FromImage(b)) g.CopyFromScreen(p, Point.Empty, new Size(1, 1));
        return b.GetPixel(0, 0).ToArgb();
    }

    /// <summary>Thumbnail for the overview; only possible while the session is on screen.</summary>
    public override void Snap()
    {
        if (!Host.Visible || Host.Width < 16 || Host.Height < 16) return;
        using var full = new Bitmap(Host.Width, Host.Height);
        using (var g = Graphics.FromImage(full)) g.CopyFromScreen(Host.PointToScreen(Point.Empty), Point.Empty, Host.Size);
        int tw = 560, th = Math.Max(1, Host.Height * tw / Host.Width);
        using var small = new Bitmap(full, tw, th);
        using var ms = new MemoryStream();
        small.Save(ms, ImageFormat.Jpeg);
        Thumb = "data:image/jpeg;base64," + Convert.ToBase64String(ms.ToArray());
    }

    public override void Dispose()
    {
        resize.Dispose();
        settle.Dispose();
        Host.Hide();
        // the control finishes its own teardown after OnDisconnected; destroying it before that deadlocks (InPlaceDeactivate never returns)
        var t = new System.Windows.Forms.Timer { Interval = 3000 };
        t.Tick += (_, _) => { t.Dispose(); Host.Dispose(); };
        t.Start();
        base.Dispose();
    }
}

