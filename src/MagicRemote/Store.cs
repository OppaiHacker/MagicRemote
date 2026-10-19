using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MagicRemote;

public sealed class Profile
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Color { get; set; } = "#6b8afd";
    public List<string> Groups { get; set; } = []; // kept even when empty, so a group can exist before any machine is in it
}

public sealed class Machine
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Host { get; set; } = "";
    public string Proto { get; set; } = "rdp"; // rdp | ssh | sftp | ftp
    public int Port { get; set; } = 3389;
    public int SshPort { get; set; } = 22;      // file transfer for Linux RDP hosts (SFTP)
    public bool Ftps { get; set; } = true;      // FTP: explicit TLS
    public string Os { get; set; } = "win";
    public string Profile { get; set; } = "";
    public string Group { get; set; } = "No group";
    public string? Cred { get; set; }
    public string Gateway { get; set; } = "";
    public string Mac { get; set; } = "";
    public string Share { get; set; } = "";      // RDP/Windows: SMB path for file transfer (default \\host\C$\Users\<user>\Desktop)
    public string KeyFile { get; set; } = "";    // SSH/SFTP: private key path
    public bool Fav { get; set; }
    public string Scale { get; set; } = "dynamic";
    public string Mon { get; set; } = "1";
    public int Depth { get; set; } = 32;
    public bool Hidpi { get; set; } = true;
    public bool Clip { get; set; } = true;
    public bool Drives { get; set; } = true;
    public bool Audio { get; set; } = true;
    public bool Mic { get; set; }
    public bool Print { get; set; }
    public bool Smart { get; set; }
    public bool Lowlat { get; set; } = true;
    public bool Rawin { get; set; }
    public bool Cam { get; set; }               // RDP: webcams (H.264 encoded on the client)
    public bool Usb { get; set; }               // RDP: PnP/MTP devices, COM/LPT ports, POS devices, hot-plugged too
    public bool Admin { get; set; }             // RDP: /admin console session
    public string Auth { get; set; } = "";
    public string App { get; set; } = "";        // RDP: RemoteApp program (e.g. notepad.exe or ||Alias); "" = full desktop
    public string AppArgs { get; set; } = "";
    public bool RelMouse { get; set; }           // RDP: relative mouse (games, 3D)      // RDP: "" | "restricted" (Restricted Admin) | "rcg" (Remote Credential Guard)
    public bool CertCheck { get; set; } = true; // RDP: warn on untrusted server certificate (off only for self-signed lab hosts)
    public int Hue { get; set; } = 220;
    public string Img { get; set; } = "";        // tile picture, small data:image/ URL
    public long Last { get; set; }
}

public sealed class Cred
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public string User { get; set; } = "";
    public string Pass { get; set; } = ""; // DPAPI (CurrentUser) ciphertext, base64
}

public sealed class Data
{
    public List<Profile> Profiles { get; set; } = [];
    public List<Machine> Machines { get; set; } = [];
    public List<Cred> Creds { get; set; } = [];
    public Dictionary<string, string> KnownHosts { get; set; } = []; // SFTP host -> SHA256 fingerprint (trust on first use)
    public string Profile { get; set; } = "";
    public string Theme { get; set; } = "dark";
    public bool Sidebar { get; set; } = true;
}

public static class Store
{
    public static readonly string Dir = Environment.GetEnvironmentVariable("MAGICREMOTE_DATA") is { Length: > 0 } d ? d
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MagicRemote");
    static string FilePath => Path.Combine(Dir, "data.json");
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    public static Data D = new();

    public static void Load()
    {
        try
        {
            if (File.Exists(FilePath)) D = JsonSerializer.Deserialize<Data>(File.ReadAllText(FilePath), Json) ?? new();
        }
        catch
        {
            // keep the unreadable file for recovery instead of silently overwriting it
            File.Copy(FilePath, FilePath + ".bad", true);
            D = new();
        }
        if (D.Profiles.Count == 0) D.Profiles.Add(new Profile { Id = "default", Name = "Default" });
        if (!D.Profiles.Any(p => p.Id == D.Profile)) D.Profile = D.Profiles[0].Id;
    }

    public static void Save()
    {
        Directory.CreateDirectory(Dir);
        lock (D)
        {
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(D, Json));
            File.Move(tmp, FilePath, true);
        }
    }

    static readonly byte[] Entropy = "MagicRemote.vault.v1"u8.ToArray();

#if WINDOWS
    // DPAPI (CurrentUser): only this Windows account can decrypt
    public static string Protect(string plain) =>
        Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), Entropy, DataProtectionScope.CurrentUser));

    public static string Unprotect(string cipher)
    {
        try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(cipher), Entropy, DataProtectionScope.CurrentUser)); }
        catch { return ""; }
    }
#else
    // AES-256-GCM; the key lives in the desktop keyring (Secret Service) or, without one, in a 0600 file next to the data
    public static string Protect(string plain)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var pt = Encoding.UTF8.GetBytes(plain);
        var ct = new byte[pt.Length];
        var tag = new byte[16];
        using (var g = new AesGcm(VaultKey.Get(), 16)) g.Encrypt(nonce, pt, ct, tag, Entropy);
        return Convert.ToBase64String([.. nonce, .. tag, .. ct]);
    }

    public static string Unprotect(string cipher)
    {
        try
        {
            var b = Convert.FromBase64String(cipher);
            var pt = new byte[b.Length - 28];
            using var g = new AesGcm(VaultKey.Get(), 16);
            g.Decrypt(b.AsSpan(0, 12), b.AsSpan(28), b.AsSpan(12, 16), pt, Entropy);
            return Encoding.UTF8.GetString(pt);
        }
        catch { return ""; }
    }
#endif

    public static Cred? CredOf(Machine m) => D.Creds.FirstOrDefault(c => c.Id == m.Cred);
    public static string PasswordOf(Machine m) => CredOf(m) is { } c ? Unprotect(c.Pass) : "";
}
