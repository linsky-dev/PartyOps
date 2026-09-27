using System;
using System.Drawing;
using System.Windows.Forms;

namespace PartyOps.DocumentFormatter.PdfToWordLauncher;

internal sealed class PdfToWordConflictDialog : Form
{
	private readonly PdfToWordConflictChoice[] _resultHolder = new PdfToWordConflictChoice[1];

	public PdfToWordConflictChoice Choice => _resultHolder[0];

	public PdfToWordConflictDialog(string targetPath)
	{
		((Control)this).Text = "partyops公文排版助手 - 文件已存在";
		((Form)this).FormBorderStyle = (FormBorderStyle)3;
		((Form)this).MaximizeBox = false;
		((Form)this).MinimizeBox = false;
		((Form)this).StartPosition = (FormStartPosition)1;
		((Form)this).ClientSize = new Size(440, 160);
		_resultHolder[0] = PdfToWordConflictChoice.Cancel;
		Label val = new Label
		{
			Location = new Point(16, 16),
			Size = new Size(408, 60),
			Text = "输出文件已存在：\r\n" + targetPath + "\r\n\r\n请选择处理方式。"
		};
		Button val2 = new Button
		{
			Text = "覆盖",
			Location = new Point(40, 105),
			Size = new Size(110, 32)
		};
		Button val3 = new Button
		{
			Text = "重命名",
			Location = new Point(170, 105),
			Size = new Size(110, 32)
		};
		Button val4 = new Button
		{
			Text = "取消",
			Location = new Point(300, 105),
			Size = new Size(110, 32)
		};
		((Control)val2).Click += delegate
		{
			_resultHolder[0] = PdfToWordConflictChoice.Overwrite;
			((Form)this).DialogResult = (DialogResult)1;
			((Form)this).Close();
		};
		((Control)val3).Click += delegate
		{
			_resultHolder[0] = PdfToWordConflictChoice.Rename;
			((Form)this).DialogResult = (DialogResult)1;
			((Form)this).Close();
		};
		((Control)val4).Click += delegate
		{
			_resultHolder[0] = PdfToWordConflictChoice.Cancel;
			((Form)this).DialogResult = (DialogResult)2;
			((Form)this).Close();
		};
		((Control)this).Controls.Add((Control)(object)val);
		((Control)this).Controls.Add((Control)(object)val2);
		((Control)this).Controls.Add((Control)(object)val3);
		((Control)this).Controls.Add((Control)(object)val4);
		((Form)this).AcceptButton = (IButtonControl)(object)val4;
		((Form)this).CancelButton = (IButtonControl)(object)val4;
	}

	public static PdfToWordConflictChoice Ask(string targetPath)
	{
		PdfToWordConflictDialog pdfToWordConflictDialog = new PdfToWordConflictDialog(targetPath);
		try
		{
			((Form)pdfToWordConflictDialog).ShowDialog();
			return pdfToWordConflictDialog.Choice;
		}
		finally
		{
			((IDisposable)(object)pdfToWordConflictDialog)?.Dispose();
		}
	}
}
