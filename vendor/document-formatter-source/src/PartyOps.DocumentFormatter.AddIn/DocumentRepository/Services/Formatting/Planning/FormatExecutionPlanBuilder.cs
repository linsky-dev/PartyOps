using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using DocumentRepository.Models;
using DocumentRepository.Models.Formatting;
using DocumentRepository.Models.FormattingPlan;
using DocumentRepository.Models.Images;
using DocumentRepository.Models.Mutations;
using DocumentRepository.Models.Snapshots;
using DocumentRepository.Models.Tasks;
using DocumentRepository.Services.Analysis;
using DocumentRepository.Services.Detection.Images;
using DocumentRepository.Services.Formatting.Images;
using DocumentRepository.Services.Hosting;
using DocumentRepository.Services.Logging;
using DocumentRepository.Services.Performance;
using DocumentRepository.Services.Snapshots;
using Microsoft.Office.Interop.Word;

namespace DocumentRepository.Services.Formatting.Planning;

public static class FormatExecutionPlanBuilder
{
	public static FormatExecutionPlan PrepareAndBuild(OperationContext context, Microsoft.Office.Interop.Word.Range scopeRange, bool isSelectionMode, DocumentSession session, ITaskProgressReporter progress, FirstFormatDiagnosticsSession diagnostics = null)
	{
		return PrepareAndBuild(context, scopeRange, isSelectionMode ? FormatExecutionScope.NormalSelection : FormatExecutionScope.FullDocument, session, progress, diagnostics, captureOutsideScope: true);
	}

