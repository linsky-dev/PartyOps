using System;
using System.Runtime.CompilerServices;

namespace DocumentRepository.Models.Replace;

public sealed class ReplaceStageTarget
{
	public int Start { get; private set; }

	public int End { get; private set; }

	public string OriginalText { get; private set; }

	public string ReplacementText { get; private set; }

	public int Length => End - Start;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public ReplaceStageTarget(int start, int end, string originalText, string replacementText)
	{
		if (start < 0)
		{
			throw new ArgumentOutOfRangeException("start");
		}
		if (end <= start)
		{
			throw new ArgumentOutOfRangeException("end");
		}
		if (originalText == null)
		{
			throw new ArgumentNullException("originalText");
		}
		if (replacementText == null)
		{
			throw new ArgumentNullException("replacementText");
		}
		if (originalText.Length != end - start)
		{
			throw new ArgumentException("替换目标范围与原始文本长度不一致。", "originalText");
		}
		Start = start;
		End = end;
		OriginalText = originalText;
		ReplacementText = replacementText;
	}
}
