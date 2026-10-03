using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using DocumentRepository.Models.Standalone;
using Microsoft.Office.Interop.Word;
using WordApplication = Microsoft.Office.Interop.Word.Application;

namespace DocumentRepository.Services.Hosting.Standalone;

/// <summary>
/// 独占启动一个不可见的 Word/WPS 文档引擎。该对象只能在独立版的 STA 工作线程中使用。
/// </summary>
internal sealed class OfficeHostSession : IDisposable
{
	private WordApplication application;
	private bool disposed;

	private OfficeHostSession(WordApplication application, string progId)
	{
		this.application = application;
		ProgId = progId;
		ConfigureApplication();
		DisplayName = ResolveDisplayName();
	}

	public WordApplication Application => application ?? throw new ObjectDisposedException(nameof(OfficeHostSession));

	public string ProgId { get; }

	public string DisplayName { get; }

	public static OfficeHostSession Start(OfficeHostPreference preference)
	{
		List<string> failures = new List<string>();
		if (preference != OfficeHostPreference.Word && PortableOfficeHostRuntime.IsRegistered)
		{
			object portableValue = null;
			if (PortableOfficeHostRuntime.TryActivate(out portableValue, out string portableFailure))
			{
				WordApplication portableApplication = portableValue as WordApplication;
				if (portableApplication == null)
				{
					PortableOfficeHostRuntime.TryRelease(portableValue);
					failures.Add("PartyOps.PortableWps.Application 未返回兼容的文档引擎");
				}
				else if (!IsIsolatedApplication(portableApplication, out string isolationFailure))
				{
					PortableOfficeHostRuntime.TryRelease(portableApplication);
					failures.Add("PartyOps.PortableWps.Application：" + isolationFailure);
				}
				else if (!MatchesPreference(portableApplication, preference))
				{
					QuitOwnedApplication(portableApplication);
					ReleaseComObject(portableApplication);
					failures.Add("PartyOps.PortableWps.Application 与指定宿主不匹配");
				}
				else
				{
					return new OfficeHostSession(portableApplication, "PartyOps.PortableWps.Application");
				}
			}
			else
			{
				failures.Add("PartyOps.PortableWps.Application：" + portableFailure);
			}
		}
		foreach (string progId in BuildCandidates(preference))
		{
			WordApplication candidate = null;
			try
			{
				Type comType = Type.GetTypeFromProgID(progId, false);
				if (comType == null)
				{
					failures.Add(progId + " 未注册");
					continue;
				}
				candidate = ExecuteWithBusyRetry(() => Activator.CreateInstance(comType) as WordApplication, "启动 " + progId);
				if (candidate == null)
				{
					failures.Add(progId + " 未返回兼容的文档引擎");
					continue;
				}
				if (!IsIsolatedApplication(candidate, out string isolationFailure))
				{
					// 不能退出或隐藏可能属于用户的现有会话；只释放本次 COM 引用并尝试下一个 ProgID。
					ReleaseComObject(candidate);
					candidate = null;
					failures.Add(progId + "：" + isolationFailure);
					continue;
				}
				if (!MatchesPreference(candidate, preference))
				{
					QuitOwnedApplication(candidate);
					ReleaseComObject(candidate);
					candidate = null;
					failures.Add(progId + " 与指定宿主不匹配");
					continue;
				}
				return new OfficeHostSession(candidate, progId);
			}
			catch (Exception ex)
			{
				if (candidate != null)
				{
					QuitOwnedApplication(candidate);
					ReleaseComObject(candidate);
				}
				failures.Add(progId + "：" + ex.Message);
			}
		}
		throw new InvalidOperationException("未能启动可用的 Word/WPS 文档引擎。请确认 Word 或 WPS 已正确安装，并选择与其位数匹配的独立版。\r\n" + string.Join("\r\n", failures));
	}

