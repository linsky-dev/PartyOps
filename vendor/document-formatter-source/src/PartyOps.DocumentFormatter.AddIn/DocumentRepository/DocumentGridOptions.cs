using System;

namespace DocumentRepository;

[Serializable]
public class DocumentGridOptions
{
	public int OptionsVersion { get; set; }

	public int LinesPerPage { get; set; }

	public int CharsPerLine { get; set; }

	public DocumentGridOptions()
	{
		OptionsVersion = 0;
		LinesPerPage = 22;
		CharsPerLine = 28;
	}
}
