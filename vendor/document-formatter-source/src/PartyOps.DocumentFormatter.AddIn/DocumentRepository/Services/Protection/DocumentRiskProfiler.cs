using System;
using System.IO;
using System.Runtime.CompilerServices;
using DocumentRepository.Models.Protection;
using DocumentRepository.Services.Hosting;
using DocumentRepository.Services.Logging;
using DocumentRepository.Services.Recovery;
using Microsoft.Office.Interop.Word;

namespace DocumentRepository.Services.Protection;

public static class DocumentRiskProfiler
{
	private delegate T StoryPartReader<T>(Section section);

	private sealed class PathProbe
	{
		public bool ProbeSucceeded { get; set; }

		public bool HasPathIdentity { get; set; }

		public bool HasStableLocalPath { get; set; }
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static DocumentRiskProfile Capture(Document document)
	{
		HostThreadRuntime.AssertAccess("DocumentRiskProfiler.Capture");
		if (document != null)
		{
			string orCreate = DocumentLifecycleRegistry.GetOrCreate(document);
			bool? isSaved = TryReadSaved(document);
			bool? isReadOnly = TryReadFlag(document, (Document d) => d.ReadOnly);
			bool? isProtected = TryReadProtected(document);
			PathProbe pathProbe = ProbePath(document);
			bool isTrulyBlank = isReadOnly.HasValue && isProtected.HasValue && pathProbe.ProbeSucceeded && !pathProbe.HasPathIdentity && !pathProbe.HasStableLocalPath && IsTrulyBlank(document);
			DocumentRiskState num = DocumentRiskClassifier.Classify(isSaved, isReadOnly, isProtected, pathProbe.ProbeSucceeded, pathProbe.HasPathIdentity, pathProbe.HasStableLocalPath, isTrulyBlank);
			return Profile(num, num != DocumentRiskState.PathUnavailable && pathProbe.HasStableLocalPath, isSaved, orCreate);
		}
		throw new ArgumentNullException("document");
	}

