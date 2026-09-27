using System.Globalization;
using System.Runtime.CompilerServices;
using DocumentRepository.Models.Formatting;

namespace DocumentRepository.Services.Formatting.Failures;

internal static class FormatConfigurationValidator
{
	internal static void Validate(FormatConfig config)
	{
		if (config == null)
		{
			throw FormatOperationException.Create(FormatFailureReasonCode.BuiltInDefaultInvalid, FormatFailureStage.Plan);
		}
		ValidateMargins(config);
		ValidateStyle(config.MainTitle, FormatParameterField.Unknown);
		ValidateStyle(config.Level1, FormatParameterField.Unknown);
		ValidateStyle(config.Level2, FormatParameterField.Unknown);
		ValidateStyle(config.Level3, FormatParameterField.Unknown);
		ValidateStyle(config.Body, FormatParameterField.BodyFont);
		if (config.EnablePageNumbers)
		{
			RequireFont(config.PageNumberFontName, FormatParameterField.PageNumberFont);
			RequireFontSize(config.PageFontSize, FormatParameterField.PageNumberFontSize);
		}
		if (config.EnableEnglishFont)
		{
			RequireFont(config.EnglishNumberFontName, FormatParameterField.EnglishNumberFont);
		}
		if (config.EnableTableFormatting)
		{
			if (config.TableOptions == null)
			{
				ThrowParameter(FormatFailureReasonCode.ConfigFieldInvalid, FormatParameterField.TableStyle);
			}
			RequireFont(config.TableOptions.HeaderFontName, FormatParameterField.TableStyle);
			RequireFontSize(config.TableOptions.HeaderFontSize, FormatParameterField.TableStyle);
			RequireFont(config.TableOptions.BodyFontName, FormatParameterField.TableStyle);
			RequireFontSize(config.TableOptions.BodyFontSize, FormatParameterField.TableStyle);
		}
	}

	private static void ValidateMargins(FormatConfig config)
	{
		if (!InRange(config.TopMargin, 0.5f, 10f) || !InRange(config.BottomMargin, 0.5f, 10f) || !InRange(config.LeftMargin, 0.5f, 10f) || !InRange(config.RightMargin, 0.5f, 10f) || !InRange(config.HeaderDistance, 0.5f, 5f) || !InRange(config.FooterDistance, 0.5f, 5f))
		{
			ThrowParameter(FormatFailureReasonCode.ConfigFieldInvalid, FormatParameterField.PageMargins);
		}
		if (!(config.LeftMargin + config.RightMargin < 20f) || !(config.TopMargin + config.BottomMargin < 28.7f))
		{
			ThrowParameter(FormatFailureReasonCode.PageGeometryInvalid, FormatParameterField.PageGeometry);
		}
	}

	private static void ValidateStyle(TextStyle style, FormatParameterField fontField)
	{
		if (style == null)
		{
			ThrowParameter(FormatFailureReasonCode.ConfigFieldInvalid, fontField);
		}
		RequireFont(style.FontName, fontField);
		RequireFontSize(style.FontSize, (fontField == FormatParameterField.BodyFont) ? FormatParameterField.BodyFontSize : FormatParameterField.Unknown);
	}

	private static void RequireFont(string value, FormatParameterField field)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			ThrowParameter(FormatFailureReasonCode.FontParameterInvalid, field);
		}
	}

	private static void RequireFontSize(string value, FormatParameterField field)
	{
		if (!TryFontSize(value, out var points) || !(points > 0f) || points > 200f)
		{
			ThrowParameter(FormatFailureReasonCode.FontParameterInvalid, field);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool TryFontSize(string value, out float points)
	{
		points = 0f;
		if (!string.IsNullOrWhiteSpace(value))
		{
			switch (value.Trim())
			{
			default:
				if (!float.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out points))
				{
					return float.TryParse(value.Trim(), out points);
				}
				return true;
			case "三号":
				points = 16f;
				return true;
			case "初号":
				points = 42f;
				return true;
			case "小初":
				points = 36f;
				return true;
			case "小五":
				points = 9f;
				return true;
			case "五号":
				points = 10.5f;
				return true;
			case "四号":
				points = 14f;
				return true;
			case "小一":
				points = 24f;
				return true;
			case "小三":
				points = 15f;
				return true;
			case "小六":
				points = 6.5f;
				return true;
			case "小四":
				points = 12f;
				return true;
			case "六号":
				points = 7.5f;
				return true;
			case "二号":
				points = 22f;
				return true;
			case "七号":
				points = 5.5f;
				return true;
			case "小二号":
			case "小二":
				points = 18f;
				return true;
			case "八号":
				points = 5f;
				return true;
			case "一号":
				points = 26f;
				return true;
			}
		}
		return false;
	}

	private static bool InRange(float value, float minimum, float maximum)
	{
		if (!float.IsNaN(value) && !float.IsInfinity(value) && value >= minimum)
		{
			return value <= maximum;
		}
		return false;
	}

	private static void ThrowParameter(FormatFailureReasonCode reason, FormatParameterField field)
	{
		throw FormatOperationException.Create(reason, FormatFailureStage.Plan, DocumentSafetyDisposition.Unchanged, null, field);
	}
}
