using System.IO;
using System.Runtime.CompilerServices;

namespace DocumentRepository.Services.Hosting;

public static class ComObjectRelease
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void ReleaseOwned(object value, [CallerMemberName] string memberName = null, [CallerFilePath] string sourceFilePath = null, [CallerLineNumber] int sourceLineNumber = 0)
	{
		string text = (string.IsNullOrWhiteSpace(sourceFilePath) ? "unknown" : Path.GetFileName(sourceFilePath));
		string context = text + ":" + sourceLineNumber + " " + (memberName ?? "unknown");
		DetachOnly(value, context);
	}

	public static void Release(object value, string context)
	{
		DetachOnly(value, context);
	}

	private static void DetachOnly(object value, string context)
	{
	}

	public static void Release<T>(ref T value, string context) where T : class
	{
		T value2 = value;
		value = null;
		Release(value2, context);
	}
}
