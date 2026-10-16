using FluentFTP;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace MagicRemote;

public sealed record FsItem(string Name, long Size, bool Dir, long Mtime);

/// <summary>One file system a session exposes: local disk / SMB share, SFTP, or FTP.</summary>
public interface IFs : IDisposable
{
    string Home();
    List<FsItem> List(string path);
    Stream OpenRead(string path);
    Stream Create(string path);
    bool Exists(string path);
    bool IsDir(string path);
    void MkDir(string path);
    void Delete(string path);
    bool TryMove(string from, string to); // cheap rename inside the same file system
    string Join(string dir, string name);
    string Parent(string path);
}

/// <summary>Local disk and UNC (\\host\share) paths.</summary>
public sealed class LocalFs(string home) : IFs
{
    public string Home() => home;

    public List<FsItem> List(string path)
    {
        if (path == "")
            return DriveInfo.GetDrives().Where(d => d.IsReady).Select(d => new FsItem(d.Name, 0, true, 0)).ToList();
        var di = new DirectoryInfo(path);
        return di.EnumerateFileSystemInfos()
            .Where(f => (f.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0)
            .Select(f => new FsItem(f.Name, f is FileInfo fi ? fi.Length : 0, f is DirectoryInfo, new DateTimeOffset(f.LastWriteTimeUtc).ToUnixTimeMilliseconds()))
            .ToList();
    }

    public Stream OpenRead(string path) => new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20);
    public Stream Create(string path) => new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 20);
    public bool Exists(string path) => File.Exists(path) || Directory.Exists(path);
    public bool IsDir(string path) => Directory.Exists(path);
    public void MkDir(string path) => Directory.CreateDirectory(path);
    public void Delete(string path) { if (Directory.Exists(path)) Directory.Delete(path, true); else File.Delete(path); }

    public bool TryMove(string from, string to)
    {
        if (!string.Equals(Path.GetPathRoot(from), Path.GetPathRoot(to), StringComparison.OrdinalIgnoreCase)) return false;
        try { if (Directory.Exists(from)) Directory.Move(from, to); else File.Move(from, to); }
        catch (IOException) when (!OperatingSystem.IsWindows() && Directory.Exists(from)) { return false; } // another mount (EXDEV): copy + delete instead
        return true;
    }

    public string Join(string dir, string name) => Path.Combine(dir, name);

    public string Parent(string path)
    {
        if (path == "") return "";
        if (!OperatingSystem.IsWindows()) return Path.GetDirectoryName(path.TrimEnd('/')) is { Length: > 0 } up ? up : "/";
        var p = Path.GetDirectoryName(path.TrimEnd('\\'));
        if (p != null) return p;
        return path.StartsWith(@"\\") ? path : ""; // UNC share root stays put; drive root goes to drive list
    }

    public void Dispose() { }
}

public static class Ssh
{
    /// <summary>Trust-on-first-use host key check, persisted in the store.</summary>
    public static void Pin(BaseClient client, string host, int port)
    {
        client.HostKeyReceived += (_, e) =>
        {
            var key = $"{host}:{port}";
            lock (Store.D)
            {
                if (Store.D.KnownHosts.TryGetValue(key, out var known)) { e.CanTrust = known == e.FingerPrintSHA256; return; }
                Store.D.KnownHosts[key] = e.FingerPrintSHA256;
                e.CanTrust = true;
            }
            Store.Save();
        };
    }

    /// <summary>Auth from the vault credential: private key (passphrase = vault password) and/or password / keyboard-interactive.</summary>
    public static ConnectionInfo Info(Machine m, int port)
    {
        var cred = Store.CredOf(m) ?? throw new InvalidOperationException("Select credentials from the vault in the machine editor.");
        var user = Session.SplitUser(cred.User).user;
        var pass = Store.Unprotect(cred.Pass);
        var auth = new List<AuthenticationMethod>();
        if (m.KeyFile != "") auth.Add(new PrivateKeyAuthenticationMethod(user, new PrivateKeyFile(m.KeyFile, pass == "" ? null : pass)));
        if (pass != "")
        {
            auth.Add(new PasswordAuthenticationMethod(user, pass));
            var ki = new KeyboardInteractiveAuthenticationMethod(user);
            ki.AuthenticationPrompt += (_, e) => { foreach (var p in e.Prompts) p.Response = pass; };
            auth.Add(ki);
        }
        if (auth.Count == 0) throw new InvalidOperationException("No password or private key for SSH.");
        return new ConnectionInfo(m.Host, port, user, auth.ToArray()) { Timeout = TimeSpan.FromSeconds(10) };
    }
}

