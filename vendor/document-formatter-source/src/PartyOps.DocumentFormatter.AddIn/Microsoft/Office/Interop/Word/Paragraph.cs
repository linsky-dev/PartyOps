using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Microsoft.Office.Interop.Word;

[ComImport]
[CompilerGenerated]
[Guid("00020957-0000-0000-C000-000000000046")]
[DefaultMember("Range")]
[TypeIdentifier]
public interface Paragraph
{
	[DispId(0)]
	Range Range
	{
		[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		[DispId(0)]
		[return: MarshalAs(UnmanagedType.Interface)]
		get;
	}

	void _VtblGap1_3();

	[DispId(1102)]
	ParagraphFormat Format
	{
		[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		[DispId(1102)]
		[return: MarshalAs(UnmanagedType.Interface)]
		get;
		[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		[DispId(1102)]
		[param: In]
		[param: MarshalAs(UnmanagedType.Interface)]
		set;
	}

	void _VtblGap2_5();

	[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
	[SpecialName]
	[DispId(100)]
	[return: MarshalAs(UnmanagedType.Struct)]
	object get_Style();

	void _VtblGap3_61();

	[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
	[DispId(324)]
	[return: MarshalAs(UnmanagedType.Interface)]
	Paragraph Next([Optional][In][MarshalAs(UnmanagedType.Struct)] ref object Count);

	[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
	[DispId(325)]
	[return: MarshalAs(UnmanagedType.Interface)]
	Paragraph Previous([Optional][In][MarshalAs(UnmanagedType.Struct)] ref object Count);
}
