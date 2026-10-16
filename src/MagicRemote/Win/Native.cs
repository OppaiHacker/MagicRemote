using System.ComponentModel;
using System.Runtime.InteropServices;

namespace MagicRemote;

static class Native
{
    public const int WM_CLIPBOARDUPDATE = 0x031D;

    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool AddClipboardFormatListener(IntPtr hwnd);
    [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, IntPtr extra);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern int GetWindowThreadProcessId(IntPtr h, out int pid);
    public static bool OwnsForeground() { GetWindowThreadProcessId(GetForegroundWindow(), out var pid); return pid == Environment.ProcessId; }
    public static void Key(byte scan, bool up) => keybd_event(0, scan, 0x0008 | (up ? 0x0002u : 0), IntPtr.Zero); // KEYEVENTF_SCANCODE

    [DllImport("ole32.dll")] static extern int RegisterDragDrop(IntPtr hwnd, IOleDropTarget target);
    delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(Point p);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetProp(IntPtr h, string name);
    /// <summary>Diagnostics: does the window under this screen point accept OLE drops?</summary>
    public static bool DropTargetAt(Point p) => GetProp(WindowFromPoint(p), "OleDropTargetInterface") != IntPtr.Zero;
    [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr parent, EnumProc cb, IntPtr l);

    /// <summary>Makes every child window of root (e.g. the RDP control's own input windows) accept file drops.</summary>
    public static void AcceptFileDrops(IntPtr root, IOleDropTarget target) =>
        EnumChildWindows(root, (h, _) => { RegisterDragDrop(h, target); return true; }, IntPtr.Zero); // already registered = harmless error

    public static void SetCaption(IntPtr hwnd, bool dark, Color bg)
    {
        int on = dark ? 1 : 0, color = bg.R | bg.G << 8 | bg.B << 16;
        DwmSetWindowAttribute(hwnd, 20, ref on, 4);    // DWMWA_USE_IMMERSIVE_DARK_MODE
        DwmSetWindowAttribute(hwnd, 35, ref color, 4); // DWMWA_CAPTION_COLOR (Win11)
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    sealed class NETRESOURCE
    {
        public int dwScope, dwType = 1, dwDisplayType, dwUsage; // RESOURCETYPE_DISK
        public string? lpLocalName, lpRemoteName, lpComment, lpProvider;
    }
    [DllImport("mpr.dll", CharSet = CharSet.Unicode)] static extern int WNetAddConnection2(NETRESOURCE nr, string? password, string? user, int flags);

    /// <summary>Authenticates to an SMB share (\\host\share) with explicit credentials.</summary>
    public static void ConnectShare(string shareRoot, string user, string password)
    {
        int rc = WNetAddConnection2(new NETRESOURCE { lpRemoteName = shareRoot }, password, user, 0);
        // 1219: already connected to this server with other credentials — the existing session is reused.
        if (rc != 0 && rc != 1219 && rc != 85) throw new Win32Exception(rc);
    }
}

[ComImport, Guid("00000122-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IOleDropTarget
{
    [PreserveSig] int DragEnter(System.Runtime.InteropServices.ComTypes.IDataObject data, int keys, long pt, ref int effect);
    [PreserveSig] int DragOver(int keys, long pt, ref int effect);
    [PreserveSig] int DragLeave();
    [PreserveSig] int Drop(System.Runtime.InteropServices.ComTypes.IDataObject data, int keys, long pt, ref int effect);
}

/// <summary>OLE drop target for files (Explorer drag), Shift = move.</summary>
sealed class FileDropTarget(Action<string[], bool> drop) : IOleDropTarget
{
    string[]? files;
    static int Pick(string[]? f, int keys, int allowed) => f == null ? 0 : (keys & 4) != 0 && (allowed & 2) != 0 ? 2 : allowed & 1;
    public int DragEnter(System.Runtime.InteropServices.ComTypes.IDataObject data, int keys, long pt, ref int effect)
    {
        try { files = new DataObject(data).GetData(DataFormats.FileDrop) as string[]; } catch (Exception ex) { App.Log(ex); files = null; }
        effect = Pick(files, keys, effect);
        return 0;
    }
    public int DragOver(int keys, long pt, ref int effect) { effect = Pick(files, keys, effect); return 0; }
    public int DragLeave() { files = null; return 0; }
    public int Drop(System.Runtime.InteropServices.ComTypes.IDataObject data, int keys, long pt, ref int effect)
    {
        effect = Pick(files, keys, effect);
        if (effect != 0) drop(files!, effect == 2);
        files = null;
        return 0;
    }
}
