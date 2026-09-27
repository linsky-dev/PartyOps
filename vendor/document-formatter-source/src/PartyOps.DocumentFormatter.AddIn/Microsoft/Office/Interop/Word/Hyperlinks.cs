using System.Collections;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Microsoft.Office.Interop.Word;

[ComImport]
[CompilerGenerated]
[Guid("0002099C-0000-0000-C000-000000000046")]
[TypeIdentifier]
public interface Hyperlinks : IEnumerable
{
	void _VtblGap1_3();

	[DispId(1)]
	int Count
	{
		[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		[DispId(1)]
		get;
	}

	void _VtblGap2_1();

	[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
	[DispId(0)]
	[return: MarshalAs(UnmanagedType.Interface)]
	Hyperlink get_Item([In][MarshalAs(UnmanagedType.Struct)] ref object Index);

	void _VtblGap3_1();

	[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
	[DispId(101)]
	[return: MarshalAs(UnmanagedType.Interface)]
	Hyperlink Add([In][MarshalAs(UnmanagedType.IDispatch)] object Anchor, [Optional][In][MarshalAs(UnmanagedType.Struct)] ref object Address, [Optional][In][MarshalAs(UnmanagedType.Struct)] ref object SubAddress, [Optional][In][MarshalAs(UnmanagedType.Struct)] ref object ScreenTip, [Optional][In][MarshalAs(UnmanagedType.Struct)] ref object TextToDisplay, [Optional][In][MarshalAs(UnmanagedType.Struct)] ref object Target);
}
