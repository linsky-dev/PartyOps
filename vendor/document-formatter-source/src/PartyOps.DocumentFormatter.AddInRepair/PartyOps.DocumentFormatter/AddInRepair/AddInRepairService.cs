using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using System.Xml.Linq;
using Microsoft.Win32;

namespace PartyOps.DocumentFormatter.AddInRepair;

internal sealed class AddInRepairService
{
	private sealed class RegistrationSnapshot
	{
		public bool Exists { get; set; }

		public string FriendlyName { get; set; }

		public string Manifest { get; set; }

		public string LoadBehavior { get; set; }
	}

	private const string AddInName = "partyops.documentformatter";

	private const string AddInKeyPath = "Software\\Microsoft\\Office\\Word\\Addins\\partyops.documentformatter";

	private const string ProductKeyPath = "Software\\PartyOps\\DocumentFormatter";

	private const uint WmSettingChange = 26u;

	private const uint SmtoAbortIfHung = 2u;

	private static readonly IntPtr HwndBroadcast = new IntPtr(65535);

	private static readonly string[] OfficeVersions = new string[3] { "16.0", "15.0", "14.0" };

	private static readonly string[] RequiredFiles = new string[3] { "PartyOps.DocumentFormatter.AddIn.dll", "PartyOps.DocumentFormatter.AddIn.dll.manifest", "PartyOps.DocumentFormatter.AddIn.vsto" };

	public string BackupDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PartyOps", "DocumentFormatter", "repair-backups");

	public DiagnosticReport Diagnose()
	{
		DiagnosticReport diagnosticReport = new DiagnosticReport();
		string installDirectory = (diagnosticReport.InstallDirectory = ResolveInstallDirectory());
		diagnosticReport.Add(DiagnosticSeverity.Information, "运行环境", Environment.OSVersion.VersionString + "；进程=" + (Environment.Is64BitProcess ? "64位" : "32位") + "；系统=" + (Environment.Is64BitOperatingSystem ? "64位" : "32位"), repairable: false);
		CheckHostProcesses(diagnosticReport);
		CheckFiles(diagnosticReport, installDirectory);
		string officePlatform = CheckOfficePlatform(diagnosticReport);
		CheckVstoRuntime(diagnosticReport, officePlatform);
		CheckRegistration(diagnosticReport, installDirectory);
		CheckOfficePolicies(diagnosticReport);
		CheckResiliency(diagnosticReport, installDirectory);
		CheckVstoDiagnosticSettings(diagnosticReport, installDirectory);
		return diagnosticReport;
	}

