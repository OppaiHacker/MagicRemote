using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;

namespace MagicRemote;

/// <summary>Checks GitHub Releases for a newer build and swaps the app folder in place (data lives elsewhere and stays).</summary>
public static class Updater
{
    public static string Current => (Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0").Split('+')[0];
    static string Feed => Environment.GetEnvironmentVariable("MAGICREMOTE_UPDATE_URL") is { Length: > 0 } u ? u : "https://api.github.com/repos/OppaiHacker/MagicRemote/releases/latest";
    static string AssetSuffix => OperatingSystem.IsWindows() ? "win-x64.zip" : "linux-x64.tar.gz";

    public static bool Newer(string tag, string current) =>
        Version.TryParse(tag.TrimStart('v', 'V').Split('-')[0], out var a) && Version.TryParse(current.Split('-')[0], out var b) && a > b;

    /// <summary>(version, download url) of a newer release for this OS, or null.</summary>
    public static async Task<(string version, string url, string? sha256)?> Check()
    {
        using var http = Client();
        var rel = await http.GetFromJsonAsync<JsonElement>(Feed);
        var tag = rel.GetProperty("tag_name").GetString() ?? "";
        if (!Newer(tag, Current)) return null;
        foreach (var a in rel.GetProperty("assets").EnumerateArray())
            if (a.GetProperty("name").GetString()!.EndsWith(AssetSuffix, StringComparison.OrdinalIgnoreCase))
                return (tag.TrimStart('v', 'V'), a.GetProperty("browser_download_url").GetString()!,
                    a.TryGetProperty("digest", out var d) && d.GetString() is { } dg && dg.StartsWith("sha256:") ? dg[7..] : null);
        return null;
    }

    static HttpClient Client()
    {
        var h = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        h.DefaultRequestHeaders.UserAgent.ParseAdd("MagicRemote/" + Current);
        return h;
    }

    /// <summary>Downloads and unpacks the release next to the app, then hands over to a script that swaps folders once this process exits and restarts the app.</summary>
    public static async Task Apply(string url, string? sha256)
    {
        var appDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        var stage = appDir + ".new";
        if (Directory.Exists(stage)) Directory.Delete(stage, true);
        Directory.CreateDirectory(stage);
        var archive = Path.Combine(Path.GetTempPath(), "magicremote-update-" + Guid.NewGuid().ToString("n") + Path.GetExtension(url));
        using (var http = Client())
        await using (var src = await http.GetStreamAsync(url))
        await using (var dst = File.Create(archive)) await src.CopyToAsync(dst);
        if (sha256 != null)
        {
            await using var f = File.OpenRead(archive);
            if (!Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(f)).Equals(sha256, StringComparison.OrdinalIgnoreCase))
            {
                f.Close();
                File.Delete(archive);
                throw new InvalidDataException("Update: SHA-256 checksum does not match, downloaded file rejected.");
            }
        }
        if (url.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) ZipFile.ExtractToDirectory(archive, stage);
        else
        {
            await using var f = File.OpenRead(archive);
            await using var gz = new GZipStream(f, CompressionMode.Decompress);
            await TarFile.ExtractToDirectoryAsync(gz, stage, true);
        }
        File.Delete(archive);
        // archives may wrap everything in one top folder
        var exeName = Path.GetFileName(Environment.ProcessPath!);
        var root = File.Exists(Path.Combine(stage, exeName)) ? stage
            : Directory.EnumerateDirectories(stage).FirstOrDefault(d => File.Exists(Path.Combine(d, exeName))) ?? throw new InvalidDataException("Update package does not contain " + exeName);

        var pid = Environment.ProcessId;
        if (OperatingSystem.IsWindows())
        {
            var bat = Path.Combine(Path.GetTempPath(), "magicremote-update.cmd");
            File.WriteAllText(bat, $"""
                @echo off
                :wait
                tasklist /fi "PID eq {pid}" | find "{pid}" >nul && (ping -n 2 127.0.0.1 >nul & goto wait)
                robocopy "{root}" "{appDir}" /MIR /R:5 /W:1 /NFL /NDL /NJH /NJS >nul
                rmdir /s /q "{stage}"
                start "" "{Path.Combine(appDir, exeName)}"
                del "%~f0"
                """);
            Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{bat}\"") { CreateNoWindow = true, UseShellExecute = false });
        }
        else
        {
            var sh = Path.Combine(Path.GetTempPath(), $"magicremote-update-{pid}.sh");
            File.WriteAllText(sh, $"""
                #!/bin/sh
                while kill -0 {pid} 2>/dev/null; do sleep 0.5; done
                rm -rf '{appDir}.old' && mv '{appDir}' '{appDir}.old' && mv '{root}' '{appDir}' && rm -rf '{appDir}.old' '{stage}'
                chmod +x '{Path.Combine(appDir, exeName)}'
                setsid '{Path.Combine(appDir, exeName)}' >/dev/null 2>&1 &
                rm -f "$0"
                """);
            File.SetUnixFileMode(sh, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            Process.Start(new ProcessStartInfo("setsid", ["-f", sh]) { UseShellExecute = false });
        }
    }
}
