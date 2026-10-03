using System.Collections;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Microsoft.Office.Interop.Word;

[ComImport]
[CompilerGenerated]
[Guid("0002094D-0000-0000-C000-000000000046")]
[TypeIdentifier]
public interface Tables : IEnumerable
{
	void _VtblGap1_1();

	[DispId(2)]
	int Count
	{
		[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		[DispId(2)]
		get;
	}

	void _VtblGap2_3();

	[DispId(0)]
	Table this[[In] int Index]
	{
		[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		[DispId(0)]
		[return: MarshalAs(UnmanagedType.Interface)]
		get;
	}

	void _VtblGap3_1();

	[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
	[DispId(200)]
	[return: MarshalAs(UnmanagedType.Interface)]
	Table Add([In][MarshalAs(UnmanagedType.Interface)] Range Range, [In] int NumRows, [In] int NumColumns, [Optional][In][MarshalAs(UnmanagedType.Struct)] ref object DefaultTableBehavior, [Optional][In][MarshalAs(UnmanagedType.Struct)] ref object AutoFitBehavior);
}