public sealed class SftpFs : IFs
{
    readonly SftpClient c;

    public SftpFs(ConnectionInfo info)
    {
        c = new SftpClient(info);
        Ssh.Pin(c, info.Host, info.Port);
        try { c.Connect(); }
        catch (SshConnectionException e) when (e.Message.Contains("Key exchange")) { throw new InvalidOperationException("SSH host key has changed — possible MITM attack. Remove the entry from known hosts if the change is expected.", e); }
    }

    public string Home()
    {
        var wd = c.WorkingDirectory;
        var desk = Join(wd, "Desktop");
        return c.Exists(desk) ? desk : wd;
    }

    public List<FsItem> List(string path) => c.ListDirectory(path)
        .Where(f => f.Name is not "." and not ".." && !f.Name.StartsWith('.'))
        .Select(f => new FsItem(f.Name, f.IsDirectory ? 0 : f.Length, f.IsDirectory, new DateTimeOffset(f.LastWriteTimeUtc).ToUnixTimeMilliseconds()))
        .ToList();

    public Stream OpenRead(string path) => c.OpenRead(path);
    public Stream Create(string path) => c.Open(path, FileMode.CreateNew, FileAccess.Write);
    public bool Exists(string path) => c.Exists(path);
    public bool IsDir(string path) => c.GetAttributes(path).IsDirectory;
    public void MkDir(string path) => c.CreateDirectory(path);

    public void Delete(string path)
    {
        if (!IsDir(path)) { c.DeleteFile(path); return; }
        foreach (var f in c.ListDirectory(path).Where(f => f.Name is not "." and not ".."))
            Delete(f.FullName);
        c.DeleteDirectory(path);
    }

    public bool TryMove(string from, string to) { c.RenameFile(from, to); return true; }
    public string Join(string dir, string name) => dir.TrimEnd('/') + "/" + name;
    public string Parent(string path) { var t = path.TrimEnd('/'); var i = t.LastIndexOf('/'); return i <= 0 ? "/" : t[..i]; }
    public void Dispose() => c.Dispose();
}

/// <summary>FTP / FTPS. FluentFTP clients are not thread-safe, so every stream gets its own connection.</summary>
public sealed class FtpFs(string host, int port, string user, string password, bool tls) : IFs
{
    readonly Lock gate = new();
    FtpClient? meta;

    FtpClient NewClient()
    {
        var c = new FtpClient(host, user, password, port);
        c.Config.EncryptionMode = tls ? FtpEncryptionMode.Explicit : FtpEncryptionMode.None;
        c.Config.DataConnectionType = FtpDataConnectionType.AutoPassive;
        c.Config.ConnectTimeout = 10000;
        // FTPS: a CA-trusted certificate passes; a self-signed one is pinned on first use (like SSH host keys)
        c.ValidateCertificate += (_, e) =>
        {
            if (e.PolicyErrors == System.Net.Security.SslPolicyErrors.None) { e.Accept = true; return; }
            var fp = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(e.Certificate.GetRawCertData()));
            var key = $"ftps://{host}:{port}";
            lock (Store.D)
            {
                if (Store.D.KnownHosts.TryGetValue(key, out var known)) { e.Accept = known == fp; return; }
                Store.D.KnownHosts[key] = fp;
                e.Accept = true;
            }
            Store.Save();
        };
        try { c.Connect(); }
        catch (System.Security.Authentication.AuthenticationException ex) { c.Dispose(); throw new InvalidOperationException("FTPS certificate has changed — possible MITM attack. Remove the entry from known hosts if the change is expected.", ex); }
        return c;
    }

    T Meta<T>(Func<FtpClient, T> op)
    {
        lock (gate)
        {
            if (meta is not { IsConnected: true }) { meta?.Dispose(); meta = NewClient(); }
            return op(meta);
        }
    }

    public string Home() => Meta(c => c.GetWorkingDirectory());

    public List<FsItem> List(string path) => Meta(c => c.GetListing(path)
        .Where(f => f.Type != FtpObjectType.Link && !f.Name.StartsWith('.'))
        .Select(f => new FsItem(f.Name, f.Type == FtpObjectType.Directory ? 0 : f.Size, f.Type == FtpObjectType.Directory, f.Modified == DateTime.MinValue ? 0 : new DateTimeOffset(f.Modified).ToUnixTimeMilliseconds()))
        .ToList());

    public Stream OpenRead(string path) { var c = NewClient(); return new Owned(c.OpenRead(path), c); }
    public Stream Create(string path) { var c = NewClient(); return new Owned(c.OpenWrite(path), c); }
    public bool Exists(string path) => Meta(c => c.FileExists(path) || c.DirectoryExists(path));
    public bool IsDir(string path) => Meta(c => c.DirectoryExists(path));
    public void MkDir(string path) => Meta(c => c.CreateDirectory(path));
    public void Delete(string path) => Meta(c => { if (c.DirectoryExists(path)) c.DeleteDirectory(path); else c.DeleteFile(path); return 0; });
    public bool TryMove(string from, string to) => Meta(c => c.MoveFile(from, to) || c.MoveDirectory(from, to));
    public string Join(string dir, string name) => dir.TrimEnd('/') + "/" + name;
    public string Parent(string path) { var t = path.TrimEnd('/'); var i = t.LastIndexOf('/'); return i <= 0 ? "/" : t[..i]; }
    public void Dispose() { lock (gate) meta?.Dispose(); }

    /// <summary>Stream that also closes the FTP connection it came from.</summary>
    sealed class Owned(Stream inner, IDisposable owner) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => inner.CanWrite;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => throw new NotSupportedException(); }
        public override void Flush() => inner.Flush();
        public override int Read(byte[] b, int o, int n) => inner.Read(b, o, n);
        public override void Write(byte[] b, int o, int n) => inner.Write(b, o, n);
        public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException();
        public override void SetLength(long v) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) { inner.Dispose(); owner.Dispose(); } base.Dispose(disposing); }
    }
}

