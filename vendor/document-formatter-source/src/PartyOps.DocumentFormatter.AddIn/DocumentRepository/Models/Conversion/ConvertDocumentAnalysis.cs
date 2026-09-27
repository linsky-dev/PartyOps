using System;
using System.Runtime.CompilerServices;

namespace DocumentRepository.Models.Conversion;

public class ConvertDocumentAnalysis
{
	public string SourcePath { get; set; }

	public string SourceExtension { get; set; }

	public int PageCount { get; set; }

	public bool IsDocx
	{
		[MethodImpl(MethodImplOptions.NoInlining)]
		get
		{
			return string.Equals(SourceExtension, ".docx", StringComparison.OrdinalIgnoreCase);
		}
	}
}
