using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace PartyOps.DocumentFormatter.PdfToWordLauncher;

internal sealed class PdfToWordResultForm : Form
{
	private readonly string _targetPath;

	public PdfToWordResultForm(string targetPath)
	{
		_targetPath = targetPath;
		((Control)this).Text = "partyops公文排版助手 - 转换成功";
		((Form)this).FormBorderStyle = (FormBorderStyle)3;
		((Form)this).MaximizeBox = false;
		((Form)this).MinimizeBox = false;
		((Form)this).StartPosition = (FormStartPosition)1;
		((Form)this).ClientSize = new Size(420, 150);
		Label val = new Label
		{
			Location = new Point(16, 20),
			Size = new Size(388, 50),
			Text = "PDF 已成功转换为 Word：\r\n" + targetPath
		};
		Button val2 = new Button
		{
			Text = "打开 DOCX",
			Location = new Point(40, 100),
			Size = new Size(100, 30)
		};
		Button val3 = new Button
		{
			Text = "打开文件夹",
			Location = new Point(160, 100),
			Size = new Size(100, 30)
		};
		Button val4 = new Button
		{
			Text = "关闭",
			Location = new Point(280, 100),
			Size = new Size(90, 30)
		};
		((Control)val2).Click += delegate
		{
			OpenDocument();
			((Form)this).Close();
		};
		((Control)val3).Click += delegate
		{
			OpenFolder();
			((Form)this).Close();
		};
		((Control)val4).Click += delegate
		{
			((Form)this).Close();
		};
		((Control)this).Controls.Add((Control)(object)val);
		((Control)this).Controls.Add((Control)(object)val2);
		((Control)this).Controls.Add((Control)(object)val3);
		((Control)this).Controls.Add((Control)(object)val4);
	}

	private void OpenDocument()
	{
		try
		{
			Process.Start(_targetPath);
		}
		catch (Exception)
		{
			OpenFolder();
		}
	}

	private void OpenFolder()
	{
		try
		{
			string directoryName = Path.GetDirectoryName(_targetPath);
			if (!string.IsNullOrEmpty(directoryName))
			{
				Process.Start("explorer.exe", "\"" + directoryName + "\"");
			}
		}
		catch (Exception)
		{
		}
	}
}
