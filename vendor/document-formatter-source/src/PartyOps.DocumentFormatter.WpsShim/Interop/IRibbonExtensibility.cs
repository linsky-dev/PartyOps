using System.Runtime.InteropServices;

namespace PartyOps.DocumentFormatter.Wps.Interop;

/// <summary>Office/WPS Custom UI 的稳定 COM 契约。</summary>
[ComImport]
[Guid("000C0396-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsDual)]
public interface IRibbonExtensibility
{
	[return: MarshalAs(UnmanagedType.BStr)]
	string GetCustomUI([MarshalAs(UnmanagedType.BStr)] string ribbonId);
}
