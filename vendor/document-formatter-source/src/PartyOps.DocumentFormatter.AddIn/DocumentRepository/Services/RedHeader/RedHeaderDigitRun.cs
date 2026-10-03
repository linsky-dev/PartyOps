using System;
using System.Runtime.CompilerServices;

namespace DocumentRepository.Services.RedHeader;

public sealed class RedHeaderDigitRun
{
	public int Start { get; private set; }

	public int Length { get; private set; }

	[MethodImpl(MethodImplOptions.NoInlining)]
	public RedHeaderDigitRun(int start, int length)
	{
		if (start < 0)
		{
			throw new ArgumentOutOfRangeException("start");
		}
		if (length <= 0)
		{
			throw new ArgumentOutOfRangeException("length");
		}
		Start = start;
		Length = length;
	}
}
