using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Microsoft.Office.Interop.Word;

[ComImport]
[CompilerGenerated]
[Guid("000209C0-0000-0000-C000-000000000046")]
[TypeIdentifier]
public interface ListFormat
{
	void _VtblGap1_14();

	[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
	[DispId(186)]
	void ConvertNumbersToText([Optional][In][MarshalAs(UnmanagedType.Struct)] ref object NumberType);
}
