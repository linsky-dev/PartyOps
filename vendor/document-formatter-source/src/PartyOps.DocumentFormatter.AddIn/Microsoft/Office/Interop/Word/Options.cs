using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Microsoft.Office.Interop.Word;

[ComImport]
[CompilerGenerated]
[Guid("000209B7-0000-0000-C000-000000000046")]
[TypeIdentifier]
public interface Options
{
	void _VtblGap1_21();

	[DispId(26)]
	WdMeasurementUnits MeasurementUnit
	{
		[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		[DispId(26)]
		get;
		[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		[DispId(26)]
		[param: In]
		set;
	}

	void _VtblGap2_266();

	[DispId(346)]
	bool UseCharacterUnit
	{
		[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		[DispId(346)]
		get;
		[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		[DispId(346)]
		[param: In]
		set;
	}
}
