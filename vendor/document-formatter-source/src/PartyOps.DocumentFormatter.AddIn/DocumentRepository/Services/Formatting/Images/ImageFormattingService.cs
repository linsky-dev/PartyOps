using System;
using System.Runtime.CompilerServices;
using DocumentRepository.Models.Images;
using DocumentRepository.Services.Hosting;
using DocumentRepository.Services.Logging;
using DocumentRepository.Services.Safety;
using Microsoft.Office.Interop.Word;

namespace DocumentRepository.Services.Formatting.Images;

internal static class ImageFormattingService
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static ImageFormattingExecutionResult Apply(Document document, ImageFormattingPlan plan, ImageExecutionAnchorSet anchors, Microsoft.Office.Interop.Word.Range scopeRange)
	{
		if (document == null)
		{
			throw new ArgumentNullException("document");
		}
		if (plan == null)
		{
			throw new ArgumentNullException("plan");
		}
		if (plan.Options != null)
		{
			if (anchors == null)
			{
				throw new ArgumentNullException("anchors");
			}
			HostThreadRuntime.AssertAccess("ImageFormattingService.Apply");
			ImageFormattingExecutionResult imageFormattingExecutionResult = new ImageFormattingExecutionResult();
			if (!plan.HasTargets)
			{
				return imageFormattingExecutionResult;
			}
			int num = 0;
			int num2 = 0;
			Microsoft.Office.Interop.Word.Range value = null;
			try
			{
				value = ((scopeRange == null) ? document.Content : scopeRange.Duplicate);
				foreach (ImageFormattingTarget target in plan.Targets)
				{
					if (!ApplyTarget(document, target, anchors.GetCurrentStart(target), plan.Options))
					{
						imageFormattingExecutionResult.SkippedCount++;
						continue;
					}
					if (!target.IsInTable && ImageCaptionFormattingService.FormatFollowingCaption(document, anchors.GetCurrentStart(target), value.Start, value.End, plan.Options))
					{
						num2++;
					}
					num++;
					imageFormattingExecutionResult.AppliedCount++;
					if (!target.SourceIsInline || !ImageFormattingPlanBuilder.RequiresFloating(plan.Options.WrapMode))
					{
						if (!target.SourceIsInline && plan.Options.WrapMode == "Inline")
						{
							imageFormattingExecutionResult.AppliedFloatingToInlineCount++;
						}
					}
					else
					{
						imageFormattingExecutionResult.AppliedInlineToFloatingCount++;
					}
				}
			}
			finally
			{
				ComObjectRelease.Release(ref value, "ImageFormattingService.liveScope");
			}
			LogService.Info("图片排版完成：计划=" + plan.Targets.Count + "，执行=" + num + "，内嵌转浮动=" + plan.InlineToFloatingCount + "，浮动转内嵌=" + plan.FloatingToInlineCount + "，跳过=" + imageFormattingExecutionResult.SkippedCount + "，已格式化题注=" + num2);
			return imageFormattingExecutionResult;
		}
		throw new InvalidOperationException("图片排版计划缺少参数快照。");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool ApplyTarget(Document document, ImageFormattingTarget target, int currentAnchorStart, ImageFormatOptions options)
	{
		InlineShape value = null;
		Shape value2 = null;
		try
		{
			if (target.SourceIsInline)
			{
				value = ImageExecutionTargetResolver.ResolveInline(document, target, currentAnchorStart);
				if (value != null)
				{
					if (!ImageFormattingPlanBuilder.RequiresFloating(options.WrapMode))
					{
						ImageStyleApplier.ApplyInline(value, options);
					}
					else
					{
						value2 = value.ConvertToShape();
						ComObjectRelease.Release(ref value, "ImageFormattingService.convertedInline");
						ImageStyleApplier.ApplyFloating(value2, options);
					}
					return true;
				}
				ReportUnresolvedTarget(target);
				return false;
			}
			value2 = ImageExecutionTargetResolver.ResolveFloating(document, target, currentAnchorStart);
			if (value2 != null)
			{
				if (options.WrapMode == "Inline")
				{
					value = value2.ConvertToInlineShape();
					ComObjectRelease.Release(ref value2, "ImageFormattingService.convertedFloating");
					ImageStyleApplier.ApplyInline(value, options);
				}
				else
				{
					ImageStyleApplier.ApplyFloating(value2, options);
				}
				return true;
			}
			ReportUnresolvedTarget(target);
			return false;
		}
		catch (Exception innerException)
		{
			throw new InvalidOperationException("图片排版失败：位置=" + target.AnchorStart + "，对象=" + (target.Name ?? target.SourceKind.ToString()), innerException);
		}
		finally
		{
			ComObjectRelease.Release(ref value2, "ImageFormattingService.floating");
			ComObjectRelease.Release(ref value, "ImageFormattingService.inline");
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ReportUnresolvedTarget(ImageFormattingTarget target)
	{
		LogService.Warn("FORMAT-QUALITY image-target-unresolved kind=" + target.SourceKind.ToString() + ", ordinal=" + target.SourceOrdinal);
		ExecutionWarningCollector.Report("format-image-target-unresolved", "image", "warn.format.image");
	}
}
