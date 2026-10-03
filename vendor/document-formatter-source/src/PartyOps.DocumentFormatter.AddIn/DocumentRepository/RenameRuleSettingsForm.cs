using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using DocumentRepository.Models.Rules;
using DocumentRepository.Services.Auth;
using DocumentRepository.Services.Rules;
using DocumentRepository.Services.UiText;

namespace DocumentRepository;

public class RenameRuleSettingsForm : Form
{
	private readonly ListBox lstRules = new ListBox();

	private readonly ListBox lstParts = new ListBox();

	private readonly Button btnAddRule = new Button();

	private readonly Button btnDeleteRule = new Button();

	private readonly Button btnSetActiveRule = new Button();

	private readonly Button btnRemovePart = new Button();

	private readonly Button btnMoveUp = new Button();

	private readonly Button btnMoveDown = new Button();

	private readonly Button btnDocNumber = new Button();

	private readonly Button btnMainTitle = new Button();

	private readonly Button btnSubtitle = new Button();

	private readonly Button btnDate = new Button();

	private readonly Button btnRotate = new Button();

	private readonly Button btnCustom = new Button();

	private readonly TextBox txtName = new TextBox();

	private readonly ComboBox cboRenameMode = new ComboBox();

	private readonly Label lblSavePath = new Label();

	private readonly ComboBox cboSavePathMode = new ComboBox();

	private readonly ComboBox cboDateFormat = new ComboBox();

	private readonly TextBox txtCustom = new TextBox();

	private readonly TextBox txtRotateWords = new TextBox();

	private readonly CheckBox chkResetRotateOnStartup = new CheckBox();

	private readonly TextBox txtPreview = new TextBox();

	private readonly Button btnOk = new Button();

	private readonly Button btnCancel = new Button();

	private readonly ToolTip toolTip = UiTextApplier.CreateToolTip();

	private RenameRuleSet ruleSet;

	private bool storeRecoveryDeclined;

	private readonly RenameRulePreviewInfo previewInfo;

	private bool loading;

	private int loadedRuleIndex = -1;

	internal static Func<bool> RuleManagementPermissionForTesting;

	private const int EM_SETCUEBANNER = 5377;

	private RenameRule SelectedRule
	{
		get
		{
			if (((ListControl)lstRules).SelectedIndex < 0 || ((ListControl)lstRules).SelectedIndex >= ruleSet.Rules.Count)
			{
				return null;
			}
			return ruleSet.Rules[((ListControl)lstRules).SelectedIndex];
		}
	}

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

