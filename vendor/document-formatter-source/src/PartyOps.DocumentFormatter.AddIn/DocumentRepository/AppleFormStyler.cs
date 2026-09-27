using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using System.Windows.Forms.Layout;
using DocumentRepository.Services.UiText;

namespace DocumentRepository;

internal static class AppleFormStyler
{
	public static void Apply(Form form, ToolTip toolTip = null)
	{
		if (form == null)
		{
			return;
		}
		((Control)form).BackColor = AppleUiColors.Window;
		((Control)form).ForeColor = AppleUiColors.TextPrimary;
		((Control)form).Font = UiFonts.Body;
		((ContainerControl)form).AutoScaleMode = (AutoScaleMode)2;
		ToolTip activeToolTip = toolTip ?? UiTextApplier.CreateToolTip();
		if (toolTip == null)
		{
			((Component)(object)form).Disposed += delegate
			{
				((Component)(object)activeToolTip).Dispose();
			};
		}
		ApplyControlTree(form, (Control)(object)form, activeToolTip);
	}

	private static void ApplyControlTree(Form form, Control root, ToolTip toolTip)
	{
		foreach (Control item in (ArrangedElementCollection)root.Controls)
		{
			Control val = item;
			StyleControl(form, val);
			EnsureTooltip(toolTip, val);
			ApplyControlTree(form, val, toolTip);
		}
	}

