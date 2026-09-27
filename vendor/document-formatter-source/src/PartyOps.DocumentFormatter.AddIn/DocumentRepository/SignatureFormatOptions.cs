using System;

namespace DocumentRepository;

[Serializable]
public class SignatureFormatOptions
{
	public int OptionsVersion { get; set; }

	public bool WithSeal { get; set; }

	public int BlankLinesBefore { get; set; }

	public SignatureFormatOptions()
	{
		OptionsVersion = 0;
		WithSeal = true;
		BlankLinesBefore = 2;
	}
}
