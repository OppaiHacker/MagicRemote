# Generates MSTSCLib.dll (COM interop for the Remote Desktop ActiveX control) from mstscax.dll.
# Must run in Windows PowerShell 5.1 (.NET Framework has TypeLibConverter; .NET Core does not).
param([string]$Out = "$PSScriptRoot\MSTSCLib.dll")
Add-Type -TypeDefinition @"
using System; using System.Reflection; using System.Runtime.InteropServices;
public class TlbConv : ITypeLibImporterNotifySink {
  [DllImport("oleaut32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
  static extern void LoadTypeLibEx(string file, int regKind, [MarshalAs(UnmanagedType.Interface)] out object tlb);
  public static void Run(string tlbPath, string outDll) {
    object tlb; LoadTypeLibEx(tlbPath, 2, out tlb);
    var ab = new TypeLibConverter().ConvertTypeLibToAssembly(tlb, outDll, TypeLibImporterFlags.SafeArrayAsSystemArray, new TlbConv(), null, null, "MSTSCLib", null);
    ab.Save(System.IO.Path.GetFileName(outDll));
  }
  public void ReportEvent(ImporterEventKind k, int c, string m) { if (k == ImporterEventKind.ERROR_REFTOLOADFAILED) Console.WriteLine(m); }
  public Assembly ResolveRef(object t) { return null; }
}
"@
[TlbConv]::Run("$env:windir\System32\mstscax.dll", $Out)
