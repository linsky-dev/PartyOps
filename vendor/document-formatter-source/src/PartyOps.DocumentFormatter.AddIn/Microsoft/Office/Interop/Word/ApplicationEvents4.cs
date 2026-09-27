using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Microsoft.Office.Interop.Word;

[ComImport]
[CompilerGenerated]
[InterfaceType(2)]
[Guid("00020A01-0000-0000-C000-000000000046")]
[TypeIdentifier]
public interface ApplicationEvents4
{
	void _VtblGap1_4();

	[MethodImpl(MethodImplOptions.PreserveSig | MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
	[DispId(6)]
	void DocumentBeforeClose([In][MarshalAs(UnmanagedType.Interface)] Document Doc, [In][Out] ref bool Cancel);
}