	public Document OpenDocument(string path, bool readOnly)
	{
		if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
		{
			throw new FileNotFoundException("待处理文档不存在。", path);
		}
		Documents documents = null;
		try
		{
			documents = Application.Documents;
			object fileName = Path.GetFullPath(path);
			object confirmConversions = false;
			object readOnlyValue = readOnly;
			object addToRecentFiles = false;
			object passwordDocument = Type.Missing;
			object passwordTemplate = Type.Missing;
			object revert = false;
			object writePasswordDocument = Type.Missing;
			object writePasswordTemplate = Type.Missing;
			object format = Type.Missing;
			object encoding = Type.Missing;
			object visible = false;
			object openAndRepair = true;
			object documentDirection = Type.Missing;
			object noEncodingDialog = true;
			object xmlTransform = Type.Missing;
			return documents.Open(ref fileName, ref confirmConversions, ref readOnlyValue, ref addToRecentFiles, ref passwordDocument, ref passwordTemplate, ref revert, ref writePasswordDocument, ref writePasswordTemplate, ref format, ref encoding, ref visible, ref openAndRepair, ref documentDirection, ref noEncodingDialog, ref xmlTransform);
		}
		finally
		{
			ReleaseComObject(documents);
		}
	}

	public void SaveAsDocx(Document document, string targetPath)
	{
		if (document == null)
		{
			throw new ArgumentNullException(nameof(document));
		}
		Directory.CreateDirectory(Path.GetDirectoryName(targetPath) ?? ".");
		object fileName = Path.GetFullPath(targetPath);
		object fileFormat = WdSaveFormat.wdFormatXMLDocument;
		object lockComments = Type.Missing;
		object password = Type.Missing;
		object addToRecentFiles = false;
		object writePassword = Type.Missing;
		object readOnlyRecommended = false;
		object embedTrueTypeFonts = Type.Missing;
		object saveNativePictureFormat = Type.Missing;
		object saveFormsData = Type.Missing;
		object saveAsAocLetter = Type.Missing;
		object encoding = Type.Missing;
		object insertLineBreaks = Type.Missing;
		object allowSubstitutions = Type.Missing;
		object lineEnding = Type.Missing;
		object addBiDiMarks = Type.Missing;
		object compatibilityMode = Type.Missing;
		document.SaveAs2(ref fileName, ref fileFormat, ref lockComments, ref password, ref addToRecentFiles, ref writePassword, ref readOnlyRecommended, ref embedTrueTypeFonts, ref saveNativePictureFormat, ref saveFormsData, ref saveAsAocLetter, ref encoding, ref insertLineBreaks, ref allowSubstitutions, ref lineEnding, ref addBiDiMarks, ref compatibilityMode);
	}

	public void CloseDocument(ref Document document, bool saveChanges)
	{
		Document owned = document;
		document = null;
		if (owned == null)
		{
			return;
		}
		try
		{
			object save = saveChanges ? WdSaveOptions.wdSaveChanges : WdSaveOptions.wdDoNotSaveChanges;
			object originalFormat = Type.Missing;
			object routeDocument = false;
			owned.Close(ref save, ref originalFormat, ref routeDocument);
		}
		finally
		{
			ReleaseComObject(owned);
		}
	}

	private void ConfigureApplication()
	{
		try
		{
			application.Visible = false;
		}
		catch
		{
		}
		try
		{
			application.DisplayAlerts = WdAlertLevel.wdAlertsNone;
		}
		catch
		{
		}
		try
		{
			application.ScreenUpdating = false;
		}
		catch
		{
		}
	}

	private string ResolveDisplayName()
	{
		string name = SafeRead(() => application.Name);
		string version = SafeRead(() => application.Version);
		string path = SafeRead(() => application.Path);
		string display = string.IsNullOrWhiteSpace(name) ? ProgId : name;
		if (!string.IsNullOrWhiteSpace(version))
		{
			display += " " + version;
		}
		if (!string.IsNullOrWhiteSpace(path))
		{
			display += "（" + path + "）";
		}
		return display;
	}

