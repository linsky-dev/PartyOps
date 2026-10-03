using System;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace PartyOps.DocumentFormatter.AddInRepair;

internal static class Program
{
	[STAThread]
	private static int Main(string[] args)
	{
		if (args != null && args.Length != 0)
		{
			return RunCommandLine(args);
		}
		Application.EnableVisualStyles();
		Application.SetCompatibleTextRenderingDefault(false);
		Application.Run((Form)(object)new MainForm());
		return 0;
	}

	private static int RunCommandLine(string[] args)
	{
		string a = args[0] ?? string.Empty;
		string path = ((args.Length > 1 && !string.IsNullOrWhiteSpace(args[1])) ? args[1] : Path.Combine(Path.GetTempPath(), "partyops.documentformatter-addin-diagnostic.txt"));
		try
		{
			AddInRepairService addInRepairService = new AddInRepairService();
			if (string.Equals(a, "/enable-vsto-log", StringComparison.OrdinalIgnoreCase))
			{
				addInRepairService.EnableVstoDiagnostics();
			}
			DiagnosticReport diagnosticReport = (string.Equals(a, "/repair", StringComparison.OrdinalIgnoreCase) ? addInRepairService.Repair() : addInRepairService.Diagnose());
			File.WriteAllText(path, diagnosticReport.ToDisplayText(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
			return diagnosticReport.HasErrors ? 2 : 0;
		}
		catch (Exception ex)
		{
			File.WriteAllText(path, "加载项检测或修复失败：" + ex, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
			return 1;
		}
	}
}
