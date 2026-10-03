using System.Collections.Generic;
using DocumentRepository.Services.Hosting;

namespace DocumentRepository.Models.Signatures;

public sealed class SignatureLayoutRequest
{
	public DocumentHostKind Host;

	public bool GridEnabled;

	public int CharsPerLine;

	public float ContentWidthPt;

	public float FontSizePt;

	public bool WithSeal;

	public IList<SignatureLayoutLineInput> SignatureLines;

	public SignatureLayoutLineInput Date;
}
