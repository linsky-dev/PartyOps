using System;
using System.Runtime.InteropServices;
using DocumentRepository.Services.Hosting;

namespace DocumentRepository.Services.Formatting;

public static class DocumentGridCompatibilityPolicy
{
	public const int WpsNativeGridEFail = -2147467259;

	public static bool IsRecoverableFailure(DocumentHostKind hostKind, Exception exception)
	{
		if (hostKind != DocumentHostKind.WpsWriter)
		{
			return false;
		}
		if (exception is COMException ex)
		{
			return ex.ErrorCode == -2147467259;
		}
		return false;
	}
}
