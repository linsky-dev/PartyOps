using Extensibility;
using System.Runtime.InteropServices;
using PartyOps.DocumentFormatter.Wps.Interop;

namespace PartyOps.DocumentFormatter.Wps;

/// <summary>
/// WPS Writer 可直接激活的薄 COM 入口。
/// 保持此类不声明业务字段或额外接口，所有行为均继承自非 COM 核心类。
/// </summary>
[ComVisible(true)]
[Guid(ClassId)]
[ProgId(ProgId)]
[ClassInterface(ClassInterfaceType.None)]
[ComDefaultInterface(typeof(IRibbonCallbacks))]
public sealed class WpsComAddInEntryPoint : WpsComAddInCore, IDTExtensibility2, IRibbonExtensibility, IRibbonCallbacks
{
	public const string ClassId = "7843E826-447C-484C-BB7E-EAA85A5BCC3F";
	public const string ProgId = "PartyOps.DocumentFormatter.WpsAddIn";
}
