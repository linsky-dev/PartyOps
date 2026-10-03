using System;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using DocumentRepository.Models.RedHeader;
using DocumentRepository.Models.Snapshots;
using DocumentRepository.Services.Hosting;
using DocumentRepository.Services.Snapshots;
using Microsoft.Office.Interop.Word;

namespace DocumentRepository.Services.RedHeader;

public static class RedHeaderAnalysisService
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static RedHeaderAnalysisSnapshot Capture(Document document)
	{
		if (document != null)
		{
			Sections value = null;
			Section value2 = null;
			Section value3 = null;
			PageSetup value4 = null;
			PageSetup value5 = null;
			try
			{
				value = document.Sections;
				if (value != null && value.Count > 0)
				{
					value2 = value[1];
					value3 = value[value.Count];
					value4 = value2.PageSetup;
					value5 = value3.PageSetup;
					RedHeaderAnalysisSnapshot obj = new RedHeaderAnalysisSnapshot
					{
						DocumentSnapshot = DocumentSnapshotService.Capture(document)
					};
					object IncludeFootnotesAndEndnotes = Type.Missing;
					obj.InitialPageCount = document.ComputeStatistics(WdStatistic.wdStatisticPages, ref IncludeFootnotesAndEndnotes);
					obj.FirstPageWidth = Convert.ToSingle(value4.PageWidth);
					obj.FirstPageHeight = Convert.ToSingle(value4.PageHeight);
					obj.FirstLeftMargin = Convert.ToSingle(value4.LeftMargin);
					obj.FirstRightMargin = Convert.ToSingle(value4.RightMargin);
					obj.LastPageHeight = Convert.ToSingle(value5.PageHeight);
					obj.LastBottomMargin = Convert.ToSingle(value5.BottomMargin);
					RedHeaderAnalysisSnapshot redHeaderAnalysisSnapshot = obj;
					string text = ReadSourceBody(document, redHeaderAnalysisSnapshot.DocumentSnapshot);
					redHeaderAnalysisSnapshot.SourceBodyLength = text.Length;
					redHeaderAnalysisSnapshot.SourceBodyHash = Hash(text);
					Validate(redHeaderAnalysisSnapshot);
					return redHeaderAnalysisSnapshot;
				}
				throw new InvalidOperationException("套红分析无法读取文档节信息。");
			}
			finally
			{
				ComObjectRelease.Release(ref value5, "RedHeaderAnalysis.LastPageSetup");
				ComObjectRelease.Release(ref value4, "RedHeaderAnalysis.FirstPageSetup");
				ComObjectRelease.Release(ref value3, "RedHeaderAnalysis.LastSection");
				ComObjectRelease.Release(ref value2, "RedHeaderAnalysis.FirstSection");
				ComObjectRelease.Release(ref value, "RedHeaderAnalysis.Sections");
			}
		}
		throw new ArgumentNullException("document");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void Validate(RedHeaderAnalysisSnapshot snapshot)
	{
		if (snapshot.DocumentSnapshot == null)
		{
			throw new InvalidOperationException("套红分析未生成文档快照。");
		}
		if (snapshot.InitialPageCount > 0)
		{
			if (snapshot.FirstPageWidth <= 0f || !(snapshot.FirstPageHeight > 0f) || !(snapshot.LastPageHeight > 0f))
			{
				throw new InvalidOperationException("套红分析读取到的页面尺寸无效。");
			}
			if (!(snapshot.FirstLeftMargin < 0f) && !(snapshot.FirstRightMargin < 0f) && !(snapshot.LastBottomMargin < 0f))
			{
				if (snapshot.SourceBodyLength < 0 || !IsSha256(snapshot.SourceBodyHash))
				{
					throw new InvalidOperationException("套红分析未生成可靠的原正文指纹。");
				}
				return;
			}
			throw new InvalidOperationException("套红分析读取到的页边距无效。");
		}
		throw new InvalidOperationException("套红分析读取到的页数无效。");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string ReadSourceBody(Document document, DocumentSnapshot snapshot)
	{
		Microsoft.Office.Interop.Word.Range value = null;
		try
		{
			int scopeStart = snapshot.ScopeStart;
			object Start = scopeStart;
			object End = snapshot.ScopeEnd;
			value = document.Range(ref Start, ref End);
			return NormalizeSourceBody((value == null) ? string.Empty : value.Text);
		}
		finally
		{
			ComObjectRelease.Release(ref value, "RedHeaderAnalysis.SourceBody");
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	internal static string Hash(string value)
	{
		using SHA256 sHA = SHA256.Create();
		byte[] array = sHA.ComputeHash(Encoding.UTF8.GetBytes(value ?? string.Empty));
		StringBuilder stringBuilder = new StringBuilder(array.Length * 2);
		byte[] array2 = array;
		foreach (byte b in array2)
		{
			stringBuilder.Append(b.ToString("x2"));
		}
		return stringBuilder.ToString();
	}

	internal static string NormalizeSourceBody(string value)
	{
		value = value ?? string.Empty;
		int num = value.Length;
		while (num > 0 && (value[num - 1] == '\r' || value[num - 1] == '\a'))
		{
			num--;
		}
		if (num != value.Length)
		{
			return value.Substring(0, num);
		}
		return value;
	}

	private static bool IsSha256(string value)
	{
		if (value != null && value.Length == 64)
		{
			foreach (char c in value)
			{
				if ((c < '0' || c > '9') && (c < 'a' || c > 'f'))
				{
					return false;
				}
			}
			return true;
		}
		return false;
	}
}
