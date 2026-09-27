using System.Runtime.InteropServices;

namespace PartyOps.DocumentFormatter.Wps.Interop;

/// <summary>
/// WPS 通过 IDispatch 按名称查找 Ribbon 回调；显式调度接口可在禁用自动类接口的同时，
/// 稳定暴露按钮动作和 Ribbon 初始化入口。
/// </summary>
[ComVisible(true)]
[Guid("5F4287F1-B36C-4DFB-A31D-B4E919098513")]
[InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
public interface IRibbonCallbacks
{
	[DispId(1)]
	void OnRibbonLoad([MarshalAs(UnmanagedType.IDispatch)] object ribbon);

	[DispId(2)]
	void OnFormat([MarshalAs(UnmanagedType.IDispatch)] object control);

	[DispId(3)]
	void OnReplace([MarshalAs(UnmanagedType.IDispatch)] object control);

	[DispId(4)]
	void OnRedHeader([MarshalAs(UnmanagedType.IDispatch)] object control);

	[DispId(5)]
	void OnRename([MarshalAs(UnmanagedType.IDispatch)] object control);

	[DispId(6)]
	void OnConvert([MarshalAs(UnmanagedType.IDispatch)] object control);

	[DispId(7)]
	void OnPdfToWord([MarshalAs(UnmanagedType.IDispatch)] object control);

	[DispId(8)]
	void OnConvertDocx([MarshalAs(UnmanagedType.IDispatch)] object control);

	[DispId(9)]
	void OnConvertPdf([MarshalAs(UnmanagedType.IDispatch)] object control);

	[DispId(10)]
	void OnConvertImage([MarshalAs(UnmanagedType.IDispatch)] object control);

	[DispId(11)]
	void OnConvertTxt([MarshalAs(UnmanagedType.IDispatch)] object control);

	[DispId(12)]
	void OnFormatSettings([MarshalAs(UnmanagedType.IDispatch)] object control);

	[DispId(13)]
	void OnReplaceSettings([MarshalAs(UnmanagedType.IDispatch)] object control);

	[DispId(14)]
	void OnRedHeaderSettings([MarshalAs(UnmanagedType.IDispatch)] object control);

	[DispId(15)]
	void OnRenameSettings([MarshalAs(UnmanagedType.IDispatch)] object control);

	[DispId(16)]
	void OnConvertSettings([MarshalAs(UnmanagedType.IDispatch)] object control);

}
