using System;
using System.Runtime.CompilerServices;
using DocumentRepository.Models.Images;
using DocumentRepository.Services.Hosting;
using DocumentRepository.Services.Logging;
using Microsoft.Office.Interop.Word;

namespace DocumentRepository.Services.Detection.Images;

public static class ImageSnapshotService
{
	internal static Action BeforeCollectionReadForTesting;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static ImageAnalysisSnapshot Capture(Document document)
	{
		if (document == null)
		{
			throw new ArgumentNullException("document");
		}
		HostThreadRuntime.AssertAccess("ImageSnapshotService.Capture.Document");
		ImageAnalysisSnapshot imageAnalysisSnapshot = new ImageAnalysisSnapshot();
		for (int i = 0; i < 2; i++)
		{
			InlineShapes value = null;
			Shapes value2 = null;
			try
			{
				InvokeCollectionReadHook();
				value = document.InlineShapes;
				value2 = document.Shapes;
				CaptureInlineShapes(value, imageAnalysisSnapshot);
				CaptureFloatingShapes(value2, imageAnalysisSnapshot);
				return imageAnalysisSnapshot;
			}
			catch (Exception ex)
			{
				if (i == 0)
				{
					imageAnalysisSnapshot = new ImageAnalysisSnapshot();
					continue;
				}
				imageAnalysisSnapshot.CountsReliable = false;
				imageAnalysisSnapshot.DetailsReliable = false;
				imageAnalysisSnapshot.FailureReasonCode = "image-collection-unreadable";
				LogService.Warn("ImageSnapshotService.Document.Collections", ex);
				return imageAnalysisSnapshot;
			}
			finally
			{
				ComObjectRelease.Release(ref value2, "ImageSnapshotService.documentShapes");
				ComObjectRelease.Release(ref value, "ImageSnapshotService.documentInlineShapes");
			}
		}
		return imageAnalysisSnapshot;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static ImageAnalysisSnapshot Capture(Microsoft.Office.Interop.Word.Range range)
	{
		if (range != null)
		{
			HostThreadRuntime.AssertAccess("ImageSnapshotService.Capture.Range");
			ImageAnalysisSnapshot imageAnalysisSnapshot = new ImageAnalysisSnapshot();
			for (int i = 0; i < 2; i++)
			{
				InlineShapes value = null;
				ShapeRange value2 = null;
				try
				{
					InvokeCollectionReadHook();
					value = range.InlineShapes;
					value2 = range.ShapeRange;
					CaptureInlineShapes(value, imageAnalysisSnapshot);
					CaptureFloatingShapes(value2, imageAnalysisSnapshot);
					return imageAnalysisSnapshot;
				}
				catch (Exception ex)
				{
					if (i != 0)
					{
						imageAnalysisSnapshot.CountsReliable = false;
						imageAnalysisSnapshot.DetailsReliable = false;
						imageAnalysisSnapshot.FailureReasonCode = "image-range-unreadable";
						LogService.Warn("ImageSnapshotService.Range.Collections", ex);
						return imageAnalysisSnapshot;
					}
					imageAnalysisSnapshot = new ImageAnalysisSnapshot();
				}
				finally
				{
					ComObjectRelease.Release(ref value2, "ImageSnapshotService.rangeShapeRange");
					ComObjectRelease.Release(ref value, "ImageSnapshotService.rangeInlineShapes");
				}
			}
			return imageAnalysisSnapshot;
		}
		throw new ArgumentNullException("range");
	}

	private static void InvokeCollectionReadHook()
	{
		BeforeCollectionReadForTesting?.Invoke();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void CaptureInlineShapes(InlineShapes shapes, ImageAnalysisSnapshot result)
	{
		if (shapes == null)
		{
			return;
		}
		int num = (result.ObservedInlineObjectCount = shapes.Count);
		for (int i = 1; i <= num; i++)
		{
			InlineShape value = null;
			Microsoft.Office.Interop.Word.Range value2 = null;
			try
			{
				value = shapes[i];
				value2 = value.Range;
				int type = (int)value.Type;
				int storyType = (int)value2.StoryType;
				ImageObjectClassification imageObjectClassification = ImageObjectClassifier.ClassifyInline(type, storyType);
				result.Objects.Add(new ImageObjectSnapshot
				{
					Ordinal = i,
					Kind = imageObjectClassification.Kind,
					IsEligible = imageObjectClassification.IsEligible,
					SkipReason = imageObjectClassification.SkipReason,
					TypeCode = type,
					StoryTypeCode = storyType,
					AnchorStart = value2.Start,
					AnchorEnd = value2.End,
					Name = "InlineShape#" + i,
					WidthPoints = value.Width,
					HeightPoints = value.Height,
					IsInTable = Convert.ToBoolean((dynamic)value2.get_Information(WdInformation.wdWithInTable)),
					IsStandaloneParagraph = IsStandaloneParagraph(value2)
				});
			}
			catch (Exception ex)
			{
				result.DetailsReliable = false;
				if (string.IsNullOrWhiteSpace(result.FailureReasonCode))
				{
					result.FailureReasonCode = "image-inline-detail-unreadable";
				}
				result.Objects.Add(CreateUnreadable(i, inline: true, ex));
				LogService.Warn("ImageSnapshotService.InlineShape index=" + i, ex);
			}
			finally
			{
				ComObjectRelease.Release(ref value2, "ImageSnapshotService.inlineAnchor");
				ComObjectRelease.Release(ref value, "ImageSnapshotService.inlineShape");
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void CaptureFloatingShapes(Shapes shapes, ImageAnalysisSnapshot result)
	{
		if (shapes == null)
		{
			return;
		}
		int num = (result.ObservedFloatingObjectCount = shapes.Count);
		for (int i = 1; i <= num; i++)
		{
			Shape value = null;
			try
			{
				object Index = i;
				value = shapes.get_Item(ref Index);
				AddFloatingSnapshot(value, i, result);
			}
			catch (Exception ex)
			{
				result.DetailsReliable = false;
				if (string.IsNullOrWhiteSpace(result.FailureReasonCode))
				{
					result.FailureReasonCode = "image-floating-detail-unreadable";
				}
				result.Objects.Add(CreateUnreadable(i, inline: false, ex));
				LogService.Warn("ImageSnapshotService.Shape index=" + i, ex);
			}
			finally
			{
				ComObjectRelease.Release(ref value, "ImageSnapshotService.shape");
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void CaptureFloatingShapes(ShapeRange shapes, ImageAnalysisSnapshot result)
	{
		if (shapes == null)
		{
			return;
		}
		int num = (result.ObservedFloatingObjectCount = shapes.Count);
		for (int i = 1; i <= num; i++)
		{
			Shape value = null;
			try
			{
				object Index = i;
				value = shapes.get_Item(ref Index);
				AddFloatingSnapshot(value, i, result);
			}
			catch (Exception ex)
			{
				result.DetailsReliable = false;
				if (string.IsNullOrWhiteSpace(result.FailureReasonCode))
				{
					result.FailureReasonCode = "image-floating-detail-unreadable";
				}
				result.Objects.Add(CreateUnreadable(i, inline: false, ex));
				LogService.Warn("ImageSnapshotService.ShapeRange index=" + i, ex);
			}
			finally
			{
				ComObjectRelease.Release(ref value, "ImageSnapshotService.rangeShape");
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void AddFloatingSnapshot(Shape shape, int index, ImageAnalysisSnapshot result)
	{
		Microsoft.Office.Interop.Word.Range value = null;
		try
		{
			value = shape.Anchor;
			int type = (int)shape.Type;
			int storyType = (int)value.StoryType;
			ImageObjectClassification imageObjectClassification = ImageObjectClassifier.ClassifyFloating(type, storyType);
			result.Objects.Add(new ImageObjectSnapshot
			{
				Ordinal = index,
				Kind = imageObjectClassification.Kind,
				IsEligible = imageObjectClassification.IsEligible,
				SkipReason = imageObjectClassification.SkipReason,
				TypeCode = type,
				StoryTypeCode = storyType,
				AnchorStart = value.Start,
				AnchorEnd = value.End,
				Name = (shape.Name ?? ("Shape#" + index)),
				WidthPoints = shape.Width,
				HeightPoints = shape.Height,
				IsInTable = Convert.ToBoolean((dynamic)value.get_Information(WdInformation.wdWithInTable)),
				IsStandaloneParagraph = IsStandaloneParagraph(value)
			});
		}
		finally
		{
			ComObjectRelease.Release(ref value, "ImageSnapshotService.floatingAnchor");
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static ImageObjectSnapshot CreateUnreadable(int index, bool inline, Exception ex)
	{
		return new ImageObjectSnapshot
		{
			Ordinal = index,
			Kind = (inline ? ImageObjectKind.UnsupportedInlineObject : ImageObjectKind.UnsupportedFloatingObject),
			IsEligible = false,
			SkipReason = "无法读取对象属性：" + ((ex == null) ? "未知错误" : ex.GetType().Name),
			Name = (inline ? "InlineShape#" : "Shape#") + index
		};
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool IsStandaloneParagraph(Microsoft.Office.Interop.Word.Range anchor)
	{
		Paragraphs value = null;
		Paragraph value2 = null;
		Microsoft.Office.Interop.Word.Range value3 = null;
		try
		{
			value = anchor.Paragraphs;
			if (value != null && value.Count == 1)
			{
				value2 = value[1];
				value3 = value2.Range;
				return (value3.Text ?? string.Empty).Replace("\r", string.Empty).Replace("\a", string.Empty).Replace("\u0001", string.Empty)
					.Replace("￼", string.Empty)
					.Trim()
					.Length == 0;
			}
			return false;
		}
		finally
		{
			ComObjectRelease.Release(ref value3, "ImageSnapshotService.standaloneRange");
			ComObjectRelease.Release(ref value2, "ImageSnapshotService.standaloneParagraph");
			ComObjectRelease.Release(ref value, "ImageSnapshotService.standaloneParagraphs");
		}
	}
}
