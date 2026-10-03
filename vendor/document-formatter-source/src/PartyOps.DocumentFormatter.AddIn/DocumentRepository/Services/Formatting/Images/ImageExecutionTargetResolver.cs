using System;
using System.Runtime.CompilerServices;
using DocumentRepository.Models.Images;
using DocumentRepository.Services.Detection.Images;
using DocumentRepository.Services.Hosting;
using DocumentRepository.Services.Logging;
using Microsoft.Office.Interop.Word;

namespace DocumentRepository.Services.Formatting.Images;

internal static class ImageExecutionTargetResolver
{
	private const int MaximumAnchorDrift = 32;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static InlineShape ResolveInline(Document document, ImageFormattingTarget target, int currentAnchorStart)
	{
		if (document != null)
		{
			if (target != null)
			{
				HostThreadRuntime.AssertAccess("ImageExecutionTargetResolver.ResolveInline");
				for (int i = 0; i < 2; i++)
				{
					InlineShapes value = null;
					try
					{
						value = document.InlineShapes;
						int num = 0;
						double num2 = double.MaxValue;
						int count = value.Count;
						for (int j = 1; j <= count; j++)
						{
							InlineShape value2 = null;
							Microsoft.Office.Interop.Word.Range value3 = null;
							try
							{
								value2 = value[j];
								value3 = value2.Range;
								if (!ImageObjectClassifier.ClassifyInline((int)value2.Type, (int)value3.StoryType).IsEligible)
								{
									continue;
								}
								int num3 = Math.Abs(value3.Start - currentAnchorStart);
								if (num3 <= 32)
								{
									double num4 = (double)num3 * 1000.0 + (double)Math.Abs(value2.Width - target.SourceWidthPoints) + (double)Math.Abs(value2.Height - target.SourceHeightPoints) + ((value2.Type == (WdInlineShapeType)target.TypeCode) ? 0.0 : 100.0);
									if (!(num4 >= num2))
									{
										num2 = num4;
										num = j;
									}
								}
							}
							finally
							{
								ComObjectRelease.Release(ref value3, "ImageTargetResolver.inlineAnchor");
								ComObjectRelease.Release(ref value2, "ImageTargetResolver.inlineCandidate");
							}
						}
						if (num != 0)
						{
							return value[num];
						}
					}
					catch (Exception ex)
					{
						if (i == 1)
						{
							LogService.Warn("ImageTargetResolver.inline-indeterminate", ex);
						}
					}
					finally
					{
						ComObjectRelease.Release(ref value, "ImageTargetResolver.inlineCollection");
					}
				}
				return null;
			}
			throw new ArgumentNullException("target");
		}
		throw new ArgumentNullException("document");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static Shape ResolveFloating(Document document, ImageFormattingTarget target, int currentAnchorStart)
	{
		if (document == null)
		{
			throw new ArgumentNullException("document");
		}
		if (target != null)
		{
			HostThreadRuntime.AssertAccess("ImageExecutionTargetResolver.ResolveFloating");
			for (int i = 0; i < 2; i++)
			{
				Shapes value = null;
				try
				{
					value = document.Shapes;
					int num = 0;
					double num2 = double.MaxValue;
					int count = value.Count;
					for (int j = 1; j <= count; j++)
					{
						Shape value2 = null;
						Microsoft.Office.Interop.Word.Range value3 = null;
						try
						{
							Shapes shapes = value;
							object Index = j;
							value2 = shapes.get_Item(ref Index);
							value3 = value2.Anchor;
							if (!ImageObjectClassifier.ClassifyFloating((int)value2.Type, (int)value3.StoryType).IsEligible)
							{
								continue;
							}
							int num3 = Math.Abs(value3.Start - currentAnchorStart);
							bool flag = !string.IsNullOrWhiteSpace(target.Name) && string.Equals(value2.Name, target.Name, StringComparison.Ordinal);
							if (flag || num3 <= 32)
							{
								double num4 = (flag ? 0.0 : 25.0) + (double)num3 * 1000.0 + (double)Math.Abs(value2.Width - target.SourceWidthPoints) + (double)Math.Abs(value2.Height - target.SourceHeightPoints) + ((value2.Type == target.TypeCode) ? 0.0 : 100.0);
								if (num4 < num2)
								{
									num2 = num4;
									num = j;
								}
							}
						}
						finally
						{
							ComObjectRelease.Release(ref value3, "ImageTargetResolver.shapeAnchor");
							ComObjectRelease.Release(ref value2, "ImageTargetResolver.shapeCandidate");
						}
					}
					if (num != 0)
					{
						Shapes shapes2 = value;
						object Index = num;
						return shapes2.get_Item(ref Index);
					}
				}
				catch (Exception ex)
				{
					if (i == 1)
					{
						LogService.Warn("ImageTargetResolver.floating-indeterminate", ex);
					}
				}
				finally
				{
					ComObjectRelease.Release(ref value, "ImageTargetResolver.shapeCollection");
				}
			}
			return null;
		}
		throw new ArgumentNullException("target");
	}
}
