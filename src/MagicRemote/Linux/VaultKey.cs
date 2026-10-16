using System.Diagnostics;
using System.Security.Cryptography;

namespace MagicRemote;

/// <summary>Vault master key: Secret Service (GNOME Keyring / KWallet via secret-tool) when available, else a 0600 key file.</summary>
static class VaultKey
{
    static byte[]? key;
    static readonly string[] Attr = ["service", "magicremote", "key", "vault"];
    static string FilePath => Path.Combine(Store.Dir, "vault.key");
    static string Marker => Path.Combine(Store.Dir, "vault.keyring");

    public static byte[] Get()
    {
        lock (Attr) return key ??= Load();
    }

    static byte[] Load()
    {
        if (Environment.GetEnvironmentVariable("MAGICREMOTE_KEYRING") != "0" && Run("lookup", null) is { Length: > 0 } k)
            return Convert.FromBase64String(k.Trim());
        if (File.Exists(FilePath)) return Convert.FromBase64String(File.ReadAllText(FilePath).Trim());
        // the key went to the keyring before: never silently replace it, the stored passwords would be lost
        if (File.Exists(Marker)) throw new InvalidOperationException("Vault: no access to the keyring (Secret Service). Unlock it and try again.");
        var fresh = RandomNumberGenerator.GetBytes(32);
        var b64 = Convert.ToBase64String(fresh);
        if (Environment.GetEnvironmentVariable("MAGICREMOTE_KEYRING") != "0" && Run("store", b64) != null && Run("lookup", null)?.Trim() == b64)
        {
            Directory.CreateDirectory(Store.Dir);
            File.WriteAllText(Marker, "key in Secret Service: service=magicremote key=vault\n");
            return fresh;
        }
        Directory.CreateDirectory(Store.Dir);
        using (var f = new FileStream(FilePath, new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite }))
        using (var w = new StreamWriter(f)) w.Write(b64);
        return fresh;
    }

    /// <summary>secret-tool lookup|store; null when the tool or the keyring is missing.</summary>
    static string? Run(string verb, string? secret)
    {
        try
        {
            var psi = new ProcessStartInfo("secret-tool") { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
            psi.ArgumentList.Add(verb);
            if (verb == "store") { psi.ArgumentList.Add("--label=MagicRemote vault"); }
            foreach (var a in Attr) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi)!;
            if (secret != null) p.StandardInput.Write(secret);
            p.StandardInput.Close();
            var o = p.StandardOutput.ReadToEnd();
            if (!p.WaitForExit(15000)) { p.Kill(); return null; }
            return p.ExitCode == 0 ? o : null;
        }
        catch { return null; }
    }
}
