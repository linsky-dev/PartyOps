using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Microsoft.Office.Interop.Word;

[CompilerGenerated]
[ComEventInterface(typeof(ApplicationEvents4), typeof(ApplicationEvents4))]
[TypeIdentifier("00020905-0000-0000-c000-000000000046", "Microsoft.Office.Interop.Word.ApplicationEvents4_Event")]
public interface ApplicationEvents4_Event
{
	void _VtblGap1_8();

	event ApplicationEvents4_DocumentBeforeCloseEventHandler DocumentBeforeClose;
}
