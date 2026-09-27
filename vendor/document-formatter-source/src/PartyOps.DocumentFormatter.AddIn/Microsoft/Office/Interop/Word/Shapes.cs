using System.Collections;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Microsoft.Office.Interop.Word;

[ComImport]
[CompilerGenerated]
[Guid("0002099F-0000-0000-C000-000000000046")]
[TypeIdentifier]
public interface Shapes : IEnumerable
{
	void _VtblGap1_3();

	[DispId(2)]
	int Count
	{
		[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		[DispId(2)]
		get;
	}

	void _VtblGap2_1();

	[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
	[DispId(0)]
	[return: MarshalAs(UnmanagedType.Interface)]
	Shape get_Item([In][MarshalAs(UnmanagedType.Struct)] ref object Index);

	void _VtblGap3_4();

	[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
	[DispId(14)]
	[return: MarshalAs(UnmanagedType.Interface)]
	Shape AddLine([In] float BeginX, [In] float BeginY, [In] float EndX, [In] float EndY, [Optional][In][MarshalAs(UnmanagedType.Struct)] ref object Anchor);

	void _VtblGap4_2();

	[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
	[DispId(17)]
	[return: MarshalAs(UnmanagedType.Interface)]
	Shape AddShape([In] int Type, [In] float Left, [In] float Top, [In] float Width, [In] float Height, [Optional][In][MarshalAs(UnmanagedType.Struct)] ref object Anchor);
}