	private static IEnumerable<string> BuildCandidates(OfficeHostPreference preference)
	{
		if (preference == OfficeHostPreference.Wps)
		{
			return new[] { "KWPS.Application", "wps.Application", "WPS.Application", "KSO.WPS.Application", "Word.Application" };
		}
		return preference == OfficeHostPreference.Word
			? new[] { "Word.Application" }
			: new[] { "Word.Application", "KWPS.Application", "wps.Application", "WPS.Application", "KSO.WPS.Application" };
	}

	private static bool MatchesPreference(WordApplication candidate, OfficeHostPreference preference)
	{
		if (preference == OfficeHostPreference.Auto)
		{
			return true;
		}
		string identity = (SafeRead(() => candidate.Name) + " " + SafeRead(() => candidate.Path)).ToLowerInvariant();
		bool isWps = identity.Contains("wps") || identity.Contains("kingsoft") || identity.Contains("金山");
		return preference == OfficeHostPreference.Wps ? isWps : !isWps;
	}

	private static bool IsIsolatedApplication(WordApplication candidate, out string failure)
	{
		failure = null;
		Documents documents = null;
		try
		{
			documents = ExecuteWithBusyRetry(() => candidate.Documents, "检查文档引擎会话");
			int existingDocumentCount = ExecuteWithBusyRetry(() => documents.Count, "检查已有文档");
			if (existingDocumentCount > 0)
			{
				failure = "COM 激活连接到了包含 " + existingDocumentCount + " 个文档的已有会话。为保护用户文档，独立版拒绝接管该会话；请保存并关闭对应 WPS/Word 窗口后重试。";
				return false;
			}
			return true;
		}
		catch (Exception ex)
		{
			failure = "无法确认这是独立的新会话。为保护用户文档，已停止接管：" + ex.Message;
			return false;
		}
		finally
		{
			ReleaseComObject(documents);
		}
	}

	private static string SafeRead(Func<string> reader)
	{
		try
		{
			return reader() ?? string.Empty;
		}
		catch
		{
			return string.Empty;
		}
	}

	private static T ExecuteWithBusyRetry<T>(Func<T> action, string operation)
	{
		COMException lastError = null;
		for (int attempt = 1; attempt <= 8; attempt++)
		{
			try
			{
				return action();
			}
			catch (COMException ex) when (IsComBusy(ex))
			{
				lastError = ex;
				Thread.Sleep(150 * attempt);
			}
		}
		throw new InvalidOperationException(operation + "时文档引擎持续忙碌。请稍后重试。", lastError);
	}

	private static bool IsComBusy(COMException error)
	{
		uint code = unchecked((uint)error.ErrorCode);
		return code == 0x80010001u || code == 0x8001010Au;
	}

	private static void QuitOwnedApplication(WordApplication ownedApplication)
	{
		try
		{
			if (PortableOfficeHostRuntime.TryQuit(ownedApplication))
			{
				return;
			}
			dynamic dynamicApplication = ownedApplication;
			object saveChanges = WdSaveOptions.wdDoNotSaveChanges;
			object originalFormat = Type.Missing;
			object routeDocument = false;
			dynamicApplication.Quit(ref saveChanges, ref originalFormat, ref routeDocument);
		}
		catch
		{
			try
			{
				dynamic dynamicApplication = ownedApplication;
				dynamicApplication.Quit();
			}
			catch
			{
			}
		}
	}

	private static void ReleaseComObject(object value)
	{
		if (value == null)
		{
			return;
		}
		try
		{
			if (PortableOfficeHostRuntime.TryRelease(value))
			{
				return;
			}
			if (Marshal.IsComObject(value))
			{
				Marshal.FinalReleaseComObject(value);
			}
		}
		catch
		{
		}
	}

	public void Dispose()
	{
		if (disposed)
		{
			return;
		}
		disposed = true;
		WordApplication owned = application;
		application = null;
		if (owned != null)
		{
			QuitOwnedApplication(owned);
			ReleaseComObject(owned);
		}
	}
}
