using System.Drawing;

namespace MagicRemote;

static class Program
{
#if WINDOWS
    [STAThread]
#endif
    static int Main(string[] args)
    {
        if (args.Contains("--selftest")) return SelfTest.Run();
        if (args.Contains("--version")) { Console.WriteLine(Updater.Current); return 0; }
        if (args.Contains("--install")) return Installer.Install();
        if (args.Contains("--uninstall")) return Installer.Uninstall();
#if WINDOWS
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
        return 0;
#else
        return LinuxApp.Run(args);
#endif
    }
}

/// <summary>`MagicRemote --selftest > out.txt` — checks the logic that doesn't need a server.</summary>
static class SelfTest
{
    public static int Run()
    {
        // isolated data dir and no desktop keyring: the test never touches the real vault
        var data = Path.Combine(Path.GetTempPath(), "mr-selftest-data-" + Guid.NewGuid().ToString("n"));
        Environment.SetEnvironmentVariable("MAGICREMOTE_DATA", data);
        Environment.SetEnvironmentVariable("MAGICREMOTE_KEYRING", "0");
        try
        {
            Check(Session.SplitUser(@"CORP\op") == ("CORP", "op"), "split domain user");
            Check(Session.SplitUser("op@corp.local") == ("", "op@corp.local"), "split UPN");
            Check(Session.ShareRoot(@"\\srv\Users\me\Desktop") == @"\\srv\Users", "share root");

            var c = Store.Protect("s3cret!äö");
            Check(c != "s3cret!äö" && Store.Unprotect(c) == "s3cret!äö", "vault round trip");
            Check(Store.Unprotect("garbage") == "", "vault rejects garbage");
            Check(Store.Protect("x") != Store.Protect("x"), "vault: fresh nonce/salt per secret");

            var root = Path.Combine(Path.GetTempPath(), "mr-selftest-" + Guid.NewGuid().ToString("n"));
            string a = Path.Combine(root, "a"), b = Path.Combine(root, "b");
            Directory.CreateDirectory(Path.Combine(a, "dir", "sub"));
            File.WriteAllBytes(Path.Combine(a, "dir", "sub", "x.bin"), new byte[3_000_000]);
            File.WriteAllText(Path.Combine(a, "dir", "y.txt"), "hello");
            Directory.CreateDirectory(b);
            File.WriteAllText(Path.Combine(b, "dir"), "collision");
            IFs src = new LocalFs(a), dst = new LocalFs(b); // two instances = cross-file-system path (no rename shortcut)

            Check(Transfers.UniqueName(dst, b, "dir") == Path.Combine(b, "dir (2)"), "unique name");
            var t = new Transfer { Name = "dir", Move = false };
            Transfers.Run(t, src, Path.Combine(a, "dir"), dst, b);
            Check(File.ReadAllText(Path.Combine(b, "dir (2)", "y.txt")) == "hello" && new FileInfo(Path.Combine(b, "dir (2)", "sub", "x.bin")).Length == 3_000_000, "recursive copy");
            Check(t.Done == t.Total && t.Total == 3_000_005, "progress accounting");

            var mv = new Transfer { Name = "dir", Move = true };
            Transfers.Run(mv, src, Path.Combine(a, "dir"), dst, b);
            Check(!Directory.Exists(Path.Combine(a, "dir")) && Directory.Exists(Path.Combine(b, "dir (3)")), "move deletes source");

            var cancel = new Transfer { Name = "x.bin" };
            cancel.Cts.Cancel();
            try { Transfers.Run(cancel, dst, Path.Combine(b, "dir (3)", "sub", "x.bin"), dst, b); Check(false, "cancel throws"); }
            catch (OperationCanceledException) { Check(!File.Exists(Path.Combine(b, "x.bin")), "cancel leaves no partial file"); }
            Directory.Delete(root, true);

            var fsRoot = Path.GetPathRoot(Path.GetTempPath())!;
            Check(new LocalFs(fsRoot).Parent(fsRoot) == (OperatingSystem.IsWindows() ? "" : "/"), "parent of file system root");

            var fgd = new byte[4 + 592 * 2];
            BitConverter.GetBytes(2u).CopyTo(fgd, 0);
            System.Text.Encoding.Unicode.GetBytes("report.xlsx").CopyTo(fgd, 4 + 72);
            System.Text.Encoding.Unicode.GetBytes("café.png").CopyTo(fgd, 4 + 592 + 72);
            Check(ClipFormats.FileNames(fgd).SequenceEqual(["report.xlsx", "café.png"]), "FileGroupDescriptorW parse");

            // .rdp (as written by mstsc: UTF-16 text, CRLF) → machine → .rdp
            Store.Load();
            Store.D.Creds.Add(new Cred { Id = "c1", User = @"CORP\admin" });
            var rdp = "\uFEFFscreen mode id:i:2\r\nfull address:s:srv01.corp.local:3390\r\nusername:s:CORP\\admin\r\nuse multimon:i:1\r\nredirectclipboard:i:1\r\naudiocapturemode:i:1\r\n" +
                      "gatewayhostname:s:gw.corp.local\r\ngatewayusagemethod:i:1\r\nrestricted admin mode:i:1\r\nremoteapplicationmode:i:1\r\nremoteapplicationprogram:s:||Excel\r\nremoteapplicationname:s:Excel\r\nauthentication level:i:0\r\n";
            var im = RdpFile.Import(rdp, "srv01");
            Check(im.Host == "srv01.corp.local" && im.Port == 3390 && im.Mon == "all" && im.Mic && im.Gateway == "gw.corp.local" && im.Auth == "restricted"
                  && im.App == "||Excel" && im.Name == "Excel" && !im.CertCheck && im.Cred == "c1", ".rdp import");
            var re = RdpFile.Parse(RdpFile.Export(im));
            Check(re["full address"] == "srv01.corp.local:3390" && re["username"] == @"CORP\admin" && re["remoteapplicationprogram"] == "||Excel" && re["restricted admin mode"] == "1", ".rdp export round trip");
            Check(RdpFile.Import("full address:s:[fe80::1]:3391", "v6") is { Host: "[fe80::1]", Port: 3391 }, ".rdp IPv6 address");
            Check(RdpFile.SafeName("a/b:c*?") == "a_b_c__", "safe file name");

            Check(Updater.Newer("v1.10.0", "1.9.3") && !Updater.Newer("1.1.0", "1.1.0") && !Updater.Newer("garbage", "1.0.0"), "version compare");

#if !WINDOWS
            var args = FreeRdpSession.Args(new Machine { Host = "h", Port = 3389, Name = "Office, PC", App = "notepad.exe", RelMouse = true, Scale = "dynamic" },
                "dynamic", new Rectangle(0, 0, 1600, 900), new Cred { User = @"CORP\john" }, "p@ss, word");
            Check(args.Contains("/u:john") && args.Contains("/d:CORP") && args.Contains("/p:p@ss, word") && args.Contains("/size:1600x900") && args.Contains("/dynamic-resolution")
                  && args.Contains("/mouse:relative:on,grab:on") && args.Contains("/app:program:notepad.exe,name:Office  PC"), "FreeRDP arguments");
#endif
            Console.WriteLine("selftest ok");
            return 0;
        }
        catch (Exception e)
        {
            Console.WriteLine("selftest FAILED: " + e.Message);
            return 1;
        }
        finally { try { Directory.Delete(data, true); } catch { } }
    }

    static void Check(bool ok, string what) { if (!ok) throw new Exception(what); Console.WriteLine("  ok  " + what); }
}
