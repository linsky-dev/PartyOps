using System;
using System.Runtime.CompilerServices;

namespace DocumentRepository.Models.Formatting;

public sealed class FormatStageException : Exception
{
	public FormatFailureStage Stage { get; private set; }

	[MethodImpl(MethodImplOptions.NoInlining)]
	internal FormatStageException(FormatFailureStage stage, Exception innerException)
		: base("format-stage-failed:" + stage, innerException)
	{
		if (innerException != null)
		{
			Stage = stage;
			return;
		}
		throw new ArgumentNullException("innerException");
	}
}
