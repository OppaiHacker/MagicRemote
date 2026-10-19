using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace MagicRemote.Setup;

/// <summary>Web installer: downloads the latest Windows release from GitHub, verifies its SHA-256, runs the app's own --install and starts it.</summary>
static class Program
{
    // same feed (and test override) as Updater.cs in the app
    static string Feed => Environment.GetEnvironmentVariable("MAGICREMOTE_UPDATE_URL") is { Length: > 0 } u ? u : "https://api.github.com/repos/OppaiHacker/MagicRemote/releases/latest";
    static string Installed => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "MagicRemote", "MagicRemote.exe");

    // GitHub API field names
    sealed class Release { public string tag_name { get; set; } = ""; public Asset[] assets { get; set; } = []; }
    sealed class Asset { public string name { get; set; } = ""; public string browser_download_url { get; set; } = ""; public long size { get; set; } public string? digest { get; set; } }

    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();

    [STAThread]
    static int Main()
    {
        SetProcessDPIAware();
        Application.EnableVisualStyles();
        var status = new Label { Dock = DockStyle.Top, Height = 36, Text = "Checking the latest version…", TextAlign = ContentAlignment.MiddleLeft };
        var bar = new ProgressBar { Dock = DockStyle.Top, Height = 20, Style = ProgressBarStyle.Marquee };
        var form = new Form
        {
            Text = "MagicRemote Setup", ClientSize = new Size(440, 92), Padding = new Padding(16, 8, 16, 16),
            FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, StartPosition = FormStartPosition.CenterScreen,
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath),
        };
        form.Controls.Add(bar);
        form.Controls.Add(status); // docked last = on top
        int rc = 1;
        form.Shown += async (_, _) =>
        {
            try
            {
                await Install((text, pct) =>
                {
                    status.Text = text;
                    bar.Style = pct is null ? ProgressBarStyle.Marquee : ProgressBarStyle.Continuous;
                    if (pct is int p) bar.Value = p;
                });
                rc = 0;
            }
            catch (Exception e) { MessageBox.Show(form, e.Message, "MagicRemote Setup", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            form.Close();
        };
        Application.Run(form);
        return rc;
    }

    static async Task Install(Action<string, int?> report)
    {
        ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; // older Windows 10 builds may default below TLS 1.2
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("MagicRemote-Setup");
        var rel = new JavaScriptSerializer().Deserialize<Release>(await http.GetStringAsync(Feed));
        var asset = rel.assets.FirstOrDefault(a => a.name.EndsWith("win-x64.zip", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException("The latest release has no Windows package.");
        var version = rel.tag_name.TrimStart('v', 'V');

        var tmp = Path.Combine(Path.GetTempPath(), "magicremote-setup-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(tmp);
        try
        {
            var zip = Path.Combine(tmp, asset.name);
            using (var resp = await http.GetAsync(asset.browser_download_url, HttpCompletionOption.ResponseHeadersRead))
            {
                resp.EnsureSuccessStatusCode();
                var total = resp.Content.Headers.ContentLength ?? asset.size;
                using var src = await resp.Content.ReadAsStreamAsync();
                using var dst = File.Create(zip);
                var buf = new byte[81920];
                long done = 0;
                int n, shown = -1;
                while ((n = await src.ReadAsync(buf, 0, buf.Length)) > 0)
                {
                    await dst.WriteAsync(buf, 0, n);
                    done += n;
                    var pct = total > 0 ? (int)(done * 100 / total) : 0;
                    if (pct != shown) report($"Downloading MagicRemote {version}…  {done >> 20} / {total >> 20} MB", shown = pct);
                }
            }

            if (asset.digest is { } d && d.StartsWith("sha256:"))
            {
                report("Verifying…", null);
                using var f = File.OpenRead(zip);
                using var sha = SHA256.Create();
                var hash = BitConverter.ToString(await Task.Run(() => sha.ComputeHash(f))).Replace("-", "");
                if (!hash.Equals(d.Substring(7), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("SHA-256 checksum does not match, downloaded file rejected.");
            }

            report("Installing…", null);
            var dir = Path.Combine(tmp, "app");
            await Task.Run(() => ZipFile.ExtractToDirectory(zip, dir));
            var exe = Directory.EnumerateFiles(dir, "MagicRemote.exe", SearchOption.AllDirectories).FirstOrDefault()
                ?? throw new InvalidDataException("The package does not contain MagicRemote.exe.");
            using (var p = Process.Start(new ProcessStartInfo(exe, "--install") { UseShellExecute = false, CreateNoWindow = true })!)
            {
                await Task.Run(p.WaitForExit);
                if (p.ExitCode != 0) throw new Exception($"Installation failed (code {p.ExitCode}). Close MagicRemote if it is running and try again.");
            }
            Process.Start(Installed);
        }
        finally { try { Directory.Delete(tmp, true); } catch { } }
    }
}
