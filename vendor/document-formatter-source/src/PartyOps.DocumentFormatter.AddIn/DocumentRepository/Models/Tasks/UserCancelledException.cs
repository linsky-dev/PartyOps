using System;
using System.Runtime.CompilerServices;

namespace DocumentRepository.Models.Tasks;

public sealed class UserCancelledException : Exception
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public UserCancelledException(string message)
		: base(message ?? "已取消。")
	{
	}
}
