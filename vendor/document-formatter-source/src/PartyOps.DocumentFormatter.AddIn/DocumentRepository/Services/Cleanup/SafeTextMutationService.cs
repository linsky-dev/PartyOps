using System;
using System.Runtime.CompilerServices;
using DocumentRepository.Services.Hosting;
using DocumentRepository.Services.Interop;
using DocumentRepository.Services.Logging;
using Microsoft.Office.Interop.Word;

namespace DocumentRepository.Services.Cleanup;

public static class SafeTextMutationService
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static bool HasProtectedContent(Microsoft.Office.Interop.Word.Range range)
	{
		if (range == null)
		{
			throw new ArgumentNullException("range");
		}
		Fields value = null;
		Hyperlinks value2 = null;
		Bookmarks value3 = null;
		Comments value4 = null;
		ContentControls value5 = null;
		try
		{
			if (WordRangeInspector.HasInlineOrAnchoredShape(range))
			{
				return true;
			}
			value = range.Fields;
			if (value != null && value.Count > 0)
			{
				return true;
			}
			value2 = range.Hyperlinks;
			if (value2 != null && value2.Count > 0)
			{
				return true;
			}
			value3 = range.Bookmarks;
			if (value3 == null || value3.Count <= 0)
			{
				value4 = range.Comments;
				if (value4 == null || value4.Count <= 0)
				{
					value5 = range.ContentControls;
					if (value5 != null && value5.Count > 0)
					{
						return true;
					}
					return false;
				}
				return true;
			}
			return true;
		}
		finally
		{
			if (value5 != null)
			{
				ComObjectRelease.Release(ref value5, "SafeTextMutationService.contentControls");
			}
			if (value4 != null)
			{
				ComObjectRelease.Release(ref value4, "SafeTextMutationService.comments");
			}
			if (value3 != null)
			{
				ComObjectRelease.Release(ref value3, "SafeTextMutationService.bookmarks");
			}
			if (value2 != null)
			{
				ComObjectRelease.Release(ref value2, "SafeTextMutationService.hyperlinks");
			}
			if (value != null)
			{
				ComObjectRelease.Release(ref value, "SafeTextMutationService.fields");
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static bool HasUnsafeContentForExactTextMutation(Microsoft.Office.Interop.Word.Range range)
	{
		if (range == null)
		{
			throw new ArgumentNullException("range");
		}
		Fields value = null;
		Hyperlinks value2 = null;
		Comments value3 = null;
		ContentControls value4 = null;
		Bookmarks value5 = null;
		try
		{
			if (!WordRangeInspector.HasInlineOrAnchoredShape(range))
			{
				value = range.Fields;
				if (value != null && value.Count > 0)
				{
					return true;
				}
				value2 = range.Hyperlinks;
				if (value2 != null && value2.Count > 0)
				{
					return true;
				}
				value3 = range.Comments;
				if (value3 == null || value3.Count <= 0)
				{
					value4 = range.ContentControls;
					if (value4 == null || value4.Count <= 0)
					{
						value5 = range.Bookmarks;
						if (value5 != null && value5.Count != 0)
						{
							int start = range.Start;
							int end = range.End;
							int count = value5.Count;
							for (int i = 1; i <= count; i++)
							{
								Bookmark value6 = null;
								Microsoft.Office.Interop.Word.Range value7 = null;
								try
								{
									Bookmarks bookmarks = value5;
									object Index = i;
									value6 = bookmarks.get_Item(ref Index);
									value7 = value6.Range;
									if (ExactTextMutationPolicy.TouchesBookmarkBoundary(start, end, value7.Start, value7.End))
									{
										return true;
									}
								}
								finally
								{
									if (value7 != null)
									{
										ComObjectRelease.Release(ref value7, "SafeTextMutationService.exactBookmarkRange");
									}
									if (value6 != null)
									{
										ComObjectRelease.Release(ref value6, "SafeTextMutationService.exactBookmark");
									}
								}
							}
							return false;
						}
						return false;
					}
					return true;
				}
				return true;
			}
			return true;
		}
		finally
		{
			if (value5 != null)
			{
				ComObjectRelease.Release(ref value5, "SafeTextMutationService.exactBookmarks");
			}
			if (value4 != null)
			{
				ComObjectRelease.Release(ref value4, "SafeTextMutationService.exactContentControls");
			}
			if (value3 != null)
			{
				ComObjectRelease.Release(ref value3, "SafeTextMutationService.exactComments");
			}
			if (value2 != null)
			{
				ComObjectRelease.Release(ref value2, "SafeTextMutationService.exactHyperlinks");
			}
			if (value != null)
			{
				ComObjectRelease.Release(ref value, "SafeTextMutationService.exactFields");
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static bool TryReplace(Microsoft.Office.Interop.Word.Range container, int relativeStart, int oldLength, string replacement, string operationName)
	{
		if (container == null)
		{
			throw new ArgumentNullException("container");
		}
		if (relativeStart >= 0)
		{
			if (oldLength >= 0)
			{
				int num = container.Start + relativeStart;
				int num2 = num + oldLength;
				if (num < container.Start || num2 > container.End)
				{
					throw new InvalidOperationException("文字修改范围超出容器边界：" + operationName);
				}
				Microsoft.Office.Interop.Word.Range value = null;
				try
				{
					value = container.Duplicate;
					value.SetRange(num, num2);
					if (HasProtectedContent(value))
					{
						LogService.Warn((operationName ?? "文字修改") + " skipped protected range start=" + num + ", end=" + num2);
						return false;
					}
					value.Text = replacement ?? string.Empty;
					return true;
				}
				finally
				{
					if (value != null)
					{
						ComObjectRelease.Release(ref value, "SafeTextMutationService.target");
					}
				}
			}
			throw new ArgumentOutOfRangeException("oldLength");
		}
		throw new ArgumentOutOfRangeException("relativeStart");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static bool TryReplaceExact(Microsoft.Office.Interop.Word.Range container, int relativeStart, int oldLength, string replacement, string operationName)
	{
		if (container == null)
		{
			throw new ArgumentNullException("container");
		}
		if (relativeStart < 0)
		{
			throw new ArgumentOutOfRangeException("relativeStart");
		}
		if (oldLength >= 0)
		{
			int num = container.Start + relativeStart;
			int num2 = num + oldLength;
			if (num < container.Start || num2 > container.End)
			{
				throw new InvalidOperationException("文字修改范围超出容器边界：" + operationName);
			}
			Microsoft.Office.Interop.Word.Range value = null;
			try
			{
				value = container.Duplicate;
				value.SetRange(num, num2);
				if (HasUnsafeContentForExactTextMutation(value))
				{
					LogService.Warn((operationName ?? "精确文字修改") + " skipped exact protected range start=" + num + ", end=" + num2);
					return false;
				}
				value.Text = replacement ?? string.Empty;
				return true;
			}
			finally
			{
				if (value != null)
				{
					ComObjectRelease.Release(ref value, "SafeTextMutationService.exactTarget");
				}
			}
		}
		throw new ArgumentOutOfRangeException("oldLength");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static bool TryReplaceWithoutProtection(Microsoft.Office.Interop.Word.Range container, int relativeStart, int oldLength, string replacement, string operationName)
	{
		if (container != null)
		{
			if (relativeStart >= 0)
			{
				if (oldLength < 0)
				{
					throw new ArgumentOutOfRangeException("oldLength");
				}
				int num = container.Start + relativeStart;
				int num2 = num + oldLength;
				if (num < container.Start || num2 > container.End)
				{
					throw new InvalidOperationException("文字修改范围超出容器边界：" + operationName);
				}
				Microsoft.Office.Interop.Word.Range value = null;
				try
				{
					value = container.Duplicate;
					value.SetRange(num, num2);
					value.Text = replacement ?? string.Empty;
					return true;
				}
				finally
				{
					if (value != null)
					{
						ComObjectRelease.Release(ref value, "SafeTextMutationService.target");
					}
				}
			}
			throw new ArgumentOutOfRangeException("relativeStart");
		}
		throw new ArgumentNullException("container");
	}

	public static int ApplyEqualLengthCharacterChanges(Microsoft.Office.Interop.Word.Range container, string original, string corrected, string operationName)
	{
		return ApplyEqualLengthCharacterChangesCore(container, original, corrected, operationName, inspectProtectedContent: true);
	}

	public static int ApplyEqualLengthCharacterChangesWithoutProtection(Microsoft.Office.Interop.Word.Range container, string original, string corrected, string operationName)
	{
		return ApplyEqualLengthCharacterChangesCore(container, original, corrected, operationName, inspectProtectedContent: false);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int ApplyEqualLengthCharacterChangesCore(Microsoft.Office.Interop.Word.Range container, string original, string corrected, string operationName, bool inspectProtectedContent)
	{
		if (container != null)
		{
			original = original ?? string.Empty;
			corrected = corrected ?? string.Empty;
			if (original.Length == corrected.Length)
			{
				if (!string.Equals(original, corrected, StringComparison.Ordinal))
				{
					if (inspectProtectedContent && HasProtectedContent(container))
					{
						LogService.Warn((operationName ?? "字符修改") + " skipped paragraph containing protected content start=" + container.Start);
						return 0;
					}
					int num = 0;
					for (int num2 = original.Length - 1; num2 >= 0; num2--)
					{
						if (original[num2] != corrected[num2] && (inspectProtectedContent ? TryReplace(container, num2, 1, corrected[num2].ToString(), operationName) : ReplaceCharacterWithoutProtection(container, num2, corrected[num2])))
						{
							num++;
						}
					}
					return num;
				}
				return 0;
			}
			throw new InvalidOperationException("等长字符修改收到长度不同的文本：" + operationName);
		}
		throw new ArgumentNullException("container");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool ReplaceCharacterWithoutProtection(Microsoft.Office.Interop.Word.Range container, int relativeStart, char replacement)
	{
		int num = container.Start + relativeStart;
		if (num < container.Start || num >= container.End)
		{
			throw new InvalidOperationException("等长字符修改范围超出容器边界。");
		}
		Microsoft.Office.Interop.Word.Range value = null;
		try
		{
			value = container.Duplicate;
			value.SetRange(num, num + 1);
			value.Text = replacement.ToString();
			return true;
		}
		finally
		{
			if (value != null)
			{
				ComObjectRelease.Release(ref value, "SafeTextMutationService.target");
			}
		}
	}
}
