using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Microsoft.Office.Interop.Word;

[ComImport]
[CompilerGenerated]
[DefaultMember("Name")]
[Guid("0002096B-0000-0000-C000-000000000046")]
[TypeIdentifier]
public interface _Document
{
	[DispId(0)]
	string Name
	{
		[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		[DispId(0)]
		[return: MarshalAs(UnmanagedType.BStr)]
		get;
	}

	[DispId(1)]
	Application Application
	{
		[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		[DispId(1)]
		[return: MarshalAs(UnmanagedType.Interface)]
		get;
	}

	void _VtblGap1_3();

	[DispId(2)]
	object CustomDocumentProperties
	{
		[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		[DispId(2)]
		[return: MarshalAs(UnmanagedType.IDispatch)]
		get;
	}

	[DispId(3)]
	string Path
	{
		[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		[DispId(3)]
		[return: MarshalAs(UnmanagedType.BStr)]
		get;
	}

	[DispId(4)]
	Bookmarks Bookmarks
	{
		[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		[DispId(4)]
		[return: MarshalAs(UnmanagedType.Interface)]
		get;
	}

	[DispId(6)]
	Tables Tables
	{
		[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		[DispId(6)]
		[return: MarshalAs(UnmanagedType.Interface)]
		get;
	}

	void _VtblGap2_2();

	[DispId(9)]
	Comments Comments
	{
		[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		[DispId(9)]
		[return: MarshalAs(UnmanagedType.Interface)]
		get;
	}

	void _VtblGap3_9();

	[DispId(15)]
	Sections Sections
	{
		[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		[DispId(15)]
		[return: MarshalAs(UnmanagedType.Interface)]
		get;
	}

	[DispId(16)]
	Paragraphs Paragraphs
	{
		[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		[DispId(16)]
		[return: MarshalAs(UnmanagedType.Interface)]
		get;
	}

	void _VtblGap4_3();

	[DispId(20)]
	Fields Fields
	{
		[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		[DispId(20)]
		[return: MarshalAs(UnmanagedType.Interface)]
		get;
	}

	void _VtblGap5_1();

	[DispId(22)]
	Styles Styles
	{
		[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		[DispId(22)]
		[return: MarshalAs(UnmanagedType.Interface)]
		get;
	}

	void _VtblGap6_5();

	[DispId(29)]
	string FullName
	{
		[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		[DispId(29)]
		[return: MarshalAs(UnmanagedType.BStr)]
		get;
	}

	void _VtblGap7_3();

	[DispId(1101)]
	PageSetup PageSetup
	{
		[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		[DispId(1101)]
		[return: MarshalAs(UnmanagedType.Interface)]
		get;
		[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		[DispId(1101)]
		[param: In]
		[param: MarshalAs(UnmanagedType.Interface)]
		set;
	}

	void _VtblGap8_7();

	[DispId(40)]
	bool Saved
	{
		[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		[DispId(40)]
		get;
		[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		[DispId(40)]
		[param: In]
		set;
	}

	[DispId(41)]
	Range Content
	{
		[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		[DispId(41)]
		[return: MarshalAs(UnmanagedType.Interface)]
		get;
	}

	[DispId(42)]
	Window ActiveWindow
	{
		[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		[DispId(42)]
		[return: MarshalAs(UnmanagedType.Interface)]
		get;
	}

	void _VtblGap9_2();

	[DispId(44)]
	bool ReadOnly
	{
		[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		[DispId(44)]
		get;
	}

	void _VtblGap10_14();

	[DispId(56)]
	StoryRanges StoryRanges
	{
		[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		[DispId(56)]
		[return: MarshalAs(UnmanagedType.Interface)]
		get;
	}

	void _VtblGap11_2();

	[DispId(59)]
	int SaveFormat
	{
		[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		[DispId(59)]
		get;
	}

	[DispId(60)]
	WdProtectionType ProtectionType
	{
		[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		[DispId(60)]
		get;
	}

	[DispId(61)]
	Hyperlinks Hyperlinks
	{
		[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		[DispId(61)]
		[return: MarshalAs(UnmanagedType.Interface)]
		get;
	}

	[DispId(62)]
	Shapes Shapes
	{
		[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		[DispId(62)]
		[return: MarshalAs(UnmanagedType.Interface)]
		get;
	}

	void _VtblGap12_6();

	[DispId(68)]
	InlineShapes InlineShapes
	{
		[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		[DispId(68)]
		[return: MarshalAs(UnmanagedType.Interface)]
		get;
	}

	void _VtblGap13_72();

	[DispId(314)]
	bool TrackRevisions
	{
		[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		[DispId(314)]
		get;
		[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		[DispId(314)]
		[param: In]
		set;
	}

	void _VtblGap14_4();

	[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
	[DispId(1105)]
	void Close([Optional][In][MarshalAs(UnmanagedType.Struct)] ref object SaveChanges, [Optional][In][MarshalAs(UnmanagedType.Struct)] ref object OriginalFormat, [Optional][In][MarshalAs(UnmanagedType.Struct)] ref object RouteDocument);

	void _VtblGap15_1();

	[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
	[DispId(103)]
	void Repaginate();

	void _VtblGap16_5();

	[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
	[DispId(108)]
	void Save();

	void _VtblGap17_2();

	[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
	[DispId(2000)]
	[return: MarshalAs(UnmanagedType.Interface)]
	Range Range([Optional][In][MarshalAs(UnmanagedType.Struct)] ref object Start, [Optional][In][MarshalAs(UnmanagedType.Struct)] ref object End);

	void _VtblGap18_1();

	[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
	[DispId(113)]
	void Activate();

	void _VtblGap19_1();

	[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
	[DispId(115)]
	[return: MarshalAs(UnmanagedType.Interface)]
	Range GoTo([Optional][In][MarshalAs(UnmanagedType.Struct)] ref object What, [Optional][In][MarshalAs(UnmanagedType.Struct)] ref object Which, [Optional][In][MarshalAs(UnmanagedType.Struct)] ref object Count, [Optional][In][MarshalAs(UnmanagedType.Struct)] ref object Name);

	[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
	[DispId(116)]
	bool Undo([Optional][In][MarshalAs(UnmanagedType.Struct)] ref object Times);

	void _VtblGap20_1();

	[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
	[DispId(118)]
	int ComputeStatistics([In] WdStatistic Statistic, [Optional][In][MarshalAs(UnmanagedType.Struct)] ref object IncludeFootnotesAndEndnotes);

	void _VtblGap21_182();

	[DispId(508)]
	ContentControls ContentControls
	{
		[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		[DispId(508)]
		[return: MarshalAs(UnmanagedType.Interface)]
		get;
	}

	void _VtblGap22_41();

	[DispId(542)]
	string WordOpenXML
	{
		[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		[DispId(542)]
		[return: MarshalAs(UnmanagedType.BStr)]
		get;
	}

	void _VtblGap23_6();

	[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
	[DispId(552)]
	void ExportAsFixedFormat([In][MarshalAs(UnmanagedType.BStr)] string OutputFileName, [In] WdExportFormat ExportFormat, [In] bool OpenAfterExport, [In] WdExportOptimizeFor OptimizeFor, [In] WdExportRange Range, [In] int From, [In] int To, [In] WdExportItem Item, [In] bool IncludeDocProps, [In] bool KeepIRM, [In] WdExportCreateBookmarks CreateBookmarks, [In] bool DocStructureTags, [In] bool BitmapMissingFonts, [In] bool UseISO19005_1, [Optional][In][MarshalAs(UnmanagedType.Struct)] ref object FixedFormatExtClassPtr);

	void _VtblGap24_16();

	[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
	[DispId(568)]
	void SaveAs2([Optional][In][MarshalAs(UnmanagedType.Struct)] ref object FileName, [Optional][In][MarshalAs(UnmanagedType.Struct)] ref object FileFormat, [Optional][In][MarshalAs(UnmanagedType.Struct)] ref object LockComments, [Optional][In][MarshalAs(UnmanagedType.Struct)] ref object Password, [Optional][In][MarshalAs(UnmanagedType.Struct)] ref object AddToRecentFiles, [Optional][In][MarshalAs(UnmanagedType.Struct)] ref object WritePassword, [Optional][In][MarshalAs(UnmanagedType.Struct)] ref object ReadOnlyRecommended, [Optional][In][MarshalAs(UnmanagedType.Struct)] ref object EmbedTrueTypeFonts, [Optional][In][MarshalAs(UnmanagedType.Struct)] ref object SaveNativePictureFormat, [Optional][In][MarshalAs(UnmanagedType.Struct)] ref object SaveFormsData, [Optional][In][MarshalAs(UnmanagedType.Struct)] ref object SaveAsAOCELetter, [Optional][In][MarshalAs(UnmanagedType.Struct)] ref object Encoding, [Optional][In][MarshalAs(UnmanagedType.Struct)] ref object InsertLineBreaks, [Optional][In][MarshalAs(UnmanagedType.Struct)] ref object AllowSubstitutions, [Optional][In][MarshalAs(UnmanagedType.Struct)] ref object LineEnding, [Optional][In][MarshalAs(UnmanagedType.Struct)] ref object AddBiDiMarks, [Optional][In][MarshalAs(UnmanagedType.Struct)] ref object CompatibilityMode);
}
