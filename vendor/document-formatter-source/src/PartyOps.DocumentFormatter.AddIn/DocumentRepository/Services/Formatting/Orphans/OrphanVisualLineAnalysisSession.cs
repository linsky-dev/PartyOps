using System;
using System.Runtime.CompilerServices;
using DocumentRepository.Services.Hosting;
using DocumentRepository.Services.Interop;
using Microsoft.Office.Interop.Word;

namespace DocumentRepository.Services.Formatting.Orphans;

public sealed class OrphanVisualLineAnalysisSession : IDisposable
{
	private struct LineIdentity : IEquatable<LineIdentity>
	{
		private readonly int _page;

		private readonly int _line;

		public LineIdentity(int page, int line)
		{
			_page = page;
			_line = line;
		}

		public bool Equals(LineIdentity other)
		{
			if (_page == other._page)
			{
				return _line == other._line;
			}
			return false;
		}

		bool IEquatable<LineIdentity>.Equals(LineIdentity other)
		{
			return this.Equals(other);
		}
	}

	private readonly Document _document;

	private readonly int _documentEnd;

	private Microsoft.Office.Interop.Word.Range _probe;

	private bool _disposed;

	public int InformationReadCount { get; private set; }

	[MethodImpl(MethodImplOptions.NoInlining)]
	internal OrphanVisualLineAnalysisSession(Document document)
	{
		if (document == null)
		{
			throw new ArgumentNullException("document");
		}
		_document = document;
		_documentEnd = ReadDocumentEnd(document);
		object Start = 0;
		object End = 0;
		_probe = document.Range(ref Start, ref End);
	}

	public OrphanVisualLineSnapshot Analyze(int rangeStart, int rangeEnd)
	{
		EnsureNotDisposed();
		int num = Math.Max(0, Math.Min(rangeStart, _documentEnd));
		int end = Math.Max(num, Math.Min(rangeEnd, _documentEnd));
		end = TrimParagraphEnd(num, end);
		OrphanVisualLineSnapshot orphanVisualLineSnapshot = new OrphanVisualLineSnapshot
		{
			ParagraphStart = num,
			ParagraphEnd = end,
			LastLineStart = num,
			AdjustmentStart = num,
			LastLineText = string.Empty,
			IsOrphan = false
		};
		if (end > num)
		{
			_probe.SetRange(num, end);
			if (_probe.ComputeStatistics(WdStatistic.wdStatisticLines) > 1)
			{
				int num2 = FindVisualLineStart(num, end - 1);
				int adjustmentStart = ((num2 > num) ? FindVisualLineStart(num, num2 - 1) : num);
				string text = ReadText(num2, end);
				orphanVisualLineSnapshot.LastLineStart = num2;
				orphanVisualLineSnapshot.AdjustmentStart = adjustmentStart;
				orphanVisualLineSnapshot.LastLineText = CleanText(text);
				orphanVisualLineSnapshot.IsOrphan = IsSingleHanWithOptionalPunctuation(orphanVisualLineSnapshot.LastLineText);
				return orphanVisualLineSnapshot;
			}
			return orphanVisualLineSnapshot;
		}
		return orphanVisualLineSnapshot;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private int FindVisualLineStart(int paragraphStart, int caretPosition)
	{
		LineIdentity other = ReadLineIdentity(caretPosition, "孤字分析目标位置");
		int num = paragraphStart;
		int num2 = caretPosition;
		while (num < num2)
		{
			int num3 = num + (num2 - num) / 2;
			if (!ReadLineIdentity(num3, "孤字分析二分探针").Equals(other))
			{
				num = num3 + 1;
			}
			else
			{
				num2 = num3;
			}
		}
		if (!ReadLineIdentity(num, "孤字分析行首验证").Equals(other))
		{
			throw new InvalidOperationException("孤字分析未能定位视觉行起点。");
		}
		return num;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private LineIdentity ReadLineIdentity(int position, string context)
	{
		_probe.SetRange(position, position);
		int page = WordPageOrdinalService.ReadPhysicalPageOrdinal(_probe, context);
		InformationReadCount++;
		int num = Convert.ToInt32((dynamic)_probe.get_Information(WdInformation.wdFirstCharacterLineNumber));
		InformationReadCount++;
		if (num <= 0)
		{
			throw new InvalidOperationException(context + "无法读取有效的视觉行编号。");
		}
		return new LineIdentity(page, num);
	}

	private int TrimParagraphEnd(int start, int end)
	{
		string text = ReadText(start, end);
		int num = text.Length;
		while (num > 0 && IsTrailingCharacter(text[num - 1]))
		{
			num--;
		}
		return start + num;
	}

	private string ReadText(int start, int end)
	{
		_probe.SetRange(start, end);
		return _probe.Text ?? string.Empty;
	}

	private static bool IsTrailingCharacter(char value)
	{
		if (value != '\r' && value != '\n' && value != '\a' && value != ' ' && value != '\t' && value != '\u00a0')
		{
			if (value < '\u2000')
			{
				return false;
			}
			return value <= '\u200b';
		}
		return true;
	}

	private static string CleanText(string text)
	{
		return (text ?? string.Empty).Trim(new char[] { '\r', '\n', '\u0007', ' ', '\t', '\u00A0', ' ', ' ', ' ', ' ', ' ', ' ', ' ', ' ', ' ', ' ', ' ', '​' });
	}

	private static bool IsSingleHanWithOptionalPunctuation(string text)
	{
		int num = 0;
		string text2 = text ?? string.Empty;
		foreach (char c in text2)
		{
			if (c < '一' || c > '鿿')
			{
				if (char.IsLetterOrDigit(c))
				{
					return false;
				}
			}
			else
			{
				num++;
			}
		}
		return num == 1;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int ReadDocumentEnd(Document document)
	{
		Microsoft.Office.Interop.Word.Range value = null;
		try
		{
			value = document.Content;
			return value.End;
		}
		finally
		{
			ComObjectRelease.Release(ref value, "OrphanVisualLineAnalysis.Content");
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void EnsureNotDisposed()
	{
		if (_disposed)
		{
			throw new ObjectDisposedException("OrphanVisualLineAnalysisSession");
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public void Dispose()
	{
		if (!_disposed)
		{
			_disposed = true;
			ComObjectRelease.Release(ref _probe, "OrphanVisualLineAnalysis.Probe");
		}
	}

	void IDisposable.Dispose()
	{
		this.Dispose();
	}
}
