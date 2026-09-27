using System;
using System.Runtime.CompilerServices;
using DocumentRepository.Services.Hosting;
using Microsoft.Office.Interop.Word;

namespace DocumentRepository.Services.CompilationFormatting;

internal static class CompilationMutationWarmupService
{
	internal static Action BeforeInsertForTesting;

	internal static Action AfterInsertBeforeDeleteForTesting;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void Execute(Document document, DocumentWriteLease writeLease)
	{
		if (document == null)
		{
			throw new ArgumentNullException("document");
		}
		if (writeLease == null)
		{
			throw new ArgumentNullException("writeLease");
		}
		Microsoft.Office.Interop.Word.Range value = null;
		Microsoft.Office.Interop.Word.Range value2 = null;
		try
		{
			BeforeInsertForTesting?.Invoke();
			object Start = 0;
			object End = 0;
			value = document.Range(ref Start, ref End);
			value.InsertAfter(" ");
			writeLease.ConfirmWriteOccurred();
			AfterInsertBeforeDeleteForTesting?.Invoke();
			End = 0;
			Start = 1;
			value2 = document.Range(ref End, ref Start);
			Microsoft.Office.Interop.Word.Range range = value2;
			Start = Type.Missing;
			End = Type.Missing;
			range.Delete(ref Start, ref End);
		}
		finally
		{
			ComObjectRelease.Release(ref value2, "CompilationWarmup.Remove");
			ComObjectRelease.Release(ref value, "CompilationWarmup.Insert");
		}
	}
}
