using System.Collections;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Microsoft.Office.Interop.Word;

[ComImport]
[CompilerGenerated]
[Guid("0002098C-0000-0000-C000-000000000046")]
[TypeIdentifier]
public interface StoryRanges : IEnumerable
{
	void _VtblGap1_5();

	[DispId(0)]
	Range this[[In] WdStoryType Index]
	{
		[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		[DispId(0)]
		[return: MarshalAs(UnmanagedType.Interface)]
		get;
	}
}