public sealed class Transfer
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public string FromName { get; init; } = "";
    public string ToName { get; init; } = "";
    public string FromLoc { get; init; } = "";
    public string ToLoc { get; init; } = "";
    public bool Move { get; init; }
    public long Total { get; set; }
    public long Done;
    public string State { get; set; } = "run"; // run | done | fail | cancel
    public string? Error { get; set; }
    public long Started { get; } = Environment.TickCount64;
    internal readonly CancellationTokenSource Cts = new();
}

public static class Transfers
{
    /// <summary>Copies (or moves) one file or directory tree from src to dstDir. Runs on the caller's thread.</summary>
    public static void Run(Transfer t, IFs src, string srcPath, IFs dst, string dstDir)
    {
        var ct = t.Cts.Token;
        var target = UniqueName(dst, dstDir, t.Name);
        if (t.Move && ReferenceEquals(src, dst) && src.TryMove(srcPath, target)) { t.Total = t.Done = 1; return; }
        t.Total = Math.Max(1, SizeOf(src, srcPath, ct));
        Copy(src, srcPath, dst, target, t, ct);
        if (t.Move) src.Delete(srcPath);
    }

    public static string UniqueName(IFs fs, string dir, string name)
    {
        var path = fs.Join(dir, name);
        if (!fs.Exists(path)) return path;
        var dot = name.LastIndexOf('.');
        string stem = dot > 0 ? name[..dot] : name, ext = dot > 0 ? name[dot..] : "";
        for (int n = 2; ; n++)
        {
            path = fs.Join(dir, $"{stem} ({n}){ext}");
            if (!fs.Exists(path)) return path;
        }
    }

    static long SizeOf(IFs fs, string path, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!fs.IsDir(path)) return fs.List(fs.Parent(path)).FirstOrDefault(i => fs.Join(fs.Parent(path), i.Name) == path)?.Size ?? 0;
        return fs.List(path).Sum(i => i.Dir ? SizeOf(fs, fs.Join(path, i.Name), ct) : i.Size);
    }

    static void Copy(IFs src, string from, IFs dst, string to, Transfer t, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (src.IsDir(from))
        {
            dst.MkDir(to);
            foreach (var i in src.List(from)) Copy(src, src.Join(from, i.Name), dst, dst.Join(to, i.Name), t, ct);
            return;
        }
        var ok = false;
        try
        {
            using (var i = src.OpenRead(from))
            using (var o = dst.Create(to))
            {
                var buf = new byte[1 << 20];
                int n;
                while ((n = i.Read(buf, 0, buf.Length)) > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    o.Write(buf, 0, n);
                    Interlocked.Add(ref t.Done, n);
                }
            }
            ok = true;
        }
        finally
        {
            if (!ok) try { dst.Delete(to); } catch { } // never leave a truncated file behind
        }
    }
}