	public DiagnosticReport Repair()
	{
		List<string> runningHostProcessNames = GetRunningHostProcessNames();
		if (runningHostProcessNames.Count > 0)
		{
			throw new InvalidOperationException("请先完全退出 Word/WPS（包括后台进程）再执行修复。当前仍在运行：" + string.Join("、", runningHostProcessNames.ToArray()));
		}
		string installDirectory = Diagnose().InstallDirectory;
		string text = Path.Combine(installDirectory ?? string.Empty, "PartyOps.DocumentFormatter.AddIn.vsto");
		if (string.IsNullOrWhiteSpace(installDirectory) || !File.Exists(text))
		{
			throw new InvalidOperationException("未找到 PartyOps.DocumentFormatter.AddIn.vsto，无法安全修复。请先重新安装partyops公文排版助手。");
		}
		string backupPath = BackupCurrentRegistration(installDirectory);
		string value = new Uri(text).AbsoluteUri + "|vstolocal";
		RegistryView[] registryViews = GetRegistryViews();
		foreach (RegistryView view in registryViews)
		{
			using (RegistryKey registryKey = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, view))
			{
				using RegistryKey registryKey2 = registryKey.CreateSubKey("Software\\Microsoft\\Office\\Word\\Addins\\partyops.documentformatter", writable: true);
				if (registryKey2 == null)
				{
					throw new InvalidOperationException("无法创建当前用户的 Word 加载项注册项（" + ViewName(view) + "）。");
				}
				registryKey2.SetValue("FriendlyName", "partyops公文排版助手", RegistryValueKind.String);
				registryKey2.SetValue("Description", "partyops公文排版助手", RegistryValueKind.String);
				registryKey2.SetValue("Manifest", value, RegistryValueKind.String);
				registryKey2.SetValue("LoadBehavior", 3, RegistryValueKind.DWord);
			}
			WriteWpsWhitelist(view, text + "|vstolocal");
		}
		int num = RemoveMatchingResiliencyEntries(installDirectory);
		DiagnosticReport diagnosticReport = Diagnose();
		diagnosticReport.BackupPath = backupPath;
		diagnosticReport.Add(DiagnosticSeverity.Information, "修复完成", "已恢复本插件的当前用户注册和 WPS 白名单" + ((num > 0) ? ("，并删除 " + num + " 条明确属于本插件的 Word 禁用/崩溃记录") : string.Empty) + "。请完全退出 Word/WPS 后重新打开。", repairable: false);
		return diagnosticReport;
	}

	public void EnableVstoDiagnostics()
	{
		Environment.SetEnvironmentVariable("VSTO_LOGALERTS", "1", EnvironmentVariableTarget.User);
		Environment.SetEnvironmentVariable("VSTO_SUPPRESSDISPLAYALERTS", "0", EnvironmentVariableTarget.User);
		SendMessageTimeout(HwndBroadcast, 26u, UIntPtr.Zero, "Environment", 2u, 5000u, out var _);
	}

	[DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
	private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint message, UIntPtr wParam, string lParam, uint flags, uint timeout, out UIntPtr result);

	private static void CheckHostProcesses(DiagnosticReport report)
	{
		List<string> runningHostProcessNames = GetRunningHostProcessNames();
		if (runningHostProcessNames.Count > 0)
		{
			report.Add(DiagnosticSeverity.Warning, "Office/WPS 正在运行", "检测到 " + string.Join("、", runningHostProcessNames.Distinct().ToArray()) + "。请完全退出这些程序（包括后台进程）后再执行修复。", repairable: false);
		}
		else
		{
			report.Add(DiagnosticSeverity.Information, "Office/WPS 进程", "未发现正在运行的 Word/WPS 进程。", repairable: false);
		}
	}

	private static List<string> GetRunningHostProcessNames()
	{
		string[] obj = new string[4] { "WINWORD", "wps", "wpp", "et" };
		List<string> list = new List<string>();
		string[] array = obj;
		foreach (string text in array)
		{
			try
			{
				if (Process.GetProcessesByName(text).Length != 0)
				{
					list.Add(text);
				}
			}
			catch
			{
			}
		}
		return list.Distinct<string>(StringComparer.OrdinalIgnoreCase).ToList();
	}

	private static void CheckFiles(DiagnosticReport report, string installDirectory)
	{
		if (string.IsNullOrWhiteSpace(installDirectory) || !Directory.Exists(installDirectory))
		{
			report.Add(DiagnosticSeverity.Error, "安装目录", "未找到有效的插件安装目录。", repairable: false);
			return;
		}
		string[] requiredFiles = RequiredFiles;
		foreach (string text in requiredFiles)
		{
			string path = Path.Combine(installDirectory, text);
			report.Add((!File.Exists(path)) ? DiagnosticSeverity.Error : DiagnosticSeverity.Information, "文件 " + text, File.Exists(path) ? "存在" : "缺失，请重新安装插件。", repairable: false);
		}
		CheckManifestSignature(report, Path.Combine(installDirectory, "PartyOps.DocumentFormatter.AddIn.vsto"), "VSTO 部署清单签名");
		CheckManifestSignature(report, Path.Combine(installDirectory, "PartyOps.DocumentFormatter.AddIn.dll.manifest"), "VSTO 应用清单签名");
	}

	private static void CheckManifestSignature(DiagnosticReport report, string path, string title)
	{
		if (!File.Exists(path))
		{
			return;
		}
		try
		{
			bool flag = ((XContainer)XDocument.Load(path, (LoadOptions)0)).Descendants().Any((XElement node) => string.Equals(node.Name.LocalName, "Signature", StringComparison.OrdinalIgnoreCase));
			report.Add((!flag) ? DiagnosticSeverity.Error : DiagnosticSeverity.Information, title + "结构", flag ? "已发现数字签名节点。此项只检查清单结构；加载时的密码学验证和证书信任结果以 VSTO 日志为准。" : "未发现数字签名节点，请重新安装正式安装包。", repairable: false);
		}
		catch (Exception ex)
		{
			report.Add(DiagnosticSeverity.Error, title, "清单无法读取：" + ex.Message, repairable: false);
		}
	}

	private static void CheckVstoRuntime(DiagnosticReport report, string officePlatform)
	{
		List<string> list = new List<string>();
		RegistryView[] registryViews = GetRegistryViews();
		foreach (RegistryView view in registryViews)
		{
			if (ReadVstoInstalled(view))
			{
				list.Add(ViewName(view));
			}
		}
		bool isOffice64Bit = string.Equals(officePlatform, "x64", StringComparison.OrdinalIgnoreCase);
		bool isOffice32Bit = string.Equals(officePlatform, "x86", StringComparison.OrdinalIgnoreCase);
		string loader64Bit = FindVstoLoader(Environment.SpecialFolder.CommonProgramFiles);
		string loader32Bit = FindVstoLoader(Environment.SpecialFolder.CommonProgramFilesX86);
		bool hasMatchingLoader = isOffice64Bit
			? !string.IsNullOrWhiteSpace(loader64Bit)
			: isOffice32Bit
				? !string.IsNullOrWhiteSpace(loader32Bit)
				: !string.IsNullOrWhiteSpace(loader64Bit) || !string.IsNullOrWhiteSpace(loader32Bit);
		DiagnosticSeverity severity = hasMatchingLoader ? DiagnosticSeverity.Information : DiagnosticSeverity.Error;
		string detail;
		if (!hasMatchingLoader)
		{
			detail = "Office=" + (string.IsNullOrWhiteSpace(officePlatform) ? "未知" : officePlatform) + "，但未找到对应位数的 VSTOEE.dll。请安装或修复 Microsoft Visual Studio Tools for Office Runtime。";
		}
		else
		{
			string selectedLoader = isOffice64Bit ? loader64Bit : (isOffice32Bit ? loader32Bit : (!string.IsNullOrWhiteSpace(loader64Bit) ? loader64Bit : loader32Bit));
			detail = "Office=" + (string.IsNullOrWhiteSpace(officePlatform) ? "未知" : officePlatform) + "；加载器=" + selectedLoader + "；运行时注册=" + ((list.Count == 0) ? "未发现" : string.Join("、", list.ToArray())) + "。注册表视图仅作辅助信息，不单独用于判断运行时位数。";
		}
		report.Add(severity, "VSTO 运行环境", detail, repairable: false);
	}

	private static string FindVstoLoader(Environment.SpecialFolder commonFilesFolder)
	{
		try
		{
			string folderPath = Environment.GetFolderPath(commonFilesFolder);
			if (string.IsNullOrWhiteSpace(folderPath))
			{
				return string.Empty;
			}
			string text = Path.Combine(folderPath, "microsoft shared", "VSTO");
			if (!Directory.Exists(text))
			{
				return string.Empty;
			}
			string text2 = Path.Combine(text, "vstoee.dll");
			if (File.Exists(text2))
			{
				return text2;
			}
			return Directory.GetFiles(text, "vstoee.dll", SearchOption.AllDirectories).FirstOrDefault() ?? string.Empty;
		}
		catch
		{
			return string.Empty;
		}
	}

	private static bool ReadVstoInstalled(RegistryView view)
	{
		string[] array = new string[2] { "SOFTWARE\\Microsoft\\VSTO Runtime Setup\\v4R", "SOFTWARE\\Microsoft\\VSTO Runtime Setup\\v4" };
		try
		{
			using RegistryKey registryKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
			string[] array2 = array;
			foreach (string name in array2)
			{
				using RegistryKey registryKey2 = registryKey.OpenSubKey(name, writable: false);
				if (registryKey2 != null)
				{
					object value = registryKey2.GetValue("Install");
					if (value == null || Convert.ToInt32(value, CultureInfo.InvariantCulture) != 0)
					{
						return true;
					}
				}
			}
		}
		catch
		{
		}
		return false;
	}

	private static string CheckOfficePlatform(DiagnosticReport report)
	{
		string text = ReadRegistryString(RegistryHive.LocalMachine, RegistryView.Registry64, "SOFTWARE\\Microsoft\\Office\\ClickToRun\\Configuration", "Platform");
		if (string.IsNullOrWhiteSpace(text))
		{
			text = ReadRegistryString(RegistryHive.LocalMachine, RegistryView.Registry32, "SOFTWARE\\Microsoft\\Office\\ClickToRun\\Configuration", "Platform");
		}
		report.Add(DiagnosticSeverity.Information, "Office 位数", string.IsNullOrWhiteSpace(text) ? "未能从 Click-to-Run 配置中确定。" : text, repairable: false);
		return text;
	}

	private static void CheckOfficePolicies(DiagnosticReport report)
	{
		bool flag = false;
		RegistryHive[] array = new RegistryHive[2]
		{
			RegistryHive.CurrentUser,
			RegistryHive.LocalMachine
		};
		for (int i = 0; i < array.Length; i++)
		{
			RegistryHive hive = array[i];
			RegistryView[] registryViews = GetRegistryViews();
			foreach (RegistryView view in registryViews)
			{
				string[] officeVersions = OfficeVersions;
				foreach (string text in officeVersions)
				{
					string text2 = "Software\\Policies\\Microsoft\\Office\\" + text + "\\Word\\Resiliency";
					if (ReadRegistryInt32(hive, view, text2, "RestrictToList") == 1)
					{
						flag = true;
						string text3 = ReadRegistryString(hive, view, text2 + "\\AddinList", "partyops.documentformatter");
						string text4 = hive.ToString() + "，Office " + text + "，" + ViewName(view);
						if (string.IsNullOrWhiteSpace(text3))
						{
							report.Add(DiagnosticSeverity.Error, "Office 加载项组策略", text4 + "：RestrictToList=1，但托管加载项列表中没有 partyops.documentformatter。该策略会阻止本插件，需由系统管理员调整。", repairable: false);
						}
						else if (text3 == "0")
						{
							report.Add(DiagnosticSeverity.Error, "Office 加载项组策略", text4 + "：partyops.documentformatter=0（始终禁用），需由系统管理员调整。", repairable: false);
						}
						else
						{
							report.Add(DiagnosticSeverity.Information, "Office 加载项组策略", text4 + "：partyops.documentformatter=" + text3 + ((text3 == "1") ? "（始终启用）" : "（允许用户启用）"), repairable: false);
						}
					}
				}
			}
		}
		if (!flag)
		{
			report.Add(DiagnosticSeverity.Information, "Office 加载项组策略", "未发现 RestrictToList=1 的阻止策略。", repairable: false);
		}
	}

	private static void CheckRegistration(DiagnosticReport report, string installDirectory)
	{
		string expectedManifest = Path.Combine(installDirectory ?? string.Empty, "PartyOps.DocumentFormatter.AddIn.vsto");
		RegistryView[] registryViews = GetRegistryViews();
		foreach (RegistryView view in registryViews)
		{
			RegistrationSnapshot registrationSnapshot = ReadRegistration(RegistryHive.CurrentUser, view);
			if (!registrationSnapshot.Exists)
			{
				RegistrationSnapshot registrationSnapshot2 = ReadRegistration(RegistryHive.LocalMachine, view);
				if (registrationSnapshot2.Exists)
				{
					report.Add(DiagnosticSeverity.Information, "Word 注册（" + ViewName(view) + "）", "当前用户未注册，已发现计算机级注册。", repairable: false);
					CheckRegistrationValues(report, registrationSnapshot2, expectedManifest, view, repairable: false);
				}
				else
				{
					report.Add(DiagnosticSeverity.Error, "Word 注册（" + ViewName(view) + "）", "未找到partyops公文排版助手注册项。", repairable: true);
				}
			}
			else
			{
				CheckRegistrationValues(report, registrationSnapshot, expectedManifest, view, repairable: true);
			}
		}
	}

	private static void CheckRegistrationValues(DiagnosticReport report, RegistrationSnapshot snapshot, string expectedManifest, RegistryView view, bool repairable)
	{
		int result;
		bool flag = int.TryParse(snapshot.LoadBehavior, out result) && result == 3;
		report.Add((!flag) ? DiagnosticSeverity.Error : DiagnosticSeverity.Information, "LoadBehavior（" + ViewName(view) + "）", flag ? "3（启动时加载）" : ("当前值=" + (snapshot.LoadBehavior ?? "空") + "，应为 3。"), repairable && !flag);
		bool flag2 = ManifestPointsTo(snapshot.Manifest, expectedManifest);
		report.Add((!flag2) ? DiagnosticSeverity.Error : DiagnosticSeverity.Information, "Manifest（" + ViewName(view) + "）", flag2 ? snapshot.Manifest : ("当前值与安装目录不一致：" + (snapshot.Manifest ?? "空")), repairable && !flag2);
		if (!string.Equals(snapshot.FriendlyName, "partyops公文排版助手", StringComparison.Ordinal))
		{
			report.Add(DiagnosticSeverity.Warning, "FriendlyName（" + ViewName(view) + "）", "当前值=" + (snapshot.FriendlyName ?? "空"), repairable);
		}
	}

	private static void CheckResiliency(DiagnosticReport report, string installDirectory)
	{
		int num = 0;
		List<string> list = new List<string>();
		List<string> list2 = new List<string>();
		RegistryView[] registryViews = GetRegistryViews();
		foreach (RegistryView view in registryViews)
		{
			try
			{
				using RegistryKey registryKey = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, view);
				string[] officeVersions = OfficeVersions;
				foreach (string text in officeVersions)
				{
					using (RegistryKey registryKey2 = registryKey.OpenSubKey("Software\\Microsoft\\Office\\" + text + "\\Word\\Resiliency\\DisabledItems", writable: false))
					{
						if (registryKey2 != null)
						{
							string[] disabledItemNames = registryKey2.GetValueNames();
							foreach (string text2 in disabledItemNames)
							{
								num++;
								byte[] bytes = registryKey2.GetValue(text2) as byte[];
								if (IsOurResiliencyValue(bytes, installDirectory))
								{
									list.Add(text + " / " + ViewName(view) + " / " + text2 + " / SHA256=" + ShortHash(bytes));
								}
							}
						}
					}
					using RegistryKey registryKey3 = registryKey.OpenSubKey("Software\\Microsoft\\Office\\" + text + "\\Word\\Resiliency\\CrashingAddinList", writable: false);
					if (registryKey3 == null)
					{
						continue;
					}
					string[] valueNames = registryKey3.GetValueNames();
					foreach (string text3 in valueNames)
					{
						if (IsOurResiliencyEntry(text3, registryKey3.GetValue(text3), installDirectory))
						{
							list2.Add(text + " / " + ViewName(view) + " / " + text3);
						}
					}
				}
			}
			catch
			{
			}
		}
		if (list2.Count > 0)
		{
			report.Add(DiagnosticSeverity.Warning, "Word 崩溃加载项记录", "检测到明确属于本插件的记录：" + string.Join("；", list2.ToArray()) + "。点击“修复本插件”时会先备份再仅删除这些记录。", repairable: true);
		}
		if (list.Count > 0)
		{
			report.Add(DiagnosticSeverity.Warning, "Word 禁用项目（本插件）", "检测到 " + list.Count + " 条明确包含本插件标识或安装路径的二进制记录：" + string.Join("；", list.ToArray()) + "。点击“修复本插件”时会先备份再仅删除这些记录。", repairable: true);
		}
		int num2 = num - list.Count;
		if (num2 > 0)
		{
			report.Add(DiagnosticSeverity.Warning, "Word 禁用项目（其他/无法识别）", "另有 " + num2 + " 条二进制禁用记录未包含可识别的本插件标识。本工具不会删除；可在 Word 的“禁用项目”中人工核实。", repairable: false);
		}
		else if (list.Count == 0 && list2.Count == 0)
		{
			report.Add(DiagnosticSeverity.Information, "Word Resiliency", "未发现可明确识别的本插件崩溃记录。", repairable: false);
		}
	}

	private static void CheckVstoDiagnosticSettings(DiagnosticReport report, string installDirectory)
	{
		string? environmentVariable = Environment.GetEnvironmentVariable("VSTO_LOGALERTS", EnvironmentVariableTarget.User);
		string environmentVariable2 = Environment.GetEnvironmentVariable("VSTO_SUPPRESSDISPLAYALERTS", EnvironmentVariableTarget.User);
		if (environmentVariable == "1")
		{
			report.Add(DiagnosticSeverity.Information, "VSTO 启动日志", "已开启。失败后优先检查 " + Path.Combine(installDirectory ?? string.Empty, "PartyOps.DocumentFormatter.AddIn.vsto.log") + "；若不存在，再检查 " + Path.GetTempPath() + "。错误弹窗=" + ((environmentVariable2 == "0") ? "已开启" : "未开启") + "。", repairable: false);
		}
		else
		{
			report.Add(DiagnosticSeverity.Information, "VSTO 启动日志", "尚未开启。基础检查不能解释“勾选后立即失效”时，请点击“开启加载日志”，重启 Word 后获取 VSTO 的真实加载错误。", repairable: false);
		}
	}

	private string BackupCurrentRegistration(string installDirectory)
	{
		Directory.CreateDirectory(BackupDirectory);
		string text = Path.Combine(BackupDirectory, "addin-registry-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json");
		List<Dictionary<string, object>> list = new List<Dictionary<string, object>>();
		RegistryView[] registryViews = GetRegistryViews();
		foreach (RegistryView view in registryViews)
		{
			CaptureKey(list, RegistryHive.CurrentUser, view, "Software\\Microsoft\\Office\\Word\\Addins\\partyops.documentformatter");
			CaptureValue(list, RegistryHive.CurrentUser, view, "Software\\KingSoft\\Office\\WPS\\AddinsWL", "partyops.documentformatter");
			CaptureValue(list, RegistryHive.CurrentUser, view, "Software\\KingSoft\\Office\\6.0\\wps\\AddinsWL", "partyops.documentformatter");
			CaptureMatchingResiliencyEntries(list, view, installDirectory);
		}
		Dictionary<string, object> dictionary = new Dictionary<string, object>
		{
			{
				"createdAt",
				DateTime.Now.ToString("o")
			},
			{
				"computer",
				Environment.MachineName
			},
			{ "entries", list }
		};
		File.WriteAllText(text, new JavaScriptSerializer().Serialize((object)dictionary), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
		return text;
	}

	private static void CaptureMatchingResiliencyEntries(IList<Dictionary<string, object>> entries, RegistryView view, string installDirectory)
	{
		using RegistryKey registryKey = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, view);
		string[] officeVersions = OfficeVersions;
		foreach (string text in officeVersions)
		{
			string text2 = "Software\\Microsoft\\Office\\" + text + "\\Word\\Resiliency\\DisabledItems";
			using (RegistryKey registryKey2 = registryKey.OpenSubKey(text2, writable: false))
			{
				if (registryKey2 != null)
				{
					string[] disabledItemNames = registryKey2.GetValueNames();
					foreach (string text3 in disabledItemNames)
					{
						if (IsOurResiliencyValue(registryKey2.GetValue(text3) as byte[], installDirectory))
						{
							CaptureValue(entries, RegistryHive.CurrentUser, view, text2, text3);
						}
					}
				}
			}
			string text4 = "Software\\Microsoft\\Office\\" + text + "\\Word\\Resiliency\\CrashingAddinList";
			using RegistryKey registryKey3 = registryKey.OpenSubKey(text4, writable: false);
			if (registryKey3 == null)
			{
				continue;
			}
			string[] valueNames = registryKey3.GetValueNames();
			foreach (string text5 in valueNames)
			{
				if (IsOurResiliencyEntry(text5, registryKey3.GetValue(text5), installDirectory))
				{
					CaptureValue(entries, RegistryHive.CurrentUser, view, text4, text5);
				}
			}
		}
	}

	private static int RemoveMatchingResiliencyEntries(string installDirectory)
	{
		int num = 0;
		RegistryView[] registryViews = GetRegistryViews();
		foreach (RegistryView view in registryViews)
		{
			using RegistryKey registryKey = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, view);
			string[] officeVersions = OfficeVersions;
			foreach (string text in officeVersions)
			{
				string name = "Software\\Microsoft\\Office\\" + text + "\\Word\\Resiliency\\DisabledItems";
				using (RegistryKey registryKey2 = registryKey.OpenSubKey(name, writable: true))
				{
					if (registryKey2 != null)
					{
						string[] disabledItemNames = registryKey2.GetValueNames();
						foreach (string name2 in disabledItemNames)
						{
							if (IsOurResiliencyValue(registryKey2.GetValue(name2) as byte[], installDirectory))
							{
								registryKey2.DeleteValue(name2, throwOnMissingValue: false);
								num++;
							}
						}
					}
				}
				string name3 = "Software\\Microsoft\\Office\\" + text + "\\Word\\Resiliency\\CrashingAddinList";
				using RegistryKey registryKey3 = registryKey.OpenSubKey(name3, writable: true);
				if (registryKey3 == null)
				{
					continue;
				}
				string[] valueNames = registryKey3.GetValueNames();
				foreach (string text2 in valueNames)
				{
					if (IsOurResiliencyEntry(text2, registryKey3.GetValue(text2), installDirectory))
					{
						registryKey3.DeleteValue(text2, throwOnMissingValue: false);
						num++;
					}
				}
			}
		}
		return num;
	}

	private static bool IsOurResiliencyEntry(string valueName, object value, string installDirectory)
	{
		if (ContainsOurMarker(valueName, installDirectory))
		{
			return true;
		}
		if (ContainsOurMarker(value as string, installDirectory))
		{
			return true;
		}
		return IsOurResiliencyValue(value as byte[], installDirectory);
	}

	private static bool IsOurResiliencyValue(byte[] bytes, string installDirectory)
	{
		if (bytes == null || bytes.Length == 0)
		{
			return false;
		}
		foreach (string resiliencyMarker in GetResiliencyMarkers(installDirectory))
		{
			if (ContainsBytes(bytes, Encoding.Unicode.GetBytes(resiliencyMarker)) || ContainsBytes(bytes, Encoding.UTF8.GetBytes(resiliencyMarker)))
			{
				return true;
			}
		}
		return false;
	}

	private static bool ContainsOurMarker(string value, string installDirectory)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return false;
		}
		return GetResiliencyMarkers(installDirectory).Any((string marker) => value.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0);
	}

	private static IEnumerable<string> GetResiliencyMarkers(string installDirectory)
	{
		yield return "partyops.documentformatter";
		yield return "partyops公文排版助手";
		yield return "PartyOps.DocumentFormatter.AddIn.vsto";
		if (!string.IsNullOrWhiteSpace(installDirectory))
		{
			yield return installDirectory.TrimEnd(new char[1] { Path.DirectorySeparatorChar });
			yield return Path.Combine(installDirectory, "PartyOps.DocumentFormatter.AddIn.vsto");
		}
	}

	private static bool ContainsBytes(byte[] source, byte[] pattern)
	{
		if (source == null || pattern == null || pattern.Length == 0 || pattern.Length > source.Length)
		{
			return false;
		}
		for (int i = 0; i <= source.Length - pattern.Length; i++)
		{
			int j;
			for (j = 0; j < pattern.Length; j++)
			{
				byte b = source[i + j];
				byte b2 = pattern[j];
				if (b >= 65 && b <= 90)
				{
					b += 32;
				}
				if (b2 >= 65 && b2 <= 90)
				{
					b2 += 32;
				}
				if (b != b2)
				{
					break;
				}
			}
			if (j == pattern.Length)
			{
				return true;
			}
		}
		return false;
	}

	private static string ShortHash(byte[] bytes)
	{
		if (bytes == null)
		{
			return "n/a";
		}
		using SHA256 sHA = SHA256.Create();
		return BitConverter.ToString(sHA.ComputeHash(bytes)).Replace("-", string.Empty).Substring(0, 16);
	}

	private static void CaptureKey(IList<Dictionary<string, object>> entries, RegistryHive hive, RegistryView view, string keyPath)
	{
		try
		{
			using RegistryKey registryKey = RegistryKey.OpenBaseKey(hive, view);
			using RegistryKey registryKey2 = registryKey.OpenSubKey(keyPath, writable: false);
			if (registryKey2 == null)
			{
				entries.Add(BackupEntry(hive, view, keyPath, null, null, "Missing"));
				return;
			}
			string[] valueNames = registryKey2.GetValueNames();
			foreach (string name in valueNames)
			{
				object value = registryKey2.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
				entries.Add(BackupEntry(hive, view, keyPath, name, SerializeValue(value), registryKey2.GetValueKind(name).ToString()));
			}
		}
		catch (Exception ex)
		{
			entries.Add(BackupEntry(hive, view, keyPath, null, ex.Message, "ReadError"));
		}
	}

	private static void CaptureValue(IList<Dictionary<string, object>> entries, RegistryHive hive, RegistryView view, string keyPath, string valueName)
	{
		try
		{
			using RegistryKey registryKey = RegistryKey.OpenBaseKey(hive, view);
			using RegistryKey registryKey2 = registryKey.OpenSubKey(keyPath, writable: false);
			if (registryKey2 == null || !registryKey2.GetValueNames().Contains<string>(valueName, StringComparer.OrdinalIgnoreCase))
			{
				entries.Add(BackupEntry(hive, view, keyPath, valueName, null, "Missing"));
				return;
			}
			object value = registryKey2.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
			entries.Add(BackupEntry(hive, view, keyPath, valueName, SerializeValue(value), registryKey2.GetValueKind(valueName).ToString()));
		}
		catch (Exception ex)
		{
			entries.Add(BackupEntry(hive, view, keyPath, valueName, ex.Message, "ReadError"));
		}
	}

	private static Dictionary<string, object> BackupEntry(RegistryHive hive, RegistryView view, string path, string name, object value, string kind)
	{
		return new Dictionary<string, object>
		{
			{
				"hive",
				hive.ToString()
			},
			{
				"view",
				ViewName(view)
			},
			{ "path", path },
			{
				"name",
				name ?? string.Empty
			},
			{ "value", value },
			{ "kind", kind }
		};
	}

	private static object SerializeValue(object value)
	{
		if (value is byte[] inArray)
		{
			return Convert.ToBase64String(inArray);
		}
		return value;
	}

	private static void WriteWpsWhitelist(RegistryView view, string manifestValue)
	{
		string[] array = new string[2] { "Software\\KingSoft\\Office\\WPS\\AddinsWL", "Software\\KingSoft\\Office\\6.0\\wps\\AddinsWL" };
		using RegistryKey registryKey = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, view);
		string[] array2 = array;
		foreach (string subkey in array2)
		{
			using RegistryKey registryKey2 = registryKey.CreateSubKey(subkey, writable: true);
			registryKey2?.SetValue("partyops.documentformatter", manifestValue, RegistryValueKind.String);
		}
	}

	private static RegistrationSnapshot ReadRegistration(RegistryHive hive, RegistryView view)
	{
		RegistrationSnapshot registrationSnapshot = new RegistrationSnapshot();
		try
		{
			using RegistryKey registryKey = RegistryKey.OpenBaseKey(hive, view);
			using RegistryKey registryKey2 = registryKey.OpenSubKey("Software\\Microsoft\\Office\\Word\\Addins\\partyops.documentformatter", writable: false);
			if (registryKey2 == null)
			{
				return registrationSnapshot;
			}
			registrationSnapshot.Exists = true;
			registrationSnapshot.FriendlyName = Convert.ToString(registryKey2.GetValue("FriendlyName"), CultureInfo.InvariantCulture);
			registrationSnapshot.Manifest = Convert.ToString(registryKey2.GetValue("Manifest"), CultureInfo.InvariantCulture);
			registrationSnapshot.LoadBehavior = Convert.ToString(registryKey2.GetValue("LoadBehavior"), CultureInfo.InvariantCulture);
		}
		catch
		{
		}
		return registrationSnapshot;
	}

	private static string ResolveInstallDirectory()
	{
		string baseDirectory = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(new char[1] { Path.DirectorySeparatorChar });
		if (RequiredFiles.All((string file) => File.Exists(Path.Combine(baseDirectory, file))))
		{
			return baseDirectory;
		}
		RegistryView[] registryViews = GetRegistryViews();
		foreach (RegistryView view in registryViews)
		{
			string text = ReadRegistryString(RegistryHive.CurrentUser, view, ProductKeyPath, "Path");
			if (Directory.Exists(text))
			{
				return text.TrimEnd(new char[1] { Path.DirectorySeparatorChar });
			}
			text = ReadRegistryString(RegistryHive.LocalMachine, view, ProductKeyPath, "Path");
			if (Directory.Exists(text))
			{
				return text.TrimEnd(new char[1] { Path.DirectorySeparatorChar });
			}
			string path = ManifestToPath(ReadRegistration(RegistryHive.CurrentUser, view).Manifest);
			if (File.Exists(path))
			{
				return Path.GetDirectoryName(path);
			}
			path = ManifestToPath(ReadRegistration(RegistryHive.LocalMachine, view).Manifest);
			if (File.Exists(path))
			{
				return Path.GetDirectoryName(path);
			}
		}
		return baseDirectory;
	}

	private static bool ManifestPointsTo(string manifestValue, string expectedPath)
	{
		string text = ManifestToPath(manifestValue);
		if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(expectedPath))
		{
			return false;
		}
		try
		{
			return string.Equals(Path.GetFullPath(text), Path.GetFullPath(expectedPath), StringComparison.OrdinalIgnoreCase);
		}
		catch
		{
			return false;
		}
	}

	private static string ManifestToPath(string manifestValue)
	{
		if (string.IsNullOrWhiteSpace(manifestValue))
		{
			return string.Empty;
		}
		string text = manifestValue.Replace("|vstolocal", string.Empty).Trim();
		if (Uri.TryCreate(text, UriKind.Absolute, out Uri result) && result.IsFile)
		{
			return result.LocalPath;
		}
		return text;
	}

	private static string ReadRegistryString(RegistryHive hive, RegistryView view, string keyPath, string valueName)
	{
		try
		{
			using RegistryKey registryKey = RegistryKey.OpenBaseKey(hive, view);
			using RegistryKey registryKey2 = registryKey.OpenSubKey(keyPath, writable: false);
			return (registryKey2 == null) ? null : Convert.ToString(registryKey2.GetValue(valueName), CultureInfo.InvariantCulture);
		}
		catch
		{
			return null;
		}
	}

	private static int? ReadRegistryInt32(RegistryHive hive, RegistryView view, string keyPath, string valueName)
	{
		try
		{
			using RegistryKey registryKey = RegistryKey.OpenBaseKey(hive, view);
			using RegistryKey registryKey2 = registryKey.OpenSubKey(keyPath, writable: false);
			object obj = registryKey2?.GetValue(valueName);
			if (obj == null)
			{
				return null;
			}
			return Convert.ToInt32(obj, CultureInfo.InvariantCulture);
		}
		catch
		{
			return null;
		}
	}

	private static RegistryView[] GetRegistryViews()
	{
		if (Environment.Is64BitOperatingSystem)
		{
			return new RegistryView[2]
			{
				RegistryView.Registry32,
				RegistryView.Registry64
			};
		}
		return new RegistryView[1] { RegistryView.Registry32 };
	}

	private static string ViewName(RegistryView view)
	{
		if (view != RegistryView.Registry64)
		{
			return "32位注册表";
		}
		return "64位注册表";
	}
}
