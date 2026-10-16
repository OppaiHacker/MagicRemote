using System.Text;

namespace MagicRemote;

/// <summary>.rdp files (mstsc format: "key:type:value" per line) to and from a Machine.</summary>
public static class RdpFile
{
    public static Dictionary<string, string> Parse(string text)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim().TrimStart('﻿');
            int a = line.IndexOf(':'), b = a < 0 ? -1 : line.IndexOf(':', a + 1);
            if (b < 0) continue;
            d[line[..a].Trim()] = line[(b + 1)..];
        }
        return d;
    }

    public static string User(string text) => Parse(text).GetValueOrDefault("username", "");

    public static Machine Import(string text, string fallbackName)
    {
        var d = Parse(text);
        string S(string k, string def = "") => d.TryGetValue(k, out var v) ? v.Trim() : def;
        bool B(string k, bool def) => d.TryGetValue(k, out var v) ? v.Trim() != "0" : def;
        int I(string k, int def) => int.TryParse(S(k), out var v) ? v : def;

        var addr = S("full address");
        if (addr == "") throw new ArgumentException(".rdp file has no address (full address).");
        var port = I("server port", 3389);
        // host:port, [v6]:port
        if (addr.StartsWith('[') && addr.IndexOf(']') is var e and > 0)
        {
            if (addr.Length > e + 2 && addr[e + 1] == ':') port = int.TryParse(addr[(e + 2)..], out var p6) ? p6 : port;
            addr = addr[..(e + 1)];
        }
        else if (addr.Count(c => c == ':') == 1)
        {
            var i = addr.IndexOf(':');
            port = int.TryParse(addr[(i + 1)..], out var p) ? p : port;
            addr = addr[..i];
        }

        var user = S("username");
        var m = new Machine
        {
            Id = Guid.NewGuid().ToString("n"),
            Name = fallbackName,
            Host = addr,
            Port = port,
            Proto = "rdp",
            Group = "Import .rdp",
            Gateway = I("gatewayusagemethod", 0) is 1 or 2 ? S("gatewayhostname") : "",
            Scale = I("dynamic resolution", 1) == 1 ? "dynamic" : I("smart sizing", 0) == 1 ? "fit" : "native",
            Mon = B("use multimon", false) ? "all" : "1",
            Depth = I("session bpp", 32) is 15 or 16 ? 16 : I("session bpp", 32) == 24 ? 24 : 32,
            Clip = B("redirectclipboard", true),
            Drives = S("drivestoredirect") != "",
            Audio = I("audiomode", 0) == 0,
            Mic = I("audiocapturemode", 0) == 1,
            Print = B("redirectprinters", false),
            Smart = B("redirectsmartcards", false),
            Cam = S("camerastoredirect") != "",
            Usb = S("devicestoredirect") != "" || B("redirectcomports", false),
            Admin = B("administrative session", false),
            Auth = B("enablerdsaadauth", false) ? "" : I("restricted admin mode", 0) == 1 ? "restricted" : I("enableremotecredentialguard", 0) == 1 || I("redirected authentication", 0) == 1 ? "rcg" : "",
            CertCheck = I("authentication level", 2) != 0,
            App = I("remoteapplicationmode", 0) == 1 ? S("remoteapplicationprogram") : "",
            AppArgs = I("remoteapplicationmode", 0) == 1 ? S("remoteapplicationcmdline") : "",
            Rawin = I("keyboardhook", 2) == 1,
        };
        if (m.App != "" && S("remoteapplicationname") is { Length: > 0 } an) m.Name = an;
        // credentials never come from the file; match an existing vault entry by user name
        if (user != "") m.Cred = Store.D.Creds.FirstOrDefault(c => string.Equals(c.User, user, StringComparison.OrdinalIgnoreCase)
            || string.Equals(Session.SplitUser(c.User).user, Session.SplitUser(user).user, StringComparison.OrdinalIgnoreCase))?.Id;
        return m;
    }

    public static string Export(Machine m)
    {
        var sb = new StringBuilder();
        void S(string k, string v) => sb.Append(k).Append(":s:").Append(v).Append("\r\n");
        void I(string k, int v) => sb.Append(k).Append(":i:").Append(v).Append("\r\n");
        S("full address", m.Port == 3389 ? m.Host : $"{m.Host}:{m.Port}");
        if (Store.CredOf(m) is { } c) S("username", c.User);
        I("screen mode id", 1);
        I("dynamic resolution", m.Scale == "dynamic" ? 1 : 0);
        I("smart sizing", m.Scale == "fit" ? 1 : 0);
        I("use multimon", m.Mon == "all" ? 1 : 0);
        I("session bpp", m.Depth);
        I("redirectclipboard", m.Clip ? 1 : 0);
        if (m.Drives) S("drivestoredirect", "*");
        I("audiomode", m.Audio ? 0 : 2);
        I("audiocapturemode", m.Mic ? 1 : 0);
        I("redirectprinters", m.Print ? 1 : 0);
        I("redirectsmartcards", m.Smart ? 1 : 0);
        if (m.Cam) S("camerastoredirect", "*");
        if (m.Usb) { S("devicestoredirect", "*"); I("redirectcomports", 1); I("redirectposdevices", 1); }
        I("administrative session", m.Admin ? 1 : 0);
        if (m.Auth == "restricted") I("restricted admin mode", 1);
        if (m.Auth == "rcg") I("enableremotecredentialguard", 1);
        I("authentication level", m.CertCheck ? 2 : 0);
        I("enablecredsspsupport", 1);
        I("keyboardhook", m.Rawin ? 1 : 2);
        I("autoreconnection enabled", 1);
        I("networkautodetect", 1);
        I("bandwidthautodetect", 1);
        I("connection type", m.Lowlat ? 6 : 7);
        if (m.Gateway != "") { S("gatewayhostname", m.Gateway); I("gatewayusagemethod", 1); I("gatewayprofileusagemethod", 1); I("gatewaycredentialssource", 0); }
        if (m.App != "")
        {
            I("remoteapplicationmode", 1);
            S("remoteapplicationprogram", m.App);
            S("remoteapplicationname", m.Name);
            if (m.AppArgs != "") S("remoteapplicationcmdline", m.AppArgs);
        }
        return sb.ToString();
    }

    public static string SafeName(string name)
    {
        var bad = Path.GetInvalidFileNameChars().Concat(['\\', '/', ':', '*', '?', '"', '<', '>', '|']).ToHashSet();
        var s = new string(name.Select(c => bad.Contains(c) ? '_' : c).ToArray()).Trim();
        return s == "" ? "machine" : s;
    }
}

static class ClipFormats
{
    /// <summary>Names from a FileGroupDescriptorW (how RDP announces files copied inside a session).</summary>
    public static string[] FileNames(byte[] b)
    {
        if (b.Length < 4) return [];
        var count = (int)Math.Min(BitConverter.ToUInt32(b, 0), (uint)((b.Length - 4) / 592));
        var names = new string[count];
        for (int i = 0; i < count; i++)
        {
            var s = System.Text.Encoding.Unicode.GetString(b, 4 + i * 592 + 72, 520);
            var z = s.IndexOf('\0');
            names[i] = z >= 0 ? s[..z] : s;
        }
        return names;
    }
}
