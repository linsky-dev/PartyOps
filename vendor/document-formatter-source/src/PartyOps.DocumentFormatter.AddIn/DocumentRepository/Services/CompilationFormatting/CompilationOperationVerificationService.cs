using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using DocumentRepository.Models.CompilationFormatting;
using DocumentRepository.Models.Mutations;
using DocumentRepository.Models.Snapshots;
using DocumentRepository.Services.Snapshots;
using Microsoft.Office.Interop.Word;

namespace DocumentRepository.Services.CompilationFormatting;

internal static class CompilationOperationVerificationService
{
	internal static Action BeforeVerificationForTesting;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static VerificationReceipt Verify(string taskId, string planId, DocumentSnapshot sourceSnapshot, string ruleContentHash, Document document, int expectedArticleCount, CompilationFormatOptions options, bool fullDocument, CompilationTocOutcome tocOutcome, FormatConfig formatConfig = null)
	{
		if (sourceSnapshot == null)
		{
			throw new InvalidOperationException("汇编排版验证缺少修改前完整快照。");
		}
		if (document == null)
		{
			throw new InvalidOperationException("汇编排版验证缺少活动文档。");
		}
		if (expectedArticleCount > 0)
		{
			BeforeVerificationForTesting?.Invoke();
			CompilationManifest compilationManifest = CompilationManifestService.Read(document);
			if (CompilationManifestService.ValidateManifestStructure(compilationManifest, out var _))
			{
				if (compilationManifest.Articles.Count == expectedArticleCount)
				{
					string failureReason;
					bool titlesReliable;
					CompilationArticleList compilationArticleList = CompilationBoundaryService.TryReadFromBoundaries(document, formatConfig, out failureReason, out titlesReliable);
					if (compilationArticleList != null && compilationArticleList.Articles.Count == expectedArticleCount)
					{
						if (titlesReliable)
						{
							for (int i = 0; i < compilationArticleList.Articles.Count; i++)
							{
								CompilationArticleInfo compilationArticleInfo = compilationManifest.Articles[i];
								CompilationArticleInfo compilationArticleInfo2 = compilationArticleList.Articles[i];
								if (compilationArticleInfo2.OrderIndex != compilationArticleInfo.OrderIndex || !string.Equals(compilationArticleInfo2.BeginBookmarkName, compilationArticleInfo.BeginBookmarkName, StringComparison.Ordinal) || !string.Equals(compilationArticleInfo2.EndBookmarkName, compilationArticleInfo.EndBookmarkName, StringComparison.Ordinal) || string.IsNullOrWhiteSpace(compilationArticleInfo2.Title))
								{
									throw new InvalidOperationException("第 " + (i + 1) + " 篇文章边界或标题未通过最终验证。");
								}
							}
							if (fullDocument && options != null && options.GenerateToc && (tocOutcome == CompilationTocOutcome.Created || tocOutcome == CompilationTocOutcome.Updated))
							{
								if (!document.Bookmarks.Exists("SXCF_TOC_BEGIN") || !document.Bookmarks.Exists("SXCF_TOC_END"))
								{
									throw new InvalidOperationException("汇编目录区域未通过最终验证。");
								}
								List<CompilationTocEntry> list = new List<CompilationTocEntry>();
								foreach (CompilationArticleInfo article in compilationArticleList.Articles)
								{
									list.Add(new CompilationTocEntry
									{
										Title = article.Title,
										BookmarkName = article.BeginBookmarkName,
										OrderIndex = article.OrderIndex
									});
								}
								CompilationTocOwnershipService.VerifyCanonicalState(document, list, options.TocOptions);
							}
							DocumentSnapshot documentSnapshot = DocumentSnapshotService.Capture(document);
							VerifyProtectedObjects(sourceSnapshot.ProtectedObjects, documentSnapshot.ProtectedObjects);
							List<string> list2 = new List<string> { "manifest-current-and-valid", "article-boundaries-paired", "article-count-matches-confirmation", "article-titles-readable", "protected-objects-conserved" };
							if (fullDocument && options != null && options.GenerateToc && (tocOutcome == CompilationTocOutcome.Created || tocOutcome == CompilationTocOutcome.Updated))
							{
								list2.Add("toc-region-paired");
								list2.Add("toc-plugin-region-unique");
							}
							return new VerificationReceipt(taskId, planId, sourceSnapshot.SnapshotId, list2, new VerificationFinding[0], ruleContentHash);
						}
						throw new InvalidOperationException("汇编文章标题未通过最终验证。");
					}
					throw new InvalidOperationException("汇编文章边界未通过最终验证。");
				}
				throw new InvalidOperationException("汇编文章数量与执行前确认结果不一致。");
			}
			throw new InvalidOperationException("汇编文章清单未通过最终验证。");
		}
		throw new InvalidOperationException("汇编排版验证缺少预期文章数量。");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void VerifyProtectedObjects(ProtectedObjectSnapshot expected, ProtectedObjectSnapshot actual)
	{
		if (expected == null || actual == null)
		{
			throw new InvalidOperationException("汇编排版的受保护对象快照不可用。");
		}
		AssertEqual("表格", expected.Tables, actual.Tables);
		AssertEqual("嵌入图片", expected.InlineShapes, actual.InlineShapes);
		AssertEqual("浮动对象", expected.Shapes, actual.Shapes);
		AssertEqual("批注", expected.Comments, actual.Comments);
		AssertEqual("内容控件", expected.ContentControls, actual.ContentControls);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void AssertEqual(string objectName, int expected, int actual)
	{
		if (expected != actual)
		{
			throw new InvalidOperationException(objectName + "数量在汇编排版前后不一致。");
		}
	}
}
