using System;
using System.ComponentModel;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using DocumentRepository.Services.Conversion.PdfToWord;

namespace PartyOps.DocumentFormatter.PdfToWordLauncher;

internal sealed class PdfToWordProgressForm : Form
{
	public sealed class ConversionCallbacks
	{
		public Action<int, int> PageProgress;

		public Action<int, int> ReconstructProgress;

		public Action<ConversionOutcomeKind, string> Finished;

		public CancellationToken CancellationToken;
	}

	private readonly ProgressBar _bar;

	private readonly Label _status;

	private readonly Button _cancel;

	private volatile bool _started;

	private volatile bool _finished;

	private volatile bool _cancelled;

	private readonly string _pdfPath;

	private readonly Action<ConversionCallbacks> _runConversion;

	private readonly CancellationTokenSource _cts = new CancellationTokenSource();

	private ConversionOutcomeKind _outcomeKind;

	private string _safeReason;

	public ConversionOutcomeKind OutcomeKind => _outcomeKind;

	public string SafeReason => _safeReason;

	public bool WasCancelled => _cancelled;

	public PdfToWordProgressForm(string pdfPath, Action<ConversionCallbacks> runConversion)
	{
		_runConversion = runConversion;
		_pdfPath = pdfPath;
		((Control)this).Text = "partyops公文排版助手 - PDF 转 Word";
		((Form)this).FormBorderStyle = (FormBorderStyle)3;
		((Form)this).MaximizeBox = false;
		((Form)this).MinimizeBox = false;
		((Form)this).StartPosition = (FormStartPosition)1;
		((Form)this).ClientSize = new Size(430, 140);
		_bar = new ProgressBar
		{
			Location = new Point(16, 20),
			Width = 398,
			Minimum = 0,
			Maximum = 100
		};
		_status = new Label
		{
			Location = new Point(16, 52),
			Size = new Size(398, 24),
			Text = "正在解析 PDF ..."
		};
		_cancel = new Button
		{
			Text = "取消",
			Location = new Point(340, 92),
			Size = new Size(74, 28)
		};
		((Control)_cancel).Click += delegate
		{
			RequestCancel();
		};
		((Control)this).Controls.Add((Control)(object)_bar);
		((Control)this).Controls.Add((Control)(object)_status);
		((Control)this).Controls.Add((Control)(object)_cancel);
		((Form)this).Shown += delegate
		{
			StartConversion();
		};
		((Form)this).FormClosing += new FormClosingEventHandler(OnFormClosing);
	}

	private void StartConversion()
	{
		if (_started)
		{
			return;
		}
		_started = true;
		ConversionCallbacks callbacks = new ConversionCallbacks
		{
			PageProgress = delegate(int current, int total)
			{
				UpdatePageProgress(current, total);
			},
			ReconstructProgress = delegate(int current, int total)
			{
				UpdateReconstructProgress(current, total);
			},
			Finished = OnConversionFinished,
			CancellationToken = _cts.Token
		};
		ThreadPool.QueueUserWorkItem(delegate
		{
			Exception ex = null;
			bool flag = false;
			try
			{
				_runConversion(callbacks);
			}
			catch (OperationCanceledException)
			{
				flag = true;
			}
			catch (LocalPdfToWordException ex3)
			{
				ex = ex3;
			}
			catch (Exception ex4)
			{
				ex = ex4;
			}
			finally
			{
				if (flag)
				{
					callbacks.Finished(ConversionOutcomeKind.Cancelled, null);
				}
				else if (ex != null)
				{
					callbacks.Finished(ConversionOutcomeKind.Failed, SafeReasonFromException(ex));
				}
				else
				{
					callbacks.Finished(ConversionOutcomeKind.Succeeded, null);
				}
			}
		});
	}

	private void UpdatePageProgress(int current, int total)
	{
		if (((Control)this).IsDisposed || _finished)
		{
			return;
		}
		((Control)this).BeginInvoke((Delegate)(Action)delegate
		{
			if (!((Control)this).IsDisposed && !_finished && total > 0)
			{
				int value = Math.Max(0, Math.Min(100, (int)Math.Round((double)current * 100.0 / (double)total)));
				_bar.Value = value;
				((Control)_status).Text = $"正在转换第 {current} / {total} 页";
			}
		});
	}

	private void UpdateReconstructProgress(int current, int total)
	{
		if (((Control)this).IsDisposed || _finished)
		{
			return;
		}
		((Control)this).BeginInvoke((Delegate)(Action)delegate
		{
			if (!((Control)this).IsDisposed && !_finished)
			{
				((Control)_status).Text = $"正在识别表格并重建版面（第 {current} / {total} 页）";
			}
		});
	}

	private void RequestCancel()
	{
		if (!_cancelled && !_finished)
		{
			_cancelled = true;
			((Control)_cancel).Enabled = false;
			((Control)_status).Text = "正在取消...";
			_cts.Cancel();
		}
	}

	private void OnFormClosing(object sender, FormClosingEventArgs e)
	{
		if (!_finished && _started)
		{
			if (!_cancelled)
			{
				RequestCancel();
			}
			((CancelEventArgs)(object)e).Cancel = true;
		}
	}

	private void OnConversionFinished(ConversionOutcomeKind kind, string reason)
	{
		if (((Control)this).IsDisposed)
		{
			return;
		}
		((Control)this).BeginInvoke((Delegate)(Action)delegate
		{
			if (!((Control)this).IsDisposed)
			{
				_finished = true;
				_outcomeKind = kind;
				_safeReason = reason;
				((Form)this).Close();
			}
		});
	}

	private static string SafeReasonFromException(Exception ex)
	{
		if (ex == null)
		{
			return null;
		}
		if (ex is LocalPdfToWordException { Reason: var reason })
		{
			return reason switch
			{
				PdfToWordFailureReason.Encrypted => "该 PDF 已加密或受密码保护，无法转换。", 
				PdfToWordFailureReason.NoText => "本插件暂不支持扫描型、图片型PDF转换（该类型需云端文字识别，违背本插件所有功能单机运行原则）。", 
				_ => "该 PDF 无法解析，请确认文件完整后重试。", 
			};
		}
		return "转换过程中出现内部错误，请重试；如持续失败请重新打开文档后再转换。";
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			_cts.Dispose();
		}
		base.Dispose(disposing);
	}
}
