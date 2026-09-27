using System.Collections;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Microsoft.Office.Interop.Word;

[ComImport]
[CompilerGenerated]
[Guid("0002092D-0000-0000-C000-000000000046")]
[TypeIdentifier]
public interface Styles : IEnumerable
{
	void _VtblGap1_5();

	[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
	[DispId(0)]
	[return: MarshalAs(UnmanagedType.Interface)]
	Style get_Item([In][MarshalAs(UnmanagedType.Struct)] ref object Index);

	[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
	[DispId(100)]
	[return: MarshalAs(UnmanagedType.Interface)]
	Style Add([In][MarshalAs(UnmanagedType.BStr)] string Name, [Optional][In][MarshalAs(UnmanagedType.Struct)] ref object Type);
}
