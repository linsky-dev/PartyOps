using System;
using System.Runtime.CompilerServices;
using DocumentRepository.Models;
using DocumentRepository.Models.FormattingPlan;
using DocumentRepository.Models.Tasks;
using DocumentRepository.Services.Formatting.Images;
using DocumentRepository.Services.Logging;
using DocumentRepository.Services.Mutations;
using DocumentRepository.Services.Performance;

namespace DocumentRepository.Services.Formatting.Planning;

internal static class FormatMutationApplicationService
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void Apply(FormatMutationOperation operation, MutationExecutionContext context)
	{
		if (operation != null)
		{
			FormatExecutionPlan plan = GetPlan(context);
			FormatContext formatContext = plan.FormatContext;
			if (formatContext == null)
			{
				throw new InvalidOperationException("排版计划缺少一次性识别上下文。");
			}
			FormatContext formatContext2 = formatContext;
			formatContext2.Document = context.Document;
			formatContext2.Application = context.Application;
			formatContext2.SelectionRange = context.ScopeRange;
			switch (operation.Stage)
			{
			case FormatMutationStage.ApplyEnglishNumberFont:
				EnglishNumberFontService.ApplyPlanned(context.Document, formatContext2.Config, plan.EnglishNumberFontRanges);
				break;
			case FormatMutationStage.FormatImages:
				if (plan.ImagePlan == null)
				{
					throw new InvalidOperationException("图片排版阶段缺少不可变图片计划。");
				}
				context.Items["format.image-execution-result"] = ImageFormattingService.Apply(context.Document, plan.ImagePlan, GetImageAnchors(context), context.ScopeRange);
				break;
			case FormatMutationStage.ApplyParagraphStyles:
				ApplyStyles(plan, context, formatContext2);
				break;
			case FormatMutationStage.RefreshAnchors:
				GetAnchors(context).RefreshPositions();
				break;
			case FormatMutationStage.InitializeExecutionAnchors:
				CreateAnchors(plan, context);
				break;
			case FormatMutationStage.InsertSignatureSpacing:
				FormatDocumentElementService.InsertSignatureSpacing(formatContext2);
				break;
			case FormatMutationStage.ApplyPageNumbers:
				FormatPagePreparationService.ApplyPageNumbers(context.Document, context.Application, formatContext2, GetDiagnostics(context));
				break;
			case FormatMutationStage.FormatAttachments:
				if (plan.IsSelectionMode)
				{
					FormatSelectionElementService.FormatAttachments(context.Document, context.Application, context.ScopeRange, formatContext2);
				}
				else
				{
					FormatDocumentElementService.FormatAttachments(formatContext2);
				}
				break;
			case FormatMutationStage.ApplyKeywords:
				ApplyKeywords(plan, context, formatContext2);
				break;
			case FormatMutationStage.NormalizeEastAsianPunctuationFont:
				ApplyEastAsianPunctuationFont(plan, context);
				break;
			case FormatMutationStage.ApplyPageSetup:
				ApplyPageSetup(formatContext2);
				break;
			case FormatMutationStage.FormatSignature:
				if (!plan.IsSelectionMode)
				{
					FormatDocumentElementService.FormatAnchoredSignatures(formatContext2);
				}
				else
				{
					FormatSelectionElementService.FormatSignatures(context.Document, context.Application, context.ScopeRange, formatContext2);
				}
				break;
			case FormatMutationStage.FixOrphanCharacters:
				FormatDocumentElementService.FixOrphanChars(formatContext2);
				break;
			case FormatMutationStage.ClearTabs:
				FormatDocumentElementService.ClearTabs(formatContext2);
				break;
			case FormatMutationStage.FormatTables:
				if (plan.ExecutionScope == FormatExecutionScope.FullDocument && HasValidatedStyleSkipEvidence(context) && FormatIdempotencyStampService.CanSkipTableFormatting(plan, context.Document))
				{
					LogService.Info("[FORMAT-IDEMPOTENCY] skip FormatTables");
				}
				else if (!plan.IsSelectionMode)
				{
					FormatDocumentElementService.FormatTables(formatContext2);
				}
				else
				{
					FormatSelectionElementService.FormatTables(context.Document, context.Application, context.ScopeRange, formatContext2);
				}
				break;
			default:
				throw new InvalidOperationException("未知排版变更阶段：" + operation.Stage);
			}
			return;
		}
		throw new ArgumentNullException("operation");
	}

	private static void ApplyPageSetup(FormatContext fctx)
	{
		FormatPagePreparationService.ClearHeadersFootersIfNeeded(fctx.Document, fctx.Config);
		FormatPagePreparationService.NormalizeEmptyHeaderBorders(fctx.Document);
		FormatPagePreparationService.ApplyPageSetup(fctx.Document, fctx.Application, fctx);
		FormatPagePreparationService.ClearBaseStyleResidue(fctx.Document);
	}

	private static void ApplyEastAsianPunctuationFont(FormatExecutionPlan plan, MutationExecutionContext context)
	{
		if (plan.ExecutionScope != FormatExecutionScope.FullDocument)
		{
			EastAsianPunctuationFontService.Normalize(context.ScopeRange);
		}
		else
		{
			EastAsianPunctuationFontService.Normalize(context.Document);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ApplyStyles(FormatExecutionPlan plan, MutationExecutionContext context, FormatContext fctx)
	{
		if (plan.ExecutionScope == FormatExecutionScope.FullDocument)
		{
			if (FormatIdempotencyStampService.CanSkipExpensiveFullDocumentFormatting(plan, context.Document))
			{
				context.Items["format.style-skip-evidence-validated"] = true;
				LogService.Info("[FORMAT-IDEMPOTENCY] skip ApplyParagraphStyles");
			}
			else
			{
				FormatStyleApplicationService.FormatDocument(context.Document, fctx, GetProgress(context), GetDiagnostics(context));
			}
		}
		else
		{
			FormatStyleApplicationService.FormatSelection(context.Document, context.ScopeRange, fctx);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool HasValidatedStyleSkipEvidence(MutationExecutionContext context)
	{
		if (context != null && context.Items.TryGetValue("format.style-skip-evidence-validated", out var value) && value is bool)
		{
			return (bool)value;
		}
		return false;
	}

	private static void ApplyKeywords(FormatExecutionPlan plan, MutationExecutionContext context, FormatContext fctx)
	{
		if (plan.IsSelectionMode)
		{
			FormatKeywordService.FixSemicolonsForRange(context.ScopeRange, fctx);
			FormatKeywordService.ApplyKeywordBoldForRange(context.ScopeRange, fctx);
		}
		else
		{
			FormatKeywordService.FixSemicolonsForDocument(context.Document, fctx);
			FormatKeywordService.ApplyKeywordBoldForDocument(context.Document, fctx);
			FormatKeywordService.RepairMixedHeadingBodyFonts(context.Document, fctx);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void CreateAnchors(FormatExecutionPlan plan, MutationExecutionContext context)
	{
		FormatExecutionAnchorSet formatExecutionAnchorSet = FormatExecutionAnchorSet.Create(context.Document, plan.FormatContext.Elements, plan.FormatContext.SignatureBlocks, plan.PlanId);
		context.Items["format.anchors"] = formatExecutionAnchorSet;
		context.RegisterResource(formatExecutionAnchorSet);
		if (plan.ImagePlan != null && plan.ImagePlan.HasTargets)
		{
			ImageExecutionAnchorSet imageExecutionAnchorSet = ImageExecutionAnchorSet.Create(context.Document, plan.ImagePlan.Targets);
			context.Items["format.image-anchors"] = imageExecutionAnchorSet;
			context.RegisterResource(imageExecutionAnchorSet);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static FormatExecutionAnchorSet GetAnchors(MutationExecutionContext context)
	{
		if (!context.Items.TryGetValue("format.anchors", out var value) || !(value is FormatExecutionAnchorSet))
		{
			throw new InvalidOperationException("排版执行锚点尚未建立。");
		}
		return (FormatExecutionAnchorSet)value;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static ImageExecutionAnchorSet GetImageAnchors(MutationExecutionContext context)
	{
		if (!context.Items.TryGetValue("format.image-anchors", out var value) || !(value is ImageExecutionAnchorSet))
		{
			throw new InvalidOperationException("图片排版执行锚点尚未建立。");
		}
		return (ImageExecutionAnchorSet)value;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static FormatExecutionPlan GetPlan(MutationExecutionContext context)
	{
		if (context != null)
		{
			if (!context.Items.TryGetValue("format.plan", out var value) || !(value is FormatExecutionPlan))
			{
				throw new InvalidOperationException("变更执行上下文缺少排版计划。");
			}
			return (FormatExecutionPlan)value;
		}
		throw new ArgumentNullException("context");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static ITaskProgressReporter GetProgress(MutationExecutionContext context)
	{
		if (!context.Items.TryGetValue("format.progress", out var value))
		{
			return null;
		}
		return value as ITaskProgressReporter;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static FirstFormatDiagnosticsSession GetDiagnostics(MutationExecutionContext context)
	{
		if (!context.Items.TryGetValue("format.diagnostics", out var value))
		{
			return null;
		}
		return value as FirstFormatDiagnosticsSession;
	}
}