	public static FormatExecutionPlan PrepareAndBuild(OperationContext context, Microsoft.Office.Interop.Word.Range scopeRange, FormatExecutionScope scope, DocumentSession session, ITaskProgressReporter progress)
	{
		return PrepareAndBuild(context, scopeRange, scope, session, progress, null, captureOutsideScope: true);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static FormatExecutionPlan PrepareAndBuild(OperationContext context, Microsoft.Office.Interop.Word.Range scopeRange, FormatExecutionScope scope, DocumentSession session, ITaskProgressReporter progress, FirstFormatDiagnosticsSession diagnostics, bool captureOutsideScope)
	{
		bool flag = scope == FormatExecutionScope.NormalSelection;
		bool scopedRange = scope != FormatExecutionScope.FullDocument;
		bool isCompilationArticle = scope == FormatExecutionScope.CompilationArticle;
		if (context == null)
		{
			throw new ArgumentNullException("context");
		}
		if (context.Application == null)
		{
			throw new InvalidOperationException("排版规划缺少 Word/WPS 应用实例。");
		}
		if (context.Document == null)
		{
			throw new InvalidOperationException("排版规划缺少活动文档。");
		}
		if (context.CurrentConfig == null)
		{
			throw new InvalidOperationException("排版规划缺少排版参数。");
		}
		if (scopedRange && scopeRange == null)
		{
			throw new InvalidOperationException("范围排版规划缺少范围。");
		}
		if (session == null)
		{
			throw new InvalidOperationException("排版规划缺少受控文档会话。");
		}
		Stopwatch stopwatch = Stopwatch.StartNew();
		ReportProgress(progress, 3, "读取文档", "建立原始安全快照");
		FormatExecutionPlan plan = new FormatExecutionPlan
		{
			FeatureId = (isCompilationArticle ? "format-compilation-article" : (flag ? "format-selection" : "format")),
			ExecutionScope = scope,
			ScopeStart = ((scopeRange != null) ? scopeRange.Start : 0),
			ScopeEnd = ((scopeRange != null) ? scopeRange.End : 0),
			OriginalSnapshot = RunStage(FormatFailureStage.Snapshot, () => (!scopedRange) ? DocumentSnapshotService.CaptureSafetyBaseline(context.Document) : DocumentSnapshotService.Capture(context.Document, scopeRange)),
			OutsideScopeSnapshot = ((scopedRange && captureOutsideScope) ? RunStage(FormatFailureStage.Snapshot, () => DocumentSnapshotService.CaptureOutsideScope(context.Document, scopeRange)) : null)
		};
		ImageAnalysisSnapshot protectedImageBaseline = RunStage(FormatFailureStage.Snapshot, () => (scopeRange != null) ? ImageSnapshotService.Capture(scopeRange) : ImageSnapshotService.Capture(context.Document));
		if (flag)
		{
			session.RegisterRestoreSnapshot(plan.OriginalSnapshot);
		}
		long elapsedMilliseconds = stopwatch.ElapsedMilliseconds;
		diagnostics?.Mark("original-snapshot", 1, reliable: true, "safety baseline captured");
		ReportProgress(progress, 7, "读取文档", "安全快照已建立");
		FormatConfig effectiveConfig = context.CurrentConfig;
		if (isCompilationArticle)
		{
			effectiveConfig = ConfigManager.DeepClone(context.CurrentConfig);
			if (effectiveConfig.MainTitle == null)
			{
				effectiveConfig.MainTitle = new TextStyle();
			}
			effectiveConfig.MainTitle.RecognitionStyle = "首段为标题";
		}
		FormatContext formatContext = new FormatContext
		{
			Application = context.Application,
			Document = context.Document,
			SelectionRange = scopeRange,
			IsSelectionMode = scopedRange,
			HostKind = session.Capabilities.Kind,
			Config = effectiveConfig,
			PreserveSectionBreaksInCleanup = isCompilationArticle
		};
		session.SetStage("format-normalize-once");
		session.MarkChangesStarted();
		DocumentAnalysisResult structural = RunStage(FormatFailureStage.Normalize, () => NormalizeTargetOnce(formatContext, progress));
		long elapsedMilliseconds2 = stopwatch.ElapsedMilliseconds;
		diagnostics?.Mark("normalize", 1, reliable: true, "document normalized once");
		plan.ScopeStart = ((scopeRange != null) ? scopeRange.Start : 0);
		plan.ScopeEnd = ((scopeRange != null) ? scopeRange.End : 0);
		ReportProgress(progress, 23, "排版分析", "建立规范化基线");
		DocumentSnapshot executionSourceSnapshot = RunStage(FormatFailureStage.Snapshot, () => DocumentSnapshotService.Capture(context.Document, scopeRange));
		plan.SourceSnapshot = executionSourceSnapshot;
		long elapsedMilliseconds3 = stopwatch.ElapsedMilliseconds;
		diagnostics?.Mark("baseline-snapshot", 1, reliable: true, "execution source snapshot captured");
		session.SetStage("format-analyze-once");
		ReportProgress(progress, 27, "排版分析", "识别标题、正文和特殊结构");
		DocumentAnalysisResult analysis = RunStage(FormatFailureStage.Analyze, () => (!scopedRange) ? DocumentAnalysisService.AnalyzeElements(context.Document, effectiveConfig, structural) : DocumentAnalysisService.AnalyzeElements(scopeRange, effectiveConfig, isCompilationArticle));
		long elapsedMilliseconds4 = stopwatch.ElapsedMilliseconds;
		diagnostics?.Mark("analyze", analysis?.ParagraphCount ?? 0, reliable: true, "document elements analyzed");
		if (isCompilationArticle)
		{
			formatContext.IsSelectionMode = false;
		}
		MergeSourceOnlyStructureFacts(analysis, plan.OriginalSnapshot.ProtectedObjects);
		NormalizePlannedElementTypes(analysis);
		ApplySignatureFormattingPolicy(analysis, context.CurrentConfig);
		ParagraphTextAnalysisService.RefreshKeywordFactsAfterTypeNormalization(analysis);
		FormatAnalysisStateService.Apply(formatContext, analysis);
		if (context.CurrentConfig.EnableSignatureFormatting)
		{
			SignatureBlockAnalysisService.Detect(formatContext);
			SignatureFormatOptions signatureFormatOptions = context.CurrentConfig.SignatureOptions ?? new SignatureFormatOptions();
			LogService.Info("落款排版：启用，" + (signatureFormatOptions.WithSeal ? "加盖公章" : "不加盖公章") + "，正文前空" + signatureFormatOptions.BlankLinesBefore + "行");
		}
		else
		{
			formatContext.SignatureBlocks.Clear();
			formatContext.SignatureIndex = -1;
			formatContext.DateIndex = -1;
			LogService.Info("落款排版：关闭，署名和日期按普通正文处理");
		}
		plan.FormatContext = formatContext;
		ReportProgress(progress, 31, "排版分析", "规划英文数字字体范围");
		plan.EnglishNumberFontRanges.AddRange(RunStage(FormatFailureStage.Plan, () => EnglishNumberFontRangePlanner.Build(analysis.Elements, context.CurrentConfig)));
		if (context.CurrentConfig.EnableImageFormatting)
		{
			ReportProgress(progress, 32, "排版分析", "规划真实图片排版");
			ImageAnalysisSnapshot currentImageSnapshot = RunStage(FormatFailureStage.Analyze, () => (scopeRange != null) ? ImageSnapshotService.Capture(scopeRange) : ImageSnapshotService.Capture(context.Document));
			plan.ImagePlan = RunStage(FormatFailureStage.Plan, () => ImageFormattingPlanBuilder.BuildWithProtectionBaseline(currentImageSnapshot, protectedImageBaseline, context.CurrentConfig.ImageOptions ?? new ImageFormatOptions(), executionSourceSnapshot.ScopeStart, executionSourceSnapshot.ScopeEnd));
			LogService.Info("图片计划：模式=排版并保护，可处理=" + currentImageSnapshot.EligibleCount + "，已跳过=" + currentImageSnapshot.SkippedCount + "，高级筛选排除=" + plan.ImagePlan.ProtectedEligibleObjects.Count + "，本次执行=" + plan.ImagePlan.Targets.Count);
		}
		else
		{
			plan.ImagePlan = RunStage(FormatFailureStage.Plan, () => ImageFormattingPlanBuilder.BuildProtectionOnly(protectedImageBaseline, executionSourceSnapshot.ScopeStart, executionSourceSnapshot.ScopeEnd));
			LogService.Info("图片计划：模式=仅保护，可处理=" + protectedImageBaseline.EligibleCount + "，已跳过=" + protectedImageBaseline.SkippedCount + "，本次执行=0");
		}
		long elapsedMilliseconds5 = stopwatch.ElapsedMilliseconds;
		diagnostics?.Mark("post-analysis", plan.EnglishNumberFontRanges.Count, reliable: true, "image plan and english ranges built");
		RunStage(FormatFailureStage.Plan, delegate
		{
			AddOperations(plan);
			plan.Seal();
		});
		diagnostics?.Mark("seal", plan.Operations.Count, reliable: true, "plan sealed");
		ReportProgress(progress, 33, "排版分析", "排版计划已生成");
		LogService.Info("[FORMAT-PERF] plan normalize=" + elapsedMilliseconds2 + "ms, analyze-and-seal=" + (stopwatch.ElapsedMilliseconds - elapsedMilliseconds2) + "ms, total=" + stopwatch.ElapsedMilliseconds + "ms, paragraphs=" + ((analysis != null) ? analysis.ParagraphCount : 0) + ", englishRanges=" + plan.EnglishNumberFontRanges.Count);
		LogService.Info("[FORMAT-PERF-PLAN] original-snapshot=" + elapsedMilliseconds + "ms, normalize=" + (elapsedMilliseconds2 - elapsedMilliseconds) + "ms, baseline-snapshot=" + (elapsedMilliseconds3 - elapsedMilliseconds2) + "ms, analyze=" + (elapsedMilliseconds4 - elapsedMilliseconds3) + "ms, post-analyze=" + (elapsedMilliseconds5 - elapsedMilliseconds4) + "ms, seal=" + (stopwatch.ElapsedMilliseconds - elapsedMilliseconds5) + "ms");
		return plan;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void MergeSourceOnlyStructureFacts(DocumentAnalysisResult analysis, ProtectedObjectSnapshot sourceObjects)
	{
		if (analysis != null)
		{
			if (sourceObjects != null && (sourceObjects.InlineShapes > 0 || sourceObjects.Shapes > 0))
			{
				analysis.HasImages = true;
				if (analysis.Elements != null)
				{
					analysis.Elements.HasImages = true;
				}
			}
			return;
		}
		throw new ArgumentNullException("analysis");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static DocumentAnalysisResult NormalizeTargetOnce(FormatContext fctx, ITaskProgressReporter progress)
	{
		if (!fctx.IsSelectionMode)
		{
			ReportProgress(progress, 8, "规范化内容", "转换项目编号");
			FormatDocumentPreparationService.ConvertDocumentNumbersToText(fctx.Document);
			ReportProgress(progress, 11, "规范化内容", "读取文档结构");
			LogService.Info("[FORMAT-PREP] structural-analysis-start");
			DocumentAnalysisResult documentAnalysisResult = DocumentAnalysisService.AnalyzeStructuralFlags(fctx.Document);
			LogService.Info("[FORMAT-PREP] structural-analysis-complete");
			FormatAnalysisStateService.Apply(fctx, documentAnalysisResult);
			ReportProgress(progress, 13, "规范化内容", "清理空白和标点");
			FormatDocumentCleanupService.CleanDocument(fctx.Document, fctx);
			ReportProgress(progress, 21, "规范化内容", "整理附件编号");
			FormatDocumentCleanupService.NormalizeAttachmentListsForDocument(fctx);
			return documentAnalysisResult;
		}
		ReportProgress(progress, 8, "规范化内容", "转换项目编号");
		FormatDocumentPreparationService.ConvertRangeNumbersToText(fctx.SelectionRange);
		ReportProgress(progress, 13, "规范化内容", "清理选区空白和标点");
		FormatDocumentCleanupService.CleanSelectionRange(fctx.Document, fctx.SelectionRange, fctx);
		ReportProgress(progress, 21, "规范化内容", "整理附件编号");
		FormatDocumentCleanupService.NormalizeAttachmentListsForSelection(fctx);
		return null;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ReportProgress(ITaskProgressReporter progress, int current, string stage, string step)
	{
		if (progress != null)
		{
			if (progress.CancellationRequested)
			{
				throw new OperationCanceledException("用户已取消一键排版（" + step + "）。");
			}
			progress.Report(TaskProgressInfo.Create("一键排版", current, 100, step, stage));
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static T RunStage<T>(FormatFailureStage stage, Func<T> action)
	{
		if (action == null)
		{
			throw new ArgumentNullException("action");
		}
		try
		{
			return action();
		}
		catch (FormatOperationException)
		{
			throw;
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception innerException)
		{
			throw new FormatStageException(stage, innerException);
		}
	}

	private static void RunStage(FormatFailureStage stage, Action action)
	{
		RunStage(stage, delegate
		{
			action();
			return (object)null;
		});
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void AddOperations(FormatExecutionPlan plan)
	{
		Add(plan, FormatMutationStage.InitializeExecutionAnchors, DocumentMutationKind.VerificationCheckpoint, "建立执行锚点");
		if (plan.ExecutionScope == FormatExecutionScope.FullDocument)
		{
			Add(plan, FormatMutationStage.ApplyPageSetup, DocumentMutationKind.PageSetup, "应用页面参数和基础格式");
		}
		Add(plan, FormatMutationStage.ApplyParagraphStyles, DocumentMutationKind.Style, "应用已规划的段落样式");
		Add(plan, FormatMutationStage.ApplyKeywords, DocumentMutationKind.CharacterFormat, "处理分号、关键词和混合标题字体");
		if (plan.FormatContext.HasTables)
		{
			Add(plan, FormatMutationStage.FormatTables, DocumentMutationKind.Structure, "排版表格");
		}
		if (plan.EnglishNumberFontRanges.Count > 0 && plan.FormatContext.Config.EnableEnglishFont)
		{
			Add(plan, FormatMutationStage.ApplyEnglishNumberFont, DocumentMutationKind.CharacterFormat, "设置英文数字字体");
		}
		if (plan.ExecutionScope != FormatExecutionScope.NormalSelection && plan.FormatContext.HasTabs)
		{
			Add(plan, FormatMutationStage.ClearTabs, DocumentMutationKind.CharacterText, "清理已识别制表符");
		}
		if (plan.ExecutionScope != FormatExecutionScope.NormalSelection && plan.FormatContext.SignatureBlocks.Count > 0)
		{
			Add(plan, FormatMutationStage.InsertSignatureSpacing, DocumentMutationKind.Structure, "插入落款前空行");
			Add(plan, FormatMutationStage.RefreshAnchors, DocumentMutationKind.VerificationCheckpoint, "刷新已识别元素位置");
		}
		if (plan.FormatContext.SignatureBlocks.Count > 0)
		{
			Add(plan, FormatMutationStage.FormatSignature, DocumentMutationKind.ParagraphFormat, "排版落款");
		}
		if (plan.FormatContext.Config.EnableAttachmentFormatting && plan.FormatContext.HasAttachments)
		{
			Add(plan, FormatMutationStage.RefreshAnchors, DocumentMutationKind.VerificationCheckpoint, "刷新附件元素位置");
			Add(plan, FormatMutationStage.FormatAttachments, DocumentMutationKind.Structure, "排版附件结构");
		}
		Add(plan, FormatMutationStage.RefreshAnchors, DocumentMutationKind.VerificationCheckpoint, "最终刷新已识别元素位置");
		Add(plan, FormatMutationStage.NormalizeEastAsianPunctuationFont, DocumentMutationKind.CharacterFormat, "修正中文标点字体槽");
		if (plan.ExecutionScope == FormatExecutionScope.FullDocument)
		{
			Add(plan, FormatMutationStage.ApplyPageNumbers, DocumentMutationKind.PageSetup, "设置页码");
		}
		if (plan.ExecutionScope == FormatExecutionScope.FullDocument && plan.FormatContext.HasOrphanCandidates && plan.FormatContext.Config.EnableOrphanCharFix)
		{
			Add(plan, FormatMutationStage.FixOrphanCharacters, DocumentMutationKind.CharacterFormat, "按最终分页处理孤字");
		}
		if (plan.ImagePlan != null && plan.ImagePlan.HasTargets)
		{
			Add(plan, FormatMutationStage.FormatImages, DocumentMutationKind.Structure, "排版真实图片");
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void NormalizePlannedElementTypes(DocumentAnalysisResult analysis)
	{
		if (analysis != null && analysis.Elements != null && analysis.Elements.Items != null)
		{
			int count = analysis.Elements.Items.Count;
			ElementType[] array = new ElementType[count];
			for (int i = 0; i < count; i++)
			{
				array[i] = analysis.Elements.Items[i]?.Type ?? ElementType.Unknown;
			}
			ParagraphTypeSequenceNormalizer.NormalizeOpeningTitleRegion(array);
			SalutationContextNormalizer.Normalize(analysis.Elements.Items, array);
			for (int j = 0; j < count; j++)
			{
				if (analysis.Elements.Items[j] != null)
				{
					analysis.Elements.Items[j].Type = array[j];
				}
			}
			return;
		}
		throw new InvalidOperationException("排版规划缺少段落元素事实。");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ApplySignatureFormattingPolicy(DocumentAnalysisResult analysis, FormatConfig cfg)
	{
		if (analysis == null)
		{
			throw new ArgumentNullException("analysis");
		}
		if (cfg != null)
		{
			if (cfg.EnableSignatureFormatting || analysis.Elements == null || analysis.Elements.Items == null)
			{
				return;
			}
			{
				foreach (DocumentElement item in analysis.Elements.Items)
				{
					if (item != null && (item.Type == ElementType.Signature || item.Type == ElementType.SignatureDate))
					{
						item.Type = ElementType.Body;
					}
				}
				return;
			}
		}
		throw new ArgumentNullException("cfg");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void Add(FormatExecutionPlan plan, FormatMutationStage stage, DocumentMutationKind kind, string description)
	{
		plan.AddOperation(new FormatMutationOperation
		{
			Id = "format-" + plan.Operations.Count.ToString("D2") + "-" + stage,
			Description = description,
			Kind = kind,
			Stage = stage
		});
	}
}
