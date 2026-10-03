using System;

namespace DocumentRepository.Services.Formatting.Signatures;

public static class SignatureFeedbackCorrection
{
	public const float CorrectionThresholdPt = 0.56692916f;

	public static float CorrectIndent(float currentIndentPt, float errorPt, float lineWidthPt, float contentWidthPt)
	{
		if (!float.IsNaN(currentIndentPt) && !float.IsInfinity(currentIndentPt) && !float.IsNaN(errorPt) && !float.IsInfinity(errorPt) && !float.IsNaN(lineWidthPt) && !float.IsInfinity(lineWidthPt) && !float.IsNaN(contentWidthPt) && !float.IsInfinity(contentWidthPt))
		{
			float num = Math.Max(0f, contentWidthPt - lineWidthPt);
			float num2 = Math.Max(0f, Math.Min(num, currentIndentPt));
			if (!(Math.Abs(errorPt) <= 0.56692916f))
			{
				float num3 = num2 + errorPt;
				if (num3 < 0f)
				{
					return 0f;
				}
				if (num3 > num)
				{
					return num;
				}
				return num3;
			}
			return num2;
		}
		return currentIndentPt;
	}

	public static SignatureFeedbackCorrectionResult CorrectWithSeal(float currentDateIndentPt, float dateWidthPt, float[] currentSignIndentsPt, float[] signWidthsPt, float dateEndErrorPt, float[] signCenterErrorsPt, float contentWidthPt)
	{
		SignatureFeedbackCorrectionResult signatureFeedbackCorrectionResult = new SignatureFeedbackCorrectionResult();
		signatureFeedbackCorrectionResult.DateIndentPt = CorrectIndent(currentDateIndentPt, dateEndErrorPt, dateWidthPt, contentWidthPt);
		signatureFeedbackCorrectionResult.SignIndentsPt = new float[currentSignIndentsPt.Length];
		for (int i = 0; i < currentSignIndentsPt.Length; i++)
		{
			signatureFeedbackCorrectionResult.SignIndentsPt[i] = CorrectIndent(currentSignIndentsPt[i], signCenterErrorsPt[i], signWidthsPt[i], contentWidthPt);
		}
		return signatureFeedbackCorrectionResult;
	}

	public static SignatureFeedbackCorrectionResult CorrectSignLonger(float currentDateIndentPt, float dateWidthPt, float[] currentSignIndentsPt, float[] signWidthsPt, int signReferenceIndex, float[] signEndErrorsPt, float[] signStartErrorsPt, float dateStartErrorPt, float contentWidthPt)
	{
		SignatureFeedbackCorrectionResult signatureFeedbackCorrectionResult = new SignatureFeedbackCorrectionResult();
		signatureFeedbackCorrectionResult.SignIndentsPt = new float[currentSignIndentsPt.Length];
		for (int i = 0; i < currentSignIndentsPt.Length; i++)
		{
			float errorPt = ((i == signReferenceIndex) ? signEndErrorsPt[i] : signStartErrorsPt[i]);
			signatureFeedbackCorrectionResult.SignIndentsPt[i] = CorrectIndent(currentSignIndentsPt[i], errorPt, signWidthsPt[i], contentWidthPt);
		}
		signatureFeedbackCorrectionResult.DateIndentPt = CorrectIndent(currentDateIndentPt, dateStartErrorPt, dateWidthPt, contentWidthPt);
		return signatureFeedbackCorrectionResult;
	}

	public static SignatureFeedbackCorrectionResult CorrectDateLonger(float currentDateIndentPt, float dateWidthPt, float[] currentSignIndentsPt, float[] signWidthsPt, float dateEndErrorPt, float[] signStartErrorsPt, float contentWidthPt)
	{
		SignatureFeedbackCorrectionResult signatureFeedbackCorrectionResult = new SignatureFeedbackCorrectionResult();
		signatureFeedbackCorrectionResult.DateIndentPt = CorrectIndent(currentDateIndentPt, dateEndErrorPt, dateWidthPt, contentWidthPt);
		signatureFeedbackCorrectionResult.SignIndentsPt = new float[currentSignIndentsPt.Length];
		for (int i = 0; i < currentSignIndentsPt.Length; i++)
		{
			signatureFeedbackCorrectionResult.SignIndentsPt[i] = CorrectIndent(currentSignIndentsPt[i], signStartErrorsPt[i], signWidthsPt[i], contentWidthPt);
		}
		return signatureFeedbackCorrectionResult;
	}
}