	private static void StyleControl(Form form, Control control)
	{
		Button val = (Button)(object)((control is Button) ? control : null);
		if (val != null)
		{
			StyleButton(val);
			return;
		}
		GroupBox val2 = (GroupBox)(object)((control is GroupBox) ? control : null);
		if (val2 == null)
		{
			Label val3 = (Label)(object)((control is Label) ? control : null);
			if (val3 != null)
			{
				if (!(val3 is AppleAlertLabel))
				{
					((Control)val3).BackColor = Color.Transparent;
					if (((Control)val3).ForeColor == Color.DimGray || ((Control)val3).ForeColor == Color.Gray || ((Control)val3).ForeColor == UiColors.TextMuted || ((Control)val3).ForeColor == UiColors.TextBody || ((Control)val3).ForeColor == UiColors.Primary || ((Control)val3).ForeColor == UiColors.PrimaryLight)
					{
						((Control)val3).ForeColor = (((Control)val3).Font.Bold ? AppleUiColors.TextPrimary : AppleUiColors.TextSecondary);
					}
				}
			}
			else if (control is ComboBox || control is NumericUpDown || control is TextBox || control is MaskedTextBox || control is DateTimePicker)
			{
				control.BackColor = AppleUiColors.Surface;
				control.ForeColor = (control.Enabled ? AppleUiColors.TextPrimary : AppleUiColors.TextTertiary);
				control.Font = UiFonts.Body;
			}
			else if (!(control is CheckBox) && !(control is RadioButton))
			{
				if (!(control is ListBox) && !(control is ListView) && !(control is TreeView) && !(control is DataGridView))
				{
					Panel val4 = (Panel)(object)((control is Panel) ? control : null);
					if (val4 != null && !(val4 is RoundedPanel) && (((Control)val4).BackColor == Color.Transparent || ((Control)val4).BackColor == UiColors.BgPage || ((Control)val4).BackColor == UiColors.BgCard || ((Control)val4).BackColor == UiColors.BgMuted))
					{
						((Control)val4).BackColor = (((int)((Control)val4).Dock == 2) ? AppleUiColors.SurfaceMuted : AppleUiColors.Surface);
					}
				}
				else
				{
					control.BackColor = AppleUiColors.Surface;
					control.ForeColor = AppleUiColors.TextPrimary;
					control.Font = UiFonts.Body;
				}
			}
			else
			{
				control.ForeColor = (control.Enabled ? AppleUiColors.TextPrimary : AppleUiColors.TextTertiary);
				control.Font = UiFonts.Body;
				control.Cursor = (control.Enabled ? Cursors.Hand : Cursors.Default);
			}
		}
		else
		{
			((Control)val2).BackColor = AppleUiColors.Surface;
			((Control)val2).ForeColor = AppleUiColors.TextPrimary;
			((Control)val2).Font = UiFonts.BodyBold;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void StyleButton(Button button)
	{
		string text = (((Control)button).Text ?? string.Empty).Trim();
		bool flag = text.IndexOf("删除", StringComparison.Ordinal) >= 0;
		bool flag2 = (int)button.DialogResult == 1 || (int)button.DialogResult == 6 || text == "确定" || text.IndexOf("保存", StringComparison.Ordinal) >= 0 || text.IndexOf("设为当前", StringComparison.Ordinal) >= 0 || text.IndexOf("固定参数", StringComparison.Ordinal) >= 0;
		Color normal = (flag ? AppleUiColors.DangerSoft : (flag2 ? AppleUiColors.Accent : AppleUiColors.SurfaceHover));
		Color hover = (flag ? Color.FromArgb(255, 224, 227) : (flag2 ? AppleUiColors.AccentHover : AppleUiColors.AccentSoft));
		Color pressed = (flag ? Color.FromArgb(255, 210, 214) : (flag2 ? AppleUiColors.AccentPressed : AppleUiColors.Separator));
		Color foreColor = (flag ? AppleUiColors.Danger : (flag2 ? Color.White : AppleUiColors.Accent));
		((ButtonBase)button).FlatStyle = (FlatStyle)0;
		((ButtonBase)button).FlatAppearance.BorderSize = 0;
		((ButtonBase)button).UseVisualStyleBackColor = false;
		((Control)button).BackColor = normal;
		((Control)button).ForeColor = foreColor;
		((Control)button).Font = UiFonts.BodyBold;
		((Control)button).Cursor = (((Control)button).Enabled ? Cursors.Hand : Cursors.Default);
		if (!(button is StyledButton styledButton))
		{
			ApplyRoundedRegion((Control)(object)button, 9);
			((Control)button).Resize += delegate
			{
				ApplyRoundedRegion((Control)(object)button, 9);
			};
			((Control)button).MouseEnter += delegate
			{
				if (((Control)button).Enabled)
				{
					((Control)button).BackColor = hover;
				}
			};
			((Control)button).MouseLeave += delegate
			{
				((Control)button).BackColor = normal;
			};
			((Control)button).MouseDown += (MouseEventHandler)delegate
			{
				if (((Control)button).Enabled)
				{
					((Control)button).BackColor = pressed;
				}
			};
			((Control)button).MouseUp += (MouseEventHandler)delegate
			{
				if (((Control)button).Enabled)
				{
					((Control)button).BackColor = hover;
				}
			};
		}
		else
		{
			styledButton.HoverBackColor = hover;
			styledButton.PressBackColor = pressed;
			styledButton.Radius = 9;
			((Control)styledButton).Invalidate();
		}
	}

	private static void ApplyRoundedRegion(Control control, int radius)
	{
		if (control.Width <= 1 || control.Height <= 1)
		{
			return;
		}
		GraphicsPath val = UiDraw.RoundedRect(new Rectangle(0, 0, control.Width, control.Height), radius);
		try
		{
			Region region = control.Region;
			control.Region = new Region(val);
			if (region != null)
			{
				region.Dispose();
			}
		}
		finally
		{
			((IDisposable)val)?.Dispose();
		}
	}

	private static void EnsureTooltip(ToolTip toolTip, Control control)
	{
		if (toolTip != null && control != null && IsInteractive(control) && string.IsNullOrWhiteSpace(toolTip.GetToolTip(control)))
		{
			string text = ResolveTooltip(control);
			if (!string.IsNullOrWhiteSpace(text))
			{
				toolTip.SetToolTip(control, text);
			}
		}
	}

	private static bool IsInteractive(Control control)
	{
		if (!(control is Button) && !(control is CheckBox) && !(control is RadioButton) && !(control is ComboBox) && !(control is NumericUpDown) && !(control is TextBox) && !(control is ListBox) && !(control is ListView) && !(control is TreeView))
		{
			return control is DateTimePicker;
		}
		return true;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string ResolveTooltip(Control control)
	{
		string text = (control.Text ?? string.Empty).Trim();
		if (!(control is Button))
		{
			if (control is CheckBox)
			{
				if (!string.IsNullOrWhiteSpace(text))
				{
					return "勾选后启用“" + text + "”。";
				}
				return "开启或关闭此选项。";
			}
			if (control is RadioButton)
			{
				if (string.IsNullOrWhiteSpace(text))
				{
					return "选择此设置。";
				}
				return "选择“" + text + "”模式。";
			}
			string text2 = FindNearbyCaption(control);
			if (control is ComboBox)
			{
				return "选择“" + text2 + "”的设置值。";
			}
			if (!(control is NumericUpDown))
			{
				if (!(control is TextBox))
				{
					if (control is ListBox || control is ListView || control is TreeView)
					{
						return "选择要查看或编辑的“" + text2 + "”。";
					}
					return string.Empty;
				}
				return "输入“" + text2 + "”的内容。";
			}
			return "调整“" + text2 + "”的数值。";
		}
		switch (text)
		{
		default:
			if (text.IndexOf("新增", StringComparison.Ordinal) < 0)
			{
				if (text.IndexOf("复制", StringComparison.Ordinal) >= 0)
				{
					return "复制当前所选项目，原项目保持不变。";
				}
				if (text.IndexOf("删除", StringComparison.Ordinal) < 0)
				{
					if (text.IndexOf("编辑", StringComparison.Ordinal) >= 0 || text.IndexOf("参数", StringComparison.Ordinal) >= 0)
					{
						return "打开当前项目的详细设置。";
					}
					if (!(text == "上移") && !(text == "下移"))
					{
						if (text.IndexOf("设为当前", StringComparison.Ordinal) >= 0)
						{
							return "将当前所选项目设为默认执行项。";
						}
						if (text.IndexOf("恢复", StringComparison.Ordinal) < 0)
						{
							if (text.IndexOf("打开", StringComparison.Ordinal) < 0)
							{
								if (string.IsNullOrWhiteSpace(text))
								{
									return "执行此操作。";
								}
								return "执行“" + text + "”。";
							}
							return "打开对应内容。";
						}
						return "恢复此页面的推荐默认值。";
					}
					return "调整当前所选项目的执行顺序。";
				}
				return "删除当前所选项目，操作前会再次确认。";
			}
			return "新增一项，并在当前页面继续编辑。";
		case "取消":
			return "放弃本次未保存的修改并关闭窗口。";
		case "关闭":
			return "关闭当前窗口。";
		case "确定":
			return "保存当前设置并关闭窗口。";
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string FindNearbyCaption(Control control)
	{
		Control parent = control.Parent;
		if (parent != null)
		{
			Label val = null;
			int num = int.MaxValue;
			foreach (Control item in (ArrangedElementCollection)parent.Controls)
			{
				object obj = (object)item;
				Label val2 = (Label)((obj is Label) ? obj : null);
				if (val2 == null || string.IsNullOrWhiteSpace(((Control)val2).Text))
				{
					continue;
				}
				int num2 = Math.Abs(((Control)val2).Top + ((Control)val2).Height / 2 - (control.Top + control.Height / 2));
				int num3 = control.Left - (((Control)val2).Left + ((Control)val2).Width);
				if (num2 <= 24 && num3 >= -12)
				{
					int num4 = num2 * 4 + Math.Abs(num3);
					if (num4 < num)
					{
						val = val2;
						num = num4;
					}
				}
			}
			if (val == null)
			{
				GroupBox val3 = (GroupBox)(object)((parent is GroupBox) ? parent : null);
				if (val3 != null && !string.IsNullOrWhiteSpace(((Control)val3).Text))
				{
					return ((Control)val3).Text.Trim();
				}
				return "当前参数";
			}
			return ((Control)val).Text.Trim().TrimEnd('：', ':');
		}
		return "当前参数";
	}
}
