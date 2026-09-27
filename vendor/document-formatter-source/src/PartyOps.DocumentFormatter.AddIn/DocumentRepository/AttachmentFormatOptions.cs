using System;
using System.Runtime.CompilerServices;

namespace DocumentRepository;

[Serializable]
public class AttachmentFormatOptions
{
	public bool FormatAttachmentList { get; set; }

	public bool FormatAttachmentBody { get; set; }

	public string AttachmentMarkerFontName { get; set; }

	public string AttachmentMarkerFontSize { get; set; }

	public string AttachmentBodyFontName { get; set; }

	public string AttachmentBodyFontSize { get; set; }

	[MethodImpl(MethodImplOptions.NoInlining)]
	public AttachmentFormatOptions()
	{
		FormatAttachmentList = true;
		FormatAttachmentBody = true;
		AttachmentMarkerFontName = "方正小标宋简体";
		AttachmentMarkerFontSize = "二号";
		AttachmentBodyFontName = "仿宋_GB2312";
		AttachmentBodyFontSize = "三号";
	}
}
