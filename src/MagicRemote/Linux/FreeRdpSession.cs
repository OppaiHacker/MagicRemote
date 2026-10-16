using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;

namespace MagicRemote;

/// <summary>
/// RDP on Linux: FreeRDP 3 (xfreerdp3 / sdl-freerdp3) in its own window. Arguments, password included,
/// go through stdin (/args-from:stdin), never the command line where other users could read them.
/// </summary>
public sealed class FreeRdpSession(App f, Machine m) : Session(f, m)
{
    public override string Kind => "rdp";
    public override bool External => true;
    Process? proc;
    Rectangle stage;
    readonly List<string> errors = [];
    int gen;

    [DllImport("libc", SetLastError = true)] static extern int kill(int pid, int sig);

    public static string? Binary() =>
        Environment.GetEnvironmentVariable("MAGICREMOTE_FREERDP") is { Length: > 0 } b ? b
        : new[] { "xfreerdp3", "sdl-freerdp3", "xfreerdp", "sdl-freerdp" }.Select(Tool.Find).FirstOrDefault(p => p != null);

    public override void Place(Rectangle s, bool show) { if (s.Width > 0) stage = s; }

    public override void Start()
    {
        Set("connecting");
        var exe = Binary();
        if (exe == null) { Fail("FreeRDP 3 not found — install the freerdp package (xfreerdp3 or sdl-freerdp3)."); return; }
        errors.Clear();
        var my = ++gen;
        var psi = new ProcessStartInfo(exe) { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        psi.ArgumentList.Add("/args-from:stdin");
        try { proc = Process.Start(psi)!; }
        catch (Exception e) { Fail("FreeRDP: " + e.Message); return; }
        var p = proc;
        foreach (var a in Args(M, Scale, stage, Store.CredOf(M), Store.PasswordOf(M))) p.StandardInput.WriteLine(a);
        p.StandardInput.Close();
        void Read(StreamReader r) => Task.Run(() =>
        {
            string? line;
            while ((line = r.ReadLine()) != null)
            {
                var l = line;
                F.UI(() => { if (my == gen) OnLine(l); });
            }
        });
        Read(p.StandardOutput);
        Read(p.StandardError);
        // fallback if the log lines below never show up (other FreeRDP builds): alive after the handshake window = connected
        _ = Task.Delay(6000).ContinueWith(_ => F.UI(() => { if (my == gen && !p.HasExited && State == "connecting") Set("connected"); }));
        Task.Run(() =>
        {
            p.WaitForExit();
            var code = p.ExitCode;
            F.UI(() => { if (my == gen) Exited(code); });
        });
    }

    /// <summary>Log lines enabled by /log-filters: gdi_init = picture is up; client_auto_reconnect_ex = network drop and retries.</summary>
    void OnLine(string line)
    {
        if (line.Contains("[ERROR]") || line.Contains("[FATAL]")) { errors.Add(line); if (errors.Count > 40) errors.RemoveAt(0); }
        if (line.Contains("[gdi_init_ex]") && State != "connected" && !closing) Set("connected");
        else if (line.Contains("[client_auto_reconnect_ex]") && line.Contains("Network disconnect")) Set("reconnecting");
    }

    void Exited(int code)
    {
        proc = null;
        if (closing) { F.Remove(this); return; }
        // 0 / 1 / 2 / 11 / 12: user closed the window, logged off or disconnected from the server side
        if (code is 0 or 1 or 2 or 11 or 12 && State == "connected") { Error = ExitText(code); Set("disconnected"); return; }
        Fail(ExitText(code) + (Detail() is { Length: > 0 } d ? "\n" + d : ""));
    }

    // the ERRCONNECT_/ERRINFO_ codes say why; the rest of FreeRDP's error output is mostly noise
    string Detail() => string.Join("\n", (errors.Where(e => e.Contains("ERRCONNECT_") || e.Contains("ERRINFO_")) is var key && key.Any() ? key : errors).TakeLast(2)
        .Select(e => e.Contains("]: ") ? e[(e.IndexOf("]: ") + 3)..] : e).Distinct());

    void Fail(string error)
    {
        Error = error;
        Set("disconnected");
        F.Toast($"{M.Name}: {error.Split('\n')[0]}", "err");
    }

    /// <summary>FreeRDP client exit codes (freerdp/client.h, FREERDP_CLIENT_EXIT_*).</summary>
    public static string ExitText(int code) => code switch
    {
        0 => "Disconnected",
        1 => "Disconnected by server",
        2 => "Logged off",
        3 => "Disconnected due to inactivity",
        4 => "Logon timed out",
        5 => "Session taken over by another connection",
        7 => "Server refused the connection",
        9 => "No permission to log on via RDP",
        10 => "Server requires fresh credentials",
        11 => "Disconnected by user",
        12 => "Logged off by user",
        128 => "FreeRDP: invalid arguments (outdated version? FreeRDP 3 required)",
        130 => "RDP protocol error",
        131 => "Cannot connect to host",
        132 => "Credentials rejected",
        133 => "Security negotiation failed (NLA/TLS)",
        134 => "Logon failed — check the user name and password",
        135 => "Account locked out",
        136 => "FreeRDP: failed to prepare the connection",
        147 => "Server certificate rejected",
        155 => "Access denied — the account may not log on via RDP on this computer (Remote Desktop Users group)",
        _ => $"FreeRDP exited with code {code}",
    };

    /// <summary>Machine settings as FreeRDP 3 arguments (one per line, so values may contain spaces and commas).</summary>
    public static List<string> Args(Machine m, string scale, Rectangle stage, Cred? cred, string password)
    {
        var a = new List<string> { $"/v:{m.Host}", $"/port:{m.Port}", "/title:" + m.Name, "/wm-class:MagicRemote" };
        if (cred != null)
        {
            var (d, u) = Session.SplitUser(cred.User);
            a.Add("/u:" + u);
            if (d != "") a.Add("/d:" + d);
            if (password != "") a.Add("/p:" + password);
        }
        a.Add(m.CertCheck ? "/cert:tofu" : "/cert:ignore");
        int w = Math.Clamp(stage.Width, 640, 8192) & ~1, h = Math.Clamp(stage.Height, 480, 8192);
        if (scale == "dynamic") { a.Add($"/size:{w}x{h}"); a.Add("/dynamic-resolution"); }
        else if (scale == "fit") { a.Add("/size:1920x1080"); a.Add($"/smart-sizing:{w}x{h}"); }
        else a.Add("/size:1920x1080");
        a.Add($"/bpp:{m.Depth}");
        if (m.Mon == "all") a.Add("/multimon");
        a.Add(m.Clip ? "+clipboard" : "-clipboard");
        if (m.Drives) a.Add("+home-drive");
        a.Add(m.Audio ? "/sound" : "/audio-mode:2");
        if (m.Mic) a.Add("/microphone");
        if (m.Print) a.Add("/printer");
        if (m.Smart) a.Add("/smartcard");
        if (m.Usb) { a.Add("/usb:auto"); a.Add("/serial"); }
        if (m.Cam) a.Add("/video");
        if (m.Admin) a.Add("/admin");
        if (m.Auth == "restricted") a.Add("/restricted-admin");
        else if (m.Auth == "rcg") a.Add("/remoteGuard");
        if (m.Gateway != "") a.Add($"/gateway:g:{m.Gateway}");
        a.Add("/gfx:AVC444");
        a.Add(m.Lowlat ? "/network:lan" : "/network:auto");
        a.Add("/log-filters:com.freerdp.gdi:INFO,com.freerdp.client.common:INFO");
        a.Add("+auto-reconnect");
        a.Add("/auto-reconnect-max-retries:20");
        a.Add("/floatbar:sticky:off,default:visible,show:fullscreen");
        if (m.Rawin) a.Add("+grab-keyboard"); else a.Add("-grab-keyboard");
        if (m.RelMouse) a.Add("/mouse:relative:on,grab:on");
        // FreeRDP splits /app on commas: keep them out of the window name
        if (m.App != "") a.Add($"/app:program:{m.App},name:{m.Name.Replace(',', ' ')}" + (m.AppArgs != "" ? $",cmd:{m.AppArgs}" : ""));
        return a;
    }

    public override void Reconnect()
    {
        if (State != "disconnected") return;
        Error = null;
        Start();
    }

    /// <summary>The scaling mode is a launch option for FreeRDP: reconnect with it.</summary>
    public override void SetScale(string mode)
    {
        if (mode is not ("dynamic" or "fit" or "native") || mode == Scale) return;
        Scale = mode;
        F.PushSessions();
        if (proc == null) return;
        Stop();
        Start();
    }

    void Stop()
    {
        var p = proc;
        proc = null;
        gen++;
        if (p == null || p.HasExited) return;
        kill(p.Id, 15); // SIGTERM: FreeRDP disconnects cleanly
        if (!p.WaitForExit(3000)) try { p.Kill(); } catch { }
    }

    public override void Close()
    {
        closing = true;
        Stop();
        F.Remove(this);
    }

    public override void Dispose()
    {
        closing = true;
        Stop();
        base.Dispose();
    }
}
