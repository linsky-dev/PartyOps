using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Microsoft.Office.Interop.Word;

[ComImport]
[CompilerGenerated]
[DefaultMember("Type")]
[Guid("000209B8-0000-0000-C000-000000000046")]
[TypeIdentifier]
public interface Dialog
{
	void _VtblGap1_5();

	[DispId(0)]
	WdWordDialog Type
	{
		[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		[DispId(0)]
		get;
	}

	[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
	[DispId(336)]
	int Show([Optional][In][MarshalAs(UnmanagedType.Struct)] ref object TimeOut);
}