	public RenameRuleSettingsForm()
		: this(null)
	{
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public RenameRuleSettingsForm(RenameRulePreviewInfo preview)
	{
		RuleLoadResult<RenameRuleSet> ruleLoadResult = RenameRuleManager.LoadResult();
		if (!ruleLoadResult.Usable && !RuleStoreResetPrompt.ConfirmAndReset((IWin32Window)(object)this, ruleLoadResult, "命名规则库", RenameRuleManager.ResetToDefault))
		{
			storeRecoveryDeclined = true;
			ruleSet = new RenameRuleSet
			{
				ActiveRuleId = ""
			};
		}
		else
		{
			ruleSet = RenameRuleManager.LoadResult().Value;
		}
		previewInfo = preview ?? new RenameRulePreviewInfo();
		InitializeUi();
		ReloadRuleList();
	}

	protected override void OnLoad(EventArgs e)
	{
		base.OnLoad(e);
		if (storeRecoveryDeclined)
		{
			((Form)this).Close();
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void InitializeUi()
	{
		((Control)this).Text = "一键命名规则设置";
		((Form)this).StartPosition = (FormStartPosition)4;
		((Form)this).Size = new Size(930, 595);
		((Control)this).MinimumSize = new Size(900, 565);
		((Control)this).Font = new Font("Microsoft YaHei UI", 9f);
		((Control)this).BackColor = AppleUiColors.Window;
		((Control)this).RightToLeft = (RightToLeft)0;
		toolTip.InitialDelay = 100;
		toolTip.ReshowDelay = 50;
		toolTip.AutoPopDelay = 8000;
		Label val = new Label
		{
			Text = "一键命名规则设置",
			Font = new Font("Microsoft YaHei UI", 12f, (FontStyle)1),
			ForeColor = AppleUiColors.TextPrimary,
			Location = new Point(20, 18),
			AutoSize = true
		};
		((Control)this).Controls.Add((Control)(object)val);
		((Control)lstRules).Location = new Point(20, 55);
		((Control)lstRules).Size = new Size(230, 350);
		lstRules.SelectedIndexChanged += delegate
		{
			HandleRuleSelectionChanged();
		};
		((Control)this).Controls.Add((Control)(object)lstRules);
		((Control)btnAddRule).Text = "新增规则";
		((Control)btnAddRule).Location = new Point(20, 420);
		((Control)btnAddRule).Size = new Size(100, 32);
		((Control)btnAddRule).Click += delegate
		{
			AddRule();
		};
		((Control)this).Controls.Add((Control)(object)btnAddRule);
		((Control)btnDeleteRule).Text = "删除规则";
		((Control)btnDeleteRule).Location = new Point(150, 420);
		((Control)btnDeleteRule).Size = new Size(100, 32);
		((Control)btnDeleteRule).Click += delegate
		{
			DeleteRule();
		};
		((Control)this).Controls.Add((Control)(object)btnDeleteRule);
		((Control)btnSetActiveRule).Text = "设为当前";
		((Control)btnSetActiveRule).Location = new Point(20, 462);
		((Control)btnSetActiveRule).Size = new Size(230, 32);
		((Control)btnSetActiveRule).Click += delegate
		{
			SetActiveRule();
		};
		((Control)this).Controls.Add((Control)(object)btnSetActiveRule);
		RoundedPanel roundedPanel = new RoundedPanel();
		((Control)roundedPanel).Location = new Point(270, 55);
		((Control)roundedPanel).Size = new Size(620, 392);
		roundedPanel.Radius = 12;
		roundedPanel.BorderColor = AppleUiColors.Separator;
		((Control)roundedPanel).BackColor = AppleUiColors.Surface;
		RoundedPanel roundedPanel2 = roundedPanel;
		((Control)this).Controls.Add((Control)(object)roundedPanel2);
		AddLabel((Control)(object)roundedPanel2, "规则名称", 18, 18, 80);
		((Control)txtName).Location = new Point(100, 15);
		((Control)txtName).Size = new Size(475, 24);
		txtName.TextAlign = (HorizontalAlignment)0;
		((Control)txtName).RightToLeft = (RightToLeft)0;
		((Control)txtName).Validated += delegate
		{
			CommitLoadedRuleName();
		};
		((Control)roundedPanel2).Controls.Add((Control)(object)txtName);
		AddLabel((Control)(object)roundedPanel2, "命名方式", 18, 50, 80);
		((Control)cboRenameMode).Location = new Point(100, 47);
		((Control)cboRenameMode).Size = new Size(170, 24);
		cboRenameMode.DropDownStyle = (ComboBoxStyle)2;
		cboRenameMode.Items.AddRange(new object[2] { "在线重命名", "另存重命名" });
		cboRenameMode.SelectedIndexChanged += delegate
		{
			UpdateSelectedRuleRenameMode();
		};
		((Control)roundedPanel2).Controls.Add((Control)(object)cboRenameMode);
		((Control)lblSavePath).Text = "另存路径";
		((Control)lblSavePath).Location = new Point(305, 50);
		((Control)lblSavePath).Size = new Size(70, 24);
		lblSavePath.TextAlign = (ContentAlignment)16;
		((Control)roundedPanel2).Controls.Add((Control)(object)lblSavePath);
		((Control)cboSavePathMode).Location = new Point(375, 47);
		((Control)cboSavePathMode).Size = new Size(170, 24);
		cboSavePathMode.DropDownStyle = (ComboBoxStyle)2;
		cboSavePathMode.Items.AddRange(new object[2] { "源文件地址", "自定义地址" });
		cboSavePathMode.SelectedIndexChanged += delegate
		{
			UpdateSelectedRuleSavePathMode();
		};
		((Control)roundedPanel2).Controls.Add((Control)(object)cboSavePathMode);
		AddLabel((Control)(object)roundedPanel2, "当前命名结构", 18, 82, 110);
		((Control)lstParts).Location = new Point(18, 108);
		((Control)lstParts).Size = new Size(335, 76);
		lstParts.SelectedIndexChanged += delegate
		{
			UpdatePartButtons();
		};
		((Control)roundedPanel2).Controls.Add((Control)(object)lstParts);
		((Control)btnMoveUp).Text = "上移";
		((Control)btnMoveUp).Location = new Point(380, 112);
		((Control)btnMoveUp).Size = new Size(75, 28);
		((Control)btnMoveUp).Click += delegate
		{
			MovePart(-1);
		};
		((Control)roundedPanel2).Controls.Add((Control)(object)btnMoveUp);
		((Control)btnMoveDown).Text = "下移";
		((Control)btnMoveDown).Location = new Point(470, 112);
		((Control)btnMoveDown).Size = new Size(75, 28);
		((Control)btnMoveDown).Click += delegate
		{
			MovePart(1);
		};
		((Control)roundedPanel2).Controls.Add((Control)(object)btnMoveDown);
		((Control)btnRemovePart).Text = "删除部件";
		((Control)btnRemovePart).Location = new Point(380, 150);
		((Control)btnRemovePart).Size = new Size(170, 30);
		((Control)btnRemovePart).Click += delegate
		{
			RemoveSelectedPart();
		};
		((Control)roundedPanel2).Controls.Add((Control)(object)btnRemovePart);
		AddLabel((Control)(object)roundedPanel2, "轮替词", 18, 190, 80);
		((Control)txtRotateWords).Location = new Point(96, 187);
		((Control)txtRotateWords).Size = new Size(494, 24);
		txtRotateWords.TextAlign = (HorizontalAlignment)0;
		((Control)txtRotateWords).RightToLeft = (RightToLeft)0;
		((Control)txtRotateWords).TextChanged += delegate
		{
			UpdateSelectedRuleRotateWords();
		};
		SetCueBanner(txtRotateWords, "输入多个轮替词，用顿号（、）分隔，命名将依次循环采用轮替词，如：张总、李工、王局");
		((Control)roundedPanel2).Controls.Add((Control)(object)txtRotateWords);
		((Control)chkResetRotateOnStartup).Text = "轮替重启软件初始化";
		((Control)chkResetRotateOnStartup).Location = new Point(100, 216);
		((Control)chkResetRotateOnStartup).Size = new Size(150, 24);
		chkResetRotateOnStartup.CheckedChanged += delegate
		{
			UpdateSelectedRuleRotateReset();
		};
		UiTextApplier.ApplyTooltip(toolTip, (Control)(object)chkResetRotateOnStartup, "Rename.RotateResetOnStartup");
		((Control)roundedPanel2).Controls.Add((Control)(object)chkResetRotateOnStartup);
		((Control)txtCustom).Location = new Point(18, 247);
		((Control)txtCustom).Size = new Size(422, 24);
		txtCustom.TextAlign = (HorizontalAlignment)0;
		((Control)txtCustom).RightToLeft = (RightToLeft)0;
		SetCueBanner(txtCustom, "输入自定义文字内容，可添加多条");
		((Control)roundedPanel2).Controls.Add((Control)(object)txtCustom);
		((Control)btnCustom).Text = "添加自定义";
		((Control)btnCustom).Location = new Point(455, 245);
		((Control)btnCustom).Size = new Size(115, 30);
		((Control)btnCustom).Click += delegate
		{
			AddCustomPart();
		};
		((Control)roundedPanel2).Controls.Add((Control)(object)btnCustom);
		AddLabel((Control)(object)roundedPanel2, "添加部件", 18, 286, 80);
		((Control)btnDocNumber).Text = "发文字号";
		((Control)btnDocNumber).Location = new Point(18, 312);
		((Control)btnDocNumber).Size = new Size(86, 30);
		((Control)btnDocNumber).Click += [MethodImpl(MethodImplOptions.NoInlining)] (object s, EventArgs e) =>
		{
			AddPart("docNumber");
		};
		((Control)roundedPanel2).Controls.Add((Control)(object)btnDocNumber);
		((Control)btnMainTitle).Text = "主标题";
		((Control)btnMainTitle).Location = new Point(112, 312);
		((Control)btnMainTitle).Size = new Size(78, 30);
		((Control)btnMainTitle).Click += [MethodImpl(MethodImplOptions.NoInlining)] (object s, EventArgs e) =>
		{
			AddPart("mainTitle");
		};
		((Control)roundedPanel2).Controls.Add((Control)(object)btnMainTitle);
		((Control)btnSubtitle).Text = "副标题";
		((Control)btnSubtitle).Location = new Point(198, 312);
		((Control)btnSubtitle).Size = new Size(78, 30);
		((Control)btnSubtitle).Click += [MethodImpl(MethodImplOptions.NoInlining)] (object s, EventArgs e) =>
		{
			AddPart("subtitle");
		};
		((Control)roundedPanel2).Controls.Add((Control)(object)btnSubtitle);
		((Control)btnDate).Text = "时间";
		((Control)btnDate).Location = new Point(284, 312);
		((Control)btnDate).Size = new Size(68, 30);
		((Control)btnDate).Click += [MethodImpl(MethodImplOptions.NoInlining)] (object s, EventArgs e) =>
		{
			AddPart("date");
		};
		((Control)roundedPanel2).Controls.Add((Control)(object)btnDate);
		((Control)btnRotate).Text = "轮替词";
		((Control)btnRotate).Location = new Point(360, 312);
		((Control)btnRotate).Size = new Size(78, 30);
		((Control)btnRotate).Click += [MethodImpl(MethodImplOptions.NoInlining)] (object s, EventArgs e) =>
		{
			AddPart("rotate");
		};
		((Control)roundedPanel2).Controls.Add((Control)(object)btnRotate);
		AddLabel((Control)(object)roundedPanel2, "时间格式", 372, 285, 78);
		((Control)cboDateFormat).Location = new Point(455, 282);
		((Control)cboDateFormat).Size = new Size(135, 24);
		cboDateFormat.DropDownStyle = (ComboBoxStyle)1;
		((Control)cboDateFormat).RightToLeft = (RightToLeft)0;
		cboDateFormat.Items.AddRange(new object[7] { "yyyy.M.d", "yyyy年M月d日", "yyyy-MM-dd", "yyyy.MM.dd", "yyyy.MM.dd HH:mm:ss", "yyyyMMdd", "M月d日" });
		((Control)cboDateFormat).TextChanged += delegate
		{
			UpdateSelectedRuleDateFormat();
		};
		cboDateFormat.SelectedIndexChanged += delegate
		{
			UpdateSelectedRuleDateFormat();
		};
		((Control)roundedPanel2).Controls.Add((Control)(object)cboDateFormat);
		AddLabel((Control)(object)roundedPanel2, "效果示例", 18, 352, 80);
		((Control)txtPreview).Location = new Point(100, 350);
		((Control)txtPreview).Size = new Size(490, 24);
		((TextBoxBase)txtPreview).ReadOnly = true;
		txtPreview.TextAlign = (HorizontalAlignment)0;
		((Control)txtPreview).RightToLeft = (RightToLeft)0;
		((Control)roundedPanel2).Controls.Add((Control)(object)txtPreview);
		Label val2 = new Label
		{
			Text = "点击部件按钮会按顺序追加；列表中的顺序就是最终文件名顺序。",
			Location = new Point(270, 452),
			Size = new Size(600, 24),
			ForeColor = AppleUiColors.TextSecondary
		};
		((Control)this).Controls.Add((Control)(object)val2);
		((Control)btnOk).Text = "确定";
		((Control)btnOk).Location = new Point(670, 505);
		((Control)btnOk).Size = new Size(90, 34);
		((Control)btnOk).BackColor = Color.FromArgb(32, 105, 190);
		((Control)btnOk).ForeColor = Color.White;
		((ButtonBase)btnOk).FlatStyle = (FlatStyle)0;
		((Control)btnOk).Click += delegate
		{
			SaveAndClose();
		};
		((Control)this).Controls.Add((Control)(object)btnOk);
		((Control)btnCancel).Text = "取消";
		((Control)btnCancel).Location = new Point(790, 505);
		((Control)btnCancel).Size = new Size(90, 34);
		((Control)btnCancel).Click += delegate
		{
			((Form)this).DialogResult = (DialogResult)2;
		};
		((Control)this).Controls.Add((Control)(object)btnCancel);
		ConfigureTooltips();
		toolTip.SetToolTip((Control)(object)lstRules, "选择要查看或编辑的命名规则；圆点标记表示当前执行规则。");
		toolTip.SetToolTip((Control)(object)btnAddRule, "新增一条命名规则，并在右侧继续编辑。");
		toolTip.SetToolTip((Control)(object)btnDeleteRule, "删除当前所选命名规则，操作前会再次确认。");
		toolTip.SetToolTip((Control)(object)btnSetActiveRule, "将当前所选命名规则设为一键命名默认规则。");
		toolTip.SetToolTip((Control)(object)btnOk, "保存全部命名规则和当前选择并关闭窗口。");
		toolTip.SetToolTip((Control)(object)btnCancel, "放弃本次未保存的命名规则修改。");
		AppleFormStyler.Apply((Form)(object)this, toolTip);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void ConfigureTooltips()
	{
		UiTextApplier.ApplyParameterTooltip(toolTip, (Control)(object)this, "Rename.RuleName", "规则名称", (Control)txtName);
		UiTextApplier.ApplyParameterTooltip(toolTip, (Control)(object)this, "Rename.Mode", "命名方式", (Control)cboRenameMode);
		UiTextApplier.ApplyParameterTooltip(toolTip, (Control)(object)this, "Rename.SavePath", "另存路径", (Control)cboSavePathMode);
		UiTextApplier.ApplyParameterTooltip(toolTip, (Control)(object)this, "Rename.Structure", "当前命名结构", (Control)lstParts, (Control)btnMoveUp, (Control)btnMoveDown, (Control)btnRemovePart);
		UiTextApplier.ApplyParameterTooltip(toolTip, (Control)(object)this, "Rename.RotateWords", "轮替词");
		UiTextApplier.ApplyParameterTooltip(toolTip, (Control)(object)this, "Rename.RotateResetOnStartup", "轮替重启软件初始化", (Control)chkResetRotateOnStartup);
		UiTextApplier.ApplyParameterTooltip(toolTip, (Control)(object)this, "Rename.CustomPart", "添加自定义", (Control)txtCustom, (Control)btnCustom);
		UiTextApplier.ApplyParameterTooltip(toolTip, (Control)(object)this, "Rename.Parts", "添加部件", (Control)btnDocNumber, (Control)btnMainTitle, (Control)btnSubtitle, (Control)btnDate, (Control)btnRotate);
		UiTextApplier.ApplyParameterTooltip(toolTip, (Control)(object)this, "Rename.DateFormat", "时间格式", (Control)cboDateFormat);
		UiTextApplier.ApplyParameterTooltip(toolTip, (Control)(object)this, "Rename.Preview", "效果示例", (Control)txtPreview);
	}

	private void AddLabel(Control parent, string text, int x, int y, int width)
	{
		parent.Controls.Add((Control)new Label
		{
			Text = text,
			Location = new Point(x, y),
			Size = new Size(width, 24),
			TextAlign = (ContentAlignment)16
		});
	}

	private static void SetCueBanner(TextBox textBox, string cue)
	{
		try
		{
			if (((Control)textBox).IsHandleCreated)
			{
				SendMessage(((Control)textBox).Handle, 5377, IntPtr.Zero, cue);
			}
			((Control)textBox).HandleCreated += delegate
			{
				SendMessage(((Control)textBox).Handle, 5377, IntPtr.Zero, cue);
			};
		}
		catch
		{
		}
	}

	private void ReloadRuleList()
	{
		string b = ((SelectedRule == null) ? ruleSet.ActiveRuleId : SelectedRule.Id);
		loading = true;
		loadedRuleIndex = -1;
		lstRules.Items.Clear();
		int val = -1;
		foreach (RenameRule rule in ruleSet.Rules)
		{
			RenameRuleManager.EnsureRuleParts(rule);
			lstRules.Items.Add((object)RuleDisplayName(rule));
			if (string.Equals(rule.Id, b, StringComparison.OrdinalIgnoreCase))
			{
				val = lstRules.Items.Count - 1;
			}
		}
		((ListControl)lstRules).SelectedIndex = ((ruleSet.Rules.Count > 0) ? Math.Max(0, val) : (-1));
		loading = false;
		LoadSelectedRuleToEditor();
	}

	private void HandleRuleSelectionChanged()
	{
		if (!loading)
		{
			CommitLoadedRuleName();
			LoadSelectedRuleToEditor();
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void LoadSelectedRuleToEditor()
	{
		RenameRule selectedRule = SelectedRule;
		loading = true;
		loadedRuleIndex = ((ListControl)lstRules).SelectedIndex;
		lstParts.Items.Clear();
		if (selectedRule == null)
		{
			((Control)txtName).Text = "";
			((ListControl)cboRenameMode).SelectedIndex = 0;
			((ListControl)cboSavePathMode).SelectedIndex = 0;
			RefreshSavePathControls(null);
			((Control)txtRotateWords).Text = "";
			chkResetRotateOnStartup.Checked = false;
			((Control)cboDateFormat).Text = "yyyy.MM.dd";
			((Control)txtPreview).Text = "";
		}
		else
		{
			RenameRuleManager.EnsureRuleParts(selectedRule);
			((Control)txtName).Text = selectedRule.Name;
			((ListControl)cboRenameMode).SelectedIndex = (string.Equals(selectedRule.RenameMode, "copy", StringComparison.OrdinalIgnoreCase) ? 1 : 0);
			((ListControl)cboSavePathMode).SelectedIndex = (string.Equals(selectedRule.SavePathMode, "custom", StringComparison.OrdinalIgnoreCase) ? 1 : 0);
			RefreshSavePathControls(selectedRule);
			((Control)txtRotateWords).Text = selectedRule.RotateWords ?? "";
			chkResetRotateOnStartup.Checked = selectedRule.ResetRotateOnStartup;
			((Control)cboDateFormat).Text = (string.IsNullOrWhiteSpace(selectedRule.DateFormat) ? "yyyy.MM.dd" : selectedRule.DateFormat);
			foreach (RenameRulePart part in selectedRule.Parts)
			{
				lstParts.Items.Add((object)PartDisplayName(part));
			}
			UpdatePreview(selectedRule);
		}
		loading = false;
		UpdatePartButtons();
	}

	private void CommitLoadedRuleName()
	{
		if (!loading && loadedRuleIndex >= 0 && loadedRuleIndex < ruleSet.Rules.Count)
		{
			RenameRule renameRule = ruleSet.Rules[loadedRuleIndex];
			renameRule.Name = ((Control)txtName).Text.Trim();
			RefreshRuleListText(loadedRuleIndex, renameRule);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void UpdateSelectedRuleRenameMode()
	{
		if (!loading)
		{
			RenameRule selectedRule = SelectedRule;
			if (selectedRule != null)
			{
				selectedRule.RenameMode = ((((ListControl)cboRenameMode).SelectedIndex == 1) ? "copy" : "online");
				RefreshSavePathControls(selectedRule);
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void UpdateSelectedRuleSavePathMode()
	{
		if (loading)
		{
			return;
		}
		RenameRule selectedRule = SelectedRule;
		if (selectedRule == null)
		{
			return;
		}
		if (((ListControl)cboSavePathMode).SelectedIndex == 1)
		{
			FolderBrowserDialog val = new FolderBrowserDialog();
			try
			{
				val.Description = "请选择另存重命名的保存文件夹";
				val.SelectedPath = (string.IsNullOrWhiteSpace(selectedRule.CustomSaveDirectory) ? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory) : selectedRule.CustomSaveDirectory);
				if ((int)((CommonDialog)val).ShowDialog((IWin32Window)(object)this) == 1)
				{
					selectedRule.SavePathMode = "custom";
					selectedRule.CustomSaveDirectory = val.SelectedPath;
				}
				else if (string.IsNullOrWhiteSpace(selectedRule.CustomSaveDirectory))
				{
					selectedRule.SavePathMode = "source";
					loading = true;
					((ListControl)cboSavePathMode).SelectedIndex = 0;
					loading = false;
				}
			}
			finally
			{
				((IDisposable)val)?.Dispose();
			}
		}
		else
		{
			selectedRule.SavePathMode = "source";
		}
		RefreshSavePathControls(selectedRule);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void RefreshSavePathControls(RenameRule rule)
	{
		bool flag = rule != null && string.Equals(rule.RenameMode, "copy", StringComparison.OrdinalIgnoreCase);
		((Control)lblSavePath).Visible = flag;
		((Control)cboSavePathMode).Visible = flag;
		if (!flag || rule == null || !string.Equals(rule.SavePathMode, "custom", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(rule.CustomSaveDirectory))
		{
			((Control)cboSavePathMode).Tag = null;
			((Control)cboSavePathMode).AccessibleDescription = "";
			UiTextApplier.ApplyTooltip(toolTip, (Control)(object)cboSavePathMode, "Rename.SavePath");
		}
		else
		{
			((Control)cboSavePathMode).Tag = rule.CustomSaveDirectory;
			((Control)cboSavePathMode).AccessibleDescription = rule.CustomSaveDirectory;
			UiTextApplier.ApplyTooltipText(toolTip, (Control)(object)cboSavePathMode, rule.CustomSaveDirectory);
		}
	}

	private void UpdateSelectedRuleDateFormat()
	{
		if (!loading)
		{
			RenameRule selectedRule = SelectedRule;
			if (selectedRule != null)
			{
				selectedRule.DateFormat = ((Control)cboDateFormat).Text.Trim();
				UpdatePreview(selectedRule);
			}
		}
	}

	private void UpdateSelectedRuleRotateWords()
	{
		if (!loading)
		{
			RenameRule selectedRule = SelectedRule;
			if (selectedRule != null)
			{
				selectedRule.RotateWords = ((Control)txtRotateWords).Text.Trim();
				UpdatePreview(selectedRule);
			}
		}
	}

	private void UpdateSelectedRuleRotateReset()
	{
		if (!loading)
		{
			RenameRule selectedRule = SelectedRule;
			if (selectedRule != null)
			{
				selectedRule.ResetRotateOnStartup = chkResetRotateOnStartup.Checked;
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void AddRule()
	{
		CommitLoadedRuleName();
		RenameRule renameRule = new RenameRule
		{
			Name = "新规则",
			Parts = new List<RenameRulePart>
			{
				new RenameRulePart("mainTitle")
			},
			RenameMode = "online",
			SavePathMode = "source",
			RotateWords = "",
			RotateIndex = 0,
			ResetRotateOnStartup = false,
			DateFormat = "yyyy.MM.dd"
		};
		ruleSet.Rules.Add(renameRule);
		lstRules.Items.Add((object)renameRule.Name);
		((ListControl)lstRules).SelectedIndex = lstRules.Items.Count - 1;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void DeleteRule()
	{
		CommitLoadedRuleName();
		if (SelectedRule == null)
		{
			return;
		}
		if (ruleSet.Rules.Count > 1)
		{
			int selectedIndex = ((ListControl)lstRules).SelectedIndex;
			ruleSet.Rules.RemoveAt(selectedIndex);
			if (!ContainsRuleId(ruleSet.ActiveRuleId))
			{
				ruleSet.ActiveRuleId = ruleSet.Rules[0].Id;
			}
			ReloadRuleList();
			if (lstRules.Items.Count > 0)
			{
				((ListControl)lstRules).SelectedIndex = Math.Min(selectedIndex, lstRules.Items.Count - 1);
			}
		}
		else
		{
			AppleMessageDialog.Show((IWin32Window)(object)this, "至少需要保留一条命名规则。", "提示", (MessageBoxButtons)0, (MessageBoxIcon)64);
		}
	}

	private void SetActiveRule()
	{
		CommitLoadedRuleName();
		RenameRule selectedRule = SelectedRule;
		if (selectedRule != null)
		{
			ruleSet.ActiveRuleId = selectedRule.Id;
			ReloadRuleList();
		}
	}

	private void AddPart(string type)
	{
		RenameRule selectedRule = SelectedRule;
		if (selectedRule != null)
		{
			RenameRuleManager.EnsureRuleParts(selectedRule);
			selectedRule.Parts.Add(new RenameRulePart(type));
			lstParts.Items.Add((object)PartDisplayName(selectedRule.Parts[selectedRule.Parts.Count - 1]));
			((ListControl)lstParts).SelectedIndex = lstParts.Items.Count - 1;
			UpdatePreview(selectedRule);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void AddCustomPart()
	{
		string text = ((Control)txtCustom).Text;
		if (!string.IsNullOrWhiteSpace(text))
		{
			RenameRule selectedRule = SelectedRule;
			if (selectedRule != null)
			{
				RenameRuleManager.EnsureRuleParts(selectedRule);
				RenameRulePart renameRulePart = new RenameRulePart("custom", text);
				selectedRule.Parts.Add(renameRulePart);
				lstParts.Items.Add((object)PartDisplayName(renameRulePart));
				((ListControl)lstParts).SelectedIndex = lstParts.Items.Count - 1;
				((Control)txtCustom).Text = "";
				UpdatePreview(selectedRule);
			}
		}
		else
		{
			AppleMessageDialog.Show((IWin32Window)(object)this, "请输入自定义文字后再添加。", "提示", (MessageBoxButtons)0, (MessageBoxIcon)64);
		}
	}

	private void RemoveSelectedPart()
	{
		RenameRule selectedRule = SelectedRule;
		if (selectedRule != null && ((ListControl)lstParts).SelectedIndex >= 0)
		{
			int selectedIndex = ((ListControl)lstParts).SelectedIndex;
			selectedRule.Parts.RemoveAt(selectedIndex);
			lstParts.Items.RemoveAt(selectedIndex);
			if (lstParts.Items.Count > 0)
			{
				((ListControl)lstParts).SelectedIndex = Math.Min(selectedIndex, lstParts.Items.Count - 1);
			}
			UpdatePreview(selectedRule);
		}
	}

	private void MovePart(int offset)
	{
		CommitLoadedRuleName();
		RenameRule selectedRule = SelectedRule;
		if (selectedRule != null && ((ListControl)lstParts).SelectedIndex >= 0)
		{
			int selectedIndex = ((ListControl)lstParts).SelectedIndex;
			int num = selectedIndex + offset;
			if (num >= 0 && num < selectedRule.Parts.Count)
			{
				RenameRulePart item = selectedRule.Parts[selectedIndex];
				selectedRule.Parts.RemoveAt(selectedIndex);
				selectedRule.Parts.Insert(num, item);
				LoadSelectedRuleToEditor();
				((ListControl)lstParts).SelectedIndex = num;
			}
		}
	}

	private void UpdatePartButtons()
	{
		bool flag = SelectedRule != null && ((ListControl)lstParts).SelectedIndex >= 0;
		((Control)btnRemovePart).Enabled = flag;
		((Control)btnMoveUp).Enabled = flag && ((ListControl)lstParts).SelectedIndex > 0;
		((Control)btnMoveDown).Enabled = flag && ((ListControl)lstParts).SelectedIndex >= 0 && ((ListControl)lstParts).SelectedIndex < lstParts.Items.Count - 1;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void UpdatePreview(RenameRule rule)
	{
		if (rule != null)
		{
			List<string> list = new List<string>();
			RenameRuleManager.EnsureRuleParts(rule);
			foreach (RenameRulePart part in rule.Parts)
			{
				if (part == null)
				{
					continue;
				}
				if (!string.Equals(part.Type, "docNumber", StringComparison.OrdinalIgnoreCase))
				{
					if (string.Equals(part.Type, "mainTitle", StringComparison.OrdinalIgnoreCase))
					{
						list.Add(PreviewValue(previewInfo.MainTitle));
					}
					else if (!string.Equals(part.Type, "subtitle", StringComparison.OrdinalIgnoreCase))
					{
						if (string.Equals(part.Type, "date", StringComparison.OrdinalIgnoreCase))
						{
							list.Add(SafeFormatDate(rule.DateFormat));
						}
						else if (!string.Equals(part.Type, "rotate", StringComparison.OrdinalIgnoreCase))
						{
							if (string.Equals(part.Type, "custom", StringComparison.OrdinalIgnoreCase))
							{
								list.Add(part.Text ?? "");
							}
						}
						else
						{
							list.Add(PreviewRotateWord(rule));
						}
					}
					else
					{
						list.Add(PreviewValue(previewInfo.Subtitle));
					}
				}
				else
				{
					list.Add(PreviewValue(previewInfo.DocumentNumber));
				}
			}
			((Control)txtPreview).Text = string.Join("", list.ToArray());
		}
		else
		{
			((Control)txtPreview).Text = "";
		}
	}

	private string PreviewValue(string realValue)
	{
		if (!string.IsNullOrWhiteSpace(realValue))
		{
			return realValue.Trim();
		}
		return "";
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private string PreviewRotateWord(RenameRule rule)
	{
		List<string> list = RenameRuleManager.ParseRotateWords((rule == null) ? "" : rule.RotateWords);
		if (list.Count == 0)
		{
			return "轮替词";
		}
		if (rule.ResetRotateOnStartup)
		{
			return list[0];
		}
		int num = rule.RotateIndex;
		if (num < 0)
		{
			num = 0;
		}
		num %= list.Count;
		return list[num];
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private string SafeFormatDate(string format)
	{
		try
		{
			return DateTime.Now.ToString(string.IsNullOrWhiteSpace(format) ? "yyyy.MM.dd" : format);
		}
		catch
		{
			return "时间格式错误";
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private string PartDisplayName(RenameRulePart part)
	{
		if (part == null)
		{
			return "未知部件";
		}
		if (string.Equals(part.Type, "docNumber", StringComparison.OrdinalIgnoreCase))
		{
			return "发文字号";
		}
		if (string.Equals(part.Type, "mainTitle", StringComparison.OrdinalIgnoreCase))
		{
			return "主标题";
		}
		if (!string.Equals(part.Type, "subtitle", StringComparison.OrdinalIgnoreCase))
		{
			if (!string.Equals(part.Type, "date", StringComparison.OrdinalIgnoreCase))
			{
				if (!string.Equals(part.Type, "rotate", StringComparison.OrdinalIgnoreCase))
				{
					if (string.Equals(part.Type, "custom", StringComparison.OrdinalIgnoreCase))
					{
						return "自定义：" + part.Text;
					}
					return "未知部件";
				}
				return "轮替词";
			}
			return "当前时间";
		}
		return "副标题";
	}

	private bool ContainsRuleId(string id)
	{
		foreach (RenameRule rule in ruleSet.Rules)
		{
			if (string.Equals(rule.Id, id, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}
		return false;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private string RuleDisplayName(RenameRule rule)
	{
		if (rule == null)
		{
			return "";
		}
		return (string.Equals(rule.Id, ruleSet.ActiveRuleId, StringComparison.OrdinalIgnoreCase) ? "● " : "") + rule.Name;
	}

	private void RefreshRuleListText(int index, RenameRule rule)
	{
		if (rule == null || index < 0 || index >= lstRules.Items.Count)
		{
			return;
		}
		bool flag = loading;
		loading = true;
		lstRules.BeginUpdate();
		try
		{
			lstRules.Items[index] = RuleDisplayName(rule);
		}
		finally
		{
			lstRules.EndUpdate();
			loading = flag;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void SaveAndClose()
	{
		CommitLoadedRuleName();
		if ((RuleManagementPermissionForTesting != null) ? RuleManagementPermissionForTesting() : PermissionChecker.CanManageRules())
		{
			if (ruleSet.Rules.Count != 0)
			{
				HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				foreach (RenameRule rule in ruleSet.Rules)
				{
					if (RenameRuleManager.ValidateRule(rule, out var message))
					{
						if (hashSet.Add(rule.Name.Trim()))
						{
							continue;
						}
						AppleMessageDialog.Show((IWin32Window)(object)this, "规则名称不能重复：" + rule.Name, "提示", (MessageBoxButtons)0, (MessageBoxIcon)48);
						return;
					}
					AppleMessageDialog.Show((IWin32Window)(object)this, "规则“" + rule.Name + "”设置不正确：\r\n" + message, "提示", (MessageBoxButtons)0, (MessageBoxIcon)48);
					return;
				}
				if (!ContainsRuleId(ruleSet.ActiveRuleId))
				{
					ruleSet.ActiveRuleId = ruleSet.Rules[0].Id;
				}
				RenameRuleManager.Save(ruleSet);
				((Form)this).DialogResult = (DialogResult)1;
			}
			else
			{
				AppleMessageDialog.Show((IWin32Window)(object)this, "至少需要保留一条命名规则。", "提示", (MessageBoxButtons)0, (MessageBoxIcon)48);
			}
		}
		else
		{
			AppleMessageDialog.Show((IWin32Window)(object)this, PermissionChecker.GetRuleManagementDeniedMessage("一键命名", "设置命名规则"), "一键命名", (MessageBoxButtons)0, (MessageBoxIcon)64);
		}
	}
}
