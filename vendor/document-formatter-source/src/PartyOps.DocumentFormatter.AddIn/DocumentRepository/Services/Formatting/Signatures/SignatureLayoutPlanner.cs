using System.Collections.Generic;
using System.Runtime.CompilerServices;
using DocumentRepository.Models.Signatures;
using DocumentRepository.Services.Hosting;

namespace DocumentRepository.Services.Formatting.Signatures;

public static class SignatureLayoutPlanner
{
	private const float OverflowTolerancePt = 0.01f;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static SignatureLayoutPlan Plan(SignatureLayoutRequest request)
	{
		if (IsInputSafe(request))
		{
			SignaturePlanDegradation degradation = SignaturePlanDegradation.None;
			List<string> list = new List<string>();
			float num = ((request.GridEnabled && request.CharsPerLine > 0) ? (request.ContentWidthPt / (float)request.CharsPerLine) : ResolveNoGridCjkPt(request.Host, request.FontSizePt));
			float widthPt = request.Date.WidthPt;
			int num2 = 0;
			for (int i = 1; i < request.SignatureLines.Count; i++)
			{
				if (request.SignatureLines[i].WidthPt > request.SignatureLines[num2].WidthPt)
				{
					num2 = i;
				}
			}
			float widthPt2 = request.SignatureLines[num2].WidthPt;
			float num3;
			float num4;
			if (!request.WithSeal)
			{
				if (!(widthPt2 < widthPt))
				{
					num3 = 2f * num;
					num4 = widthPt2 - widthPt;
				}
				else
				{
					num4 = 2f * num;
					num3 = widthPt + 4f * num - widthPt2;
				}
			}
			else
			{
				num4 = 4f * num;
				num3 = num4 - (widthPt2 - widthPt) / 2f;
			}
			List<SignaturePlannedLine> list2 = new List<SignaturePlannedLine>(request.SignatureLines.Count);
			for (int j = 0; j < request.SignatureLines.Count; j++)
			{
				SignatureLayoutLineInput signatureLayoutLineInput = request.SignatureLines[j];
				float rightIndentPt = ((j == num2) ? num3 : (request.WithSeal ? (num3 + (widthPt2 - signatureLayoutLineInput.WidthPt) / 2f) : (num3 + widthPt2 - signatureLayoutLineInput.WidthPt + 2f * num)));
				list2.Add(new SignaturePlannedLine
				{
					RightIndentPt = rightIndentPt,
					WidthPt = signatureLayoutLineInput.WidthPt,
					Source = signatureLayoutLineInput.Source,
					Morphology = signatureLayoutLineInput.Morphology
				});
			}
			SignaturePlannedLine signaturePlannedLine = new SignaturePlannedLine
			{
				RightIndentPt = num4,
				WidthPt = request.Date.WidthPt,
				Source = request.Date.Source,
				Morphology = request.Date.Morphology
			};
			if (!AreResultsFinite(list2, signaturePlannedLine, num))
			{
				return BuildUnsafePlan(request);
			}
			ApplyPhysicalConstraints(list2, request.ContentWidthPt, ref degradation, list);
			ApplyPhysicalConstraints(new List<SignaturePlannedLine> { signaturePlannedLine }, request.ContentWidthPt, ref degradation, list);
			return new SignatureLayoutPlan
			{
				SignatureLines = list2,
				Date = signaturePlannedLine,
				UnitPt = num,
				ContentWidthPt = request.ContentWidthPt,
				Degradation = degradation,
				DegradeNote = ((list.Count == 0) ? null : string.Join("；", list.ToArray()))
			};
		}
		return BuildUnsafePlan(request);
	}

	private static float ResolveNoGridCjkPt(DocumentHostKind host, float fontSizePt)
	{
		return fontSizePt;
	}

	private static bool IsInputSafe(SignatureLayoutRequest request)
	{
		if (request != null)
		{
			if (request.SignatureLines == null || request.SignatureLines.Count < 1)
			{
				return false;
			}
			if (request.Date != null)
			{
				if (IsFinite(request.ContentWidthPt) && request.ContentWidthPt > 0f)
				{
					int num = 0;
					while (true)
					{
						if (num >= request.SignatureLines.Count)
						{
							if (IsFinite(request.Date.WidthPt) && !(request.Date.WidthPt < 0f))
							{
								break;
							}
							return false;
						}
						if (request.SignatureLines[num] == null)
						{
							return false;
						}
						if (!IsFinite(request.SignatureLines[num].WidthPt) || !(request.SignatureLines[num].WidthPt >= 0f))
						{
							return false;
						}
						num++;
					}
					return true;
				}
				return false;
			}
			return false;
		}
		return false;
	}

