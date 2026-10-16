using System.Drawing;
using System.Text;
using Renci.SshNet;

namespace MagicRemote;

public abstract class Session : IDisposable
{
    static int seq;
    public string Id { get; } = "s" + Interlocked.Increment(ref seq);
    public Machine M { get; set; }
    public string State { get; protected set; } = "connecting"; // connecting | connected | reconnecting | disconnected
    public string? Error { get; protected set; }
    public int Rtt { get; protected set; } = -1;
    public int W { get; protected set; }
    public int H { get; protected set; }
    public string Scale { get; protected set; }
    public string? Thumb { get; protected set; }
    public abstract string Kind { get; } // rdp | ssh | files
    protected readonly App F;
    /// <summary>The remote picture lives in its own window (FreeRDP on Linux), not inside the app.</summary>
    public virtual bool External => false;
    protected bool closing;
    readonly Lock fsGate = new();
    IFs? fs;

    protected Session(App f, Machine m) { F = f; M = m; Scale = m.Scale; }

    public abstract void Start();
    public virtual void Reconnect() { Error = null; Start(); }
    public virtual void Close() { closing = true; F.Remove(this); }
    public virtual void Place(Rectangle stage, bool show) { }
    public virtual void SetScale(string mode) { }
    public virtual void GoFullScreen() { }
    public virtual void SendCtrlAltDel() { }
    /// <summary>Thumbnail for the overview.</summary>
    public virtual void Snap() { }

    protected void Set(string state) { State = state; F.Relayout(); F.PushSessions(); }

    /// <summary>File system of this machine. Opens lazily, call from a background thread.</summary>
    public IFs Fs() { lock (fsGate) return fs ??= OpenFs(); }

    IFs OpenFs()
    {
        if (M.Proto == "ftp")
        {
            var c = Store.CredOf(M);
            return new FtpFs(M.Host, M.Port, c?.User ?? "anonymous", c is null ? "anonymous@" : Store.Unprotect(c.Pass), M.Ftps);
        }
        if (M.Proto is "ssh" or "sftp") return new SftpFs(Ssh.Info(M, M.Port));
        if (M.Os == "lnx") return new SftpFs(Ssh.Info(M, M.SshPort));
        return SmbFs();
    }

    IFs SmbFs()
    {
        var cred = Store.CredOf(M) ?? throw new InvalidOperationException("Select credentials from the vault to transfer files.");
        var user = SplitUser(cred.User).user;
        var root = M.Share != "" ? ShareRoot(M.Share) : $@"\\{M.Host}\C$";
        var home = M.Share != "" ? M.Share : $@"{root}\Users\{user}\Desktop";
        return F.OpenSmb(root, home, cred);
    }

    public static string ShareRoot(string unc)
    {
        var parts = unc.TrimStart('\\').Split('\\', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 ? $@"\\{parts[0]}\{parts[1]}" : unc;
    }

    public static (string domain, string user) SplitUser(string u)
    {
        var i = u.IndexOf('\\');
        return i > 0 ? (u[..i], u[(i + 1)..]) : ("", u);
    }

    public virtual void Dispose() { lock (fsGate) fs?.Dispose(); }
}

/// <summary>Interactive SSH shell rendered by xterm.js in the UI.</summary>
public sealed class SshSession(App f, Machine m) : Session(f, m)
{
    public override string Kind => "ssh";
    SshClient? client;
    ShellStream? shell;
    uint cols = 120, rows = 32;

    public override void Start()
    {
        Set("connecting");
        Task.Run(() =>
        {
            try
            {
                var info = Ssh.Info(M, M.Port);
                var cl = new SshClient(info) { KeepAliveInterval = TimeSpan.FromSeconds(30) };
                Ssh.Pin(cl, info.Host, info.Port);
                var t0 = Environment.TickCount64;
                cl.Connect();
                client = cl;
                shell = cl.CreateShellStream("xterm-256color", cols, rows, 0, 0, 1 << 16);
                F.UI(() => Set("connected"));
                var dec = Encoding.UTF8.GetDecoder();
                var buf = new byte[1 << 16];
                var chars = new char[(1 << 16) + 1];
                int n;
                while ((n = shell.Read(buf, 0, buf.Length)) > 0)
                {
                    int k = dec.GetChars(buf, 0, n, chars, 0);
                    F.TermOut(Id, new string(chars, 0, k));
                }
                F.UI(() => { if (!closing) { Error = "SSH session ended"; Set("disconnected"); } });
            }
            catch (Exception e)
            {
                F.UI(() => { if (!closing) { Error = App.Friendly(e); Set("disconnected"); } });
            }
        });
    }

    public void Input(string data)
    {
        try { shell?.Write(data); shell?.Flush(); } catch { }
    }

    public void Resize(uint c, uint r)
    {
        cols = Math.Clamp(c, 10, 1000); rows = Math.Clamp(r, 4, 500);
        try { shell?.ChangeWindowSize(cols, rows, 0, 0); } catch { }
    }

    public override void Dispose()
    {
        closing = true;
        try { shell?.Dispose(); client?.Dispose(); } catch { }
        base.Dispose();
    }
}

/// <summary>SFTP / FTP server opened as a file manager tab.</summary>
public sealed class FilesSession(App f, Machine m) : Session(f, m)
{
    public override string Kind => "files";

    public override void Start()
    {
        Set("connecting");
        Task.Run(() =>
        {
            try { Fs().Home(); F.UI(() => Set("connected")); }
            catch (Exception e) { F.UI(() => { Error = App.Friendly(e); Set("disconnected"); }); }
        });
    }
}
