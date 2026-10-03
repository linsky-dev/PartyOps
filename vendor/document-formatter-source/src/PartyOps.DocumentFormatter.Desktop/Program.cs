using System;
using System.Windows.Forms;

namespace PartyOps.DocumentFormatter.Desktop;

internal static class Program
{
	[STAThread]
	private static void Main(string[] args)
	{
		Application.EnableVisualStyles();
		Application.SetCompatibleTextRenderingDefault(false);
		Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
		Application.ThreadException += (_, eventArgs) => ShowFatalError(eventArgs.Exception);
		AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) => ShowFatalError(eventArgs.ExceptionObject as Exception);
		Application.Run(new MainForm(args));
	}

	private static void ShowFatalError(Exception error)
	{
		string detail = error == null ? "发生未知错误。" : error.Message;
		MessageBox.Show("程序遇到未处理错误，但不会修改你的源文件。\r\n\r\n" + detail, "partyops公文排版助手独立版", MessageBoxButtons.OK, MessageBoxIcon.Error);
	}
}
