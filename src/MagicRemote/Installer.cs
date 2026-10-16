using System.Diagnostics;

namespace MagicRemote;

/// <summary>Per-user install (no admin rights): copies the app folder, adds a menu entry and an uninstaller. Data stays in the data folder.</summary>
static class Installer
{
    static string Target => OperatingSystem.IsWindows()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "MagicRemote")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "magicremote", "app");

    public static int Install()
    {
        var src = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        var dst = Target;
        var exeName = Path.GetFileName(Environment.ProcessPath!);
        if (!string.Equals(Path.GetFullPath(src), Path.GetFullPath(dst), StringComparison.OrdinalIgnoreCase))
        {
            if (Directory.Exists(dst)) Directory.Delete(dst, true);
            CopyTree(src, dst);
        }
        var exe = Path.Combine(dst, exeName);
#if WINDOWS
        var lnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "MagicRemote.lnk");
        // WScript.Shell is the stock way to write a .lnk without a COM interop library
        Run("powershell.exe", "-NoProfile", "-NonInteractive", "-Command",
            $"$s=(New-Object -ComObject WScript.Shell).CreateShortcut('{lnk.Replace("'", "''")}');$s.TargetPath='{exe.Replace("'", "''")}';$s.WorkingDirectory='{dst.Replace("'", "''")}';$s.Save()");
        using var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\MagicRemote");
        k.SetValue("DisplayName", "MagicRemote");
        k.SetValue("DisplayVersion", Updater.Current);
        k.SetValue("Publisher", "OppaiHacker");
        k.SetValue("InstallLocation", dst);
        k.SetValue("DisplayIcon", exe);
        k.SetValue("UninstallString", $"\"{exe}\" --uninstall");
        k.SetValue("NoModify", 1);
        k.SetValue("NoRepair", 1);
        k.SetValue("EstimatedSize", (int)(DirSize(dst) / 1024));
#else
        File.SetUnixFileMode(exe, File.GetUnixFileMode(exe) | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
        var bin = Path.Combine(Home, ".local", "bin");
        Directory.CreateDirectory(bin);
        var link = Path.Combine(bin, "magicremote");
        if (File.Exists(link) || new FileInfo(link).LinkTarget != null) File.Delete(link);
        File.CreateSymbolicLink(link, exe);
        var icons = Path.Combine(Home, ".local", "share", "icons", "hicolor", "scalable", "apps");
        Directory.CreateDirectory(icons);
        File.Copy(Path.Combine(dst, "wwwroot", "icon.svg"), Path.Combine(icons, "magicremote.svg"), true);
        var apps = Path.Combine(Home, ".local", "share", "applications");
        Directory.CreateDirectory(apps);
        File.WriteAllText(Path.Combine(apps, "magicremote.desktop"), $"""
            [Desktop Entry]
            Type=Application
            Name=MagicRemote
            Comment=Remote desktop: RDP, SSH, SFTP and FTP in one window
            Exec="{exe}"
            Icon=magicremote
            Terminal=false
            Categories=Network;RemoteAccess;
            StartupWMClass=MagicRemote
            Keywords=rdp;ssh;sftp;ftp;remote;desktop;
            """);
#endif
        Console.WriteLine($"Installed to {dst}");
        return 0;
    }

    public static int Uninstall()
    {
        var dst = Target;
#if WINDOWS
        File.Delete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "MagicRemote.lnk"));
        Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\MagicRemote", false);
        // the running exe can't delete its own folder: let cmd do it once we're gone
        Process.Start(new ProcessStartInfo("cmd.exe", $"/c ping -n 3 127.0.0.1 >nul & rmdir /s /q \"{dst}\"") { CreateNoWindow = true, UseShellExecute = false });
#else
        foreach (var f in new[] { Path.Combine(Home, ".local", "bin", "magicremote"), Path.Combine(Home, ".local", "share", "applications", "magicremote.desktop"),
                                  Path.Combine(Home, ".local", "share", "icons", "hicolor", "scalable", "apps", "magicremote.svg") })
            File.Delete(f);
        if (Directory.Exists(dst)) Directory.Delete(dst, true);
#endif
        Console.WriteLine($"Uninstalled. Data (machines, vault) was kept in {Store.Dir}");
        return 0;
    }

    static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    static void CopyTree(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var f in Directory.EnumerateFiles(from))
        {
            var t = Path.Combine(to, Path.GetFileName(f));
            File.Copy(f, t);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(t, File.GetUnixFileMode(f));
        }
        foreach (var d in Directory.EnumerateDirectories(from)) CopyTree(d, Path.Combine(to, Path.GetFileName(d)));
    }

    static long DirSize(string d) => new DirectoryInfo(d).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);

    static void Run(string exe, params string[] args)
    {
        var psi = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        p.WaitForExit();
    }
}