	private static bool AreResultsFinite(IList<SignaturePlannedLine> lines, SignaturePlannedLine date, float unitPt)
	{
		if (!IsFinite(unitPt))
		{
			return false;
		}
		if (!IsFinite(date.RightIndentPt))
		{
			return false;
		}
		for (int i = 0; i < lines.Count; i++)
		{
			if (!IsFinite(lines[i].RightIndentPt))
			{
				return false;
			}
		}
		return true;
	}

	private static bool IsFinite(float value)
	{
		if (!float.IsNaN(value))
		{
			return !float.IsInfinity(value);
		}
		return false;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ApplyPhysicalConstraints(IList<SignaturePlannedLine> lines, float contentWidthPt, ref SignaturePlanDegradation degradation, IList<string> notes)
	{
		for (int i = 0; i < lines.Count; i++)
		{
			SignaturePlannedLine signaturePlannedLine = lines[i];
			if (signaturePlannedLine.WidthPt > contentWidthPt)
			{
				signaturePlannedLine.RightIndentPt = 0f;
				degradation |= SignaturePlanDegradation.ExceedsContentWidth;
				AddNote(notes, "存在宽度超出版心的落款行，该行右缩进已归零");
				continue;
			}
			if (!(signaturePlannedLine.RightIndentPt >= 0f))
			{
				signaturePlannedLine.RightIndentPt = 0f;
				degradation |= SignaturePlanDegradation.NegativeIndentClamped;
				AddNote(notes, "居中公式产生负缩进，已钳制为零");
			}
			if (!(signaturePlannedLine.WidthPt + signaturePlannedLine.RightIndentPt <= contentWidthPt + 0.01f))
			{
				signaturePlannedLine.RightIndentPt = contentWidthPt - signaturePlannedLine.WidthPt;
				degradation |= SignaturePlanDegradation.NegativeIndentClamped;
				AddNote(notes, "右缩进超出版心可用宽度，已收回版心内");
			}
		}
	}

	private static void AddNote(IList<string> notes, string note)
	{
		for (int i = 0; i < notes.Count; i++)
		{
			if (notes[i] == note)
			{
				return;
			}
		}
		notes.Add(note);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static SignatureLayoutPlan BuildUnsafePlan(SignatureLayoutRequest request)
	{
		List<SignaturePlannedLine> list = new List<SignaturePlannedLine>();
		if (request != null && request.SignatureLines != null)
		{
			for (int i = 0; i < request.SignatureLines.Count; i++)
			{
				SignatureLayoutLineInput signatureLayoutLineInput = request.SignatureLines[i];
				list.Add(new SignaturePlannedLine
				{
					RightIndentPt = 0f,
					WidthPt = ((signatureLayoutLineInput != null && IsFinite(signatureLayoutLineInput.WidthPt)) ? signatureLayoutLineInput.WidthPt : 0f),
					Source = (signatureLayoutLineInput?.Source ?? SignatureWidthSource.StaticTable),
					Morphology = (signatureLayoutLineInput?.Morphology ?? SignatureTextMorphology.Unknown)
				});
			}
		}
		float contentWidthPt = ((request != null && IsFinite(request.ContentWidthPt)) ? request.ContentWidthPt : 0f);
		SignaturePlannedLine date = new SignaturePlannedLine
		{
			RightIndentPt = 0f,
			WidthPt = ((request != null && request.Date != null && IsFinite(request.Date.WidthPt)) ? request.Date.WidthPt : 0f),
			Source = ((request != null && request.Date != null) ? request.Date.Source : SignatureWidthSource.StaticTable),
			Morphology = ((request != null && request.Date != null) ? request.Date.Morphology : SignatureTextMorphology.Unknown)
		};
		return new SignatureLayoutPlan
		{
			SignatureLines = list,
			Date = date,
			UnitPt = 0f,
			ContentWidthPt = contentWidthPt,
			Degradation = SignaturePlanDegradation.UnsafeValueRejected,
			DegradeNote = "输入含非法数值，落款缩进已整体安全归零"
		};
	}
}
