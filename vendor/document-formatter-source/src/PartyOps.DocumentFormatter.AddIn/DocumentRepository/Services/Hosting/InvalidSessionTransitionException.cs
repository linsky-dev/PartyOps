using System;
using System.Runtime.CompilerServices;

namespace DocumentRepository.Services.Hosting;

public sealed class InvalidSessionTransitionException : InvalidOperationException
{
	public DocumentSessionState From { get; private set; }

	public DocumentSessionState To { get; private set; }

	[MethodImpl(MethodImplOptions.NoInlining)]
	public InvalidSessionTransitionException(DocumentSessionState from, DocumentSessionState to)
		: base("非法会话状态迁移：" + from.ToString() + " → " + to)
	{
		From = from;
		To = to;
	}
}