	private static DocumentRiskProfile Profile(DocumentRiskState state, bool hasStablePath, bool? isSaved, string documentLifecycleId)
	{
		return new DocumentRiskProfile(state, hasStablePath, isSaved, documentLifecycleId);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool IsTrulyBlank(Document document)
	{
		try
		{
			Microsoft.Office.Interop.Word.Range value = null;
			try
			{
				value = document.Content;
				if (HasVisibleText(value.Text))
				{
					return false;
				}
			}
			finally
			{
				if (value != null)
				{
					ComObjectRelease.Release(ref value, "RiskProfiler.content");
				}
			}
			if (CollectionIsEmpty(() => document.Tables, (Tables c) => c.Count, "tables"))
			{
				if (!CollectionIsEmpty(() => document.InlineShapes, (InlineShapes c) => c.Count, "inline-shapes"))
				{
					return false;
				}
				if (CollectionIsEmpty(() => document.Shapes, (Shapes c) => c.Count, "shapes"))
				{
					if (!CollectionIsEmpty(() => document.Fields, (Fields c) => c.Count, "fields"))
					{
						return false;
					}
					if (!CollectionIsEmpty(() => document.Comments, (Comments c) => c.Count, "comments"))
					{
						return false;
					}
					if (CollectionIsEmpty(() => document.Bookmarks, (Bookmarks c) => c.Count, "bookmarks"))
					{
						if (CollectionIsEmpty(() => document.ContentControls, (ContentControls c) => c.Count, "content-controls"))
						{
							if (AnyHeaderFooterHasContent(document))
							{
								return false;
							}
							return true;
						}
						return false;
					}
					return false;
				}
				return false;
			}
			return false;
		}
		catch (Exception ex)
		{
			LogService.Warn("DocumentRiskProfiler.blank-probe-failed type=" + ex.GetType().Name);
			return false;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool CollectionIsEmpty<T>(Func<T> acquire, Func<T, int> readCount, string name) where T : class
	{
		T value = null;
		try
		{
			value = acquire();
			return readCount(value) == 0;
		}
		catch (Exception ex)
		{
			LogService.Warn("DocumentRiskProfiler.probe-" + name + " type=" + ex.GetType().Name);
			return false;
		}
		finally
		{
			if (value != null)
			{
				ComObjectRelease.Release(ref value, "RiskProfiler." + name);
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool AnyHeaderFooterHasContent(Document document)
	{
		Sections value = null;
		try
		{
			value = document.Sections;
			int count = value.Count;
			if (count != 1)
			{
				return true;
			}
			for (int i = 1; i <= count; i++)
			{
				Section value2 = null;
				try
				{
					value2 = value[i];
					if (StoryPartHasContent(value2, (Section s) => s.Headers, "headers"))
					{
						return true;
					}
					if (StoryPartHasContent(value2, (Section s) => s.Footers, "footers"))
					{
						return true;
					}
				}
				finally
				{
					if (value2 != null)
					{
						ComObjectRelease.Release(ref value2, "RiskProfiler.section");
					}
				}
			}
			return false;
		}
		catch (Exception ex)
		{
			LogService.Warn("DocumentRiskProfiler.probe-stories type=" + ex.GetType().Name);
			return true;
		}
		finally
		{
			if (value != null)
			{
				ComObjectRelease.Release(ref value, "RiskProfiler.sections");
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool StoryPartHasContent(Section section, StoryPartReader<HeadersFooters> reader, string name)
	{
		HeadersFooters value = null;
		try
		{
			value = reader(section);
			WdHeaderFooterIndex[] array = new WdHeaderFooterIndex[3]
			{
				WdHeaderFooterIndex.wdHeaderFooterPrimary,
				WdHeaderFooterIndex.wdHeaderFooterFirstPage,
				WdHeaderFooterIndex.wdHeaderFooterEvenPages
			};
			foreach (WdHeaderFooterIndex index in array)
			{
				HeaderFooter part = null;
				try
				{
					part = value[index];
					if (!part.Exists)
					{
						continue;
					}
					Microsoft.Office.Interop.Word.Range range = null;
					try
					{
						range = part.Range;
						if (HasVisibleText(range.Text))
						{
							return true;
						}
						if (SafeCollectionCount(() => range.Tables, (Tables c) => c.Count, name + "-tables") != 0)
						{
							return true;
						}
						if (SafeCollectionCount(() => range.InlineShapes, (InlineShapes c) => c.Count, name + "-inline-shapes") == 0)
						{
							if (SafeCollectionCount(() => range.Fields, (Fields c) => c.Count, name + "-fields") != 0)
							{
								return true;
							}
							if (SafeCollectionCount(() => range.ContentControls, (ContentControls c) => c.Count, name + "-cc") != 0)
							{
								return true;
							}
							if (SafeCollectionCount(() => part.Shapes, (Shapes c) => c.Count, name + "-shapes") != 0)
							{
								return true;
							}
							continue;
						}
						return true;
					}
					finally
					{
						if (range != null)
						{
							ComObjectRelease.Release(ref range, "RiskProfiler.hf-range");
						}
					}
				}
				finally
				{
					if (part != null)
					{
						ComObjectRelease.Release(ref part, "RiskProfiler.hf-part");
					}
				}
			}
			return false;
		}
		catch (Exception ex)
		{
			LogService.Warn("DocumentRiskProfiler.probe-" + name + " type=" + ex.GetType().Name);
			return true;
		}
		finally
		{
			if (value != null)
			{
				ComObjectRelease.Release(ref value, "RiskProfiler.hf-parts");
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int SafeCollectionCount<T>(Func<T> acquire, Func<T, int> readCount, string name) where T : class
	{
		T value = null;
		try
		{
			value = acquire();
			return readCount(value);
		}
		catch (Exception ex)
		{
			LogService.Warn("DocumentRiskProfiler.count-" + name + " type=" + ex.GetType().Name);
			return -1;
		}
		finally
		{
			if (value != null)
			{
				ComObjectRelease.Release(ref value, "RiskProfiler." + name);
			}
		}
	}

	private static bool HasVisibleText(string text)
	{
		if (string.IsNullOrEmpty(text))
		{
			return false;
		}
		foreach (char c in text)
		{
			if (c != '\r' && c != '\a' && c != '\v' && c != '\f' && c != ' ' && c != '\t' && c != '\u3000')
			{
				return true;
			}
		}
		return false;
	}

	private static bool? TryReadSaved(Document document)
	{
		try
		{
			return document.Saved;
		}
		catch
		{
			return null;
		}
	}

	private static bool? TryReadFlag(Document document, Func<Document, bool> reader)
	{
		try
		{
			return reader(document);
		}
		catch
		{
			return null;
		}
	}

	private static bool? TryReadProtected(Document document)
	{
		try
		{
			return document.ProtectionType != WdProtectionType.wdNoProtection;
		}
		catch
		{
			return null;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static PathProbe ProbePath(Document document)
	{
		try
		{
			string path = document.Path;
			string fullName = document.FullName;
			bool flag = !string.IsNullOrWhiteSpace(fullName) && Path.IsPathRooted(fullName);
			bool flag2 = false;
			if (!string.IsNullOrWhiteSpace(fullName))
			{
				flag2 = Uri.TryCreate(fullName, UriKind.Absolute, out Uri result) && !result.IsFile;
			}
			bool hasPathIdentity = !string.IsNullOrWhiteSpace(path) || flag || flag2;
			bool flag3 = false;
			if (flag && !string.IsNullOrWhiteSpace(path))
			{
				string fullPath = Path.GetFullPath(fullName);
				bool num = File.Exists(fullPath);
				flag3 = num && SavedBaselineValidator.IsUsable(fullPath);
				if (num && !flag3)
				{
					LogService.Info("DocumentRiskProfiler.local-baseline-unusable");
				}
			}
			return new PathProbe
			{
				ProbeSucceeded = true,
				HasPathIdentity = hasPathIdentity,
				HasStableLocalPath = flag3
			};
		}
		catch
		{
			return new PathProbe
			{
				ProbeSucceeded = false
			};
		}
	}
}
