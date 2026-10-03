using System.Collections.Generic;

namespace DocumentRepository.Models.Signatures;

public sealed class SignatureLayoutPlan
{
	public IList<SignaturePlannedLine> SignatureLines;

	public SignaturePlannedLine Date;

	public float UnitPt;

	public float ContentWidthPt;

	public SignaturePlanDegradation Degradation;

	public string DegradeNote;
}
