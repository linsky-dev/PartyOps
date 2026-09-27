using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Microsoft.Office.Interop.Word;

[ComImport]
[CompilerGenerated]
[Guid("00020955-0000-0000-C000-000000000046")]
[DefaultMember("Item")]
[TypeIdentifier]
public interface TabStops : IEnumerable
{
	void _VtblGap1_6();

	[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
	[DispId(100)]
	[return: MarshalAs(UnmanagedType.Interface)]
	TabStop Add([In] float Position, [Optional][In][MarshalAs(UnmanagedType.Struct)] ref object Alignment, [Optional][In][MarshalAs(UnmanagedType.Struct)] ref object Leader);

	[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
	[DispId(101)]
	void ClearAll();
}
