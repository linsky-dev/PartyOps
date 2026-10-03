using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Microsoft.Office.Interop.Word;

[ComImport]
[CompilerGenerated]
[DefaultMember("Item")]
[Guid("804CD967-F83B-432D-9446-C61A45CFEFF0")]
[TypeIdentifier]
public interface ContentControls : IEnumerable
{
	void _VtblGap1_4();

	[DispId(103)]
	int Count
	{
		[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		[DispId(103)]
		get;
	}
}
