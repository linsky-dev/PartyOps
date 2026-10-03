using System;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using DocumentRepository.Models;
using DocumentRepository.Models.Rules;
using DocumentRepository.Services.Auth;
using DocumentRepository.Services.Replace;
using DocumentRepository.Services.Rules;
using DocumentRepository.Services.UiText;

namespace DocumentRepository;

public class ReplacePlanSettingsForm : Form
{
	private ReplacePlanSet set;

	private bool storeRecoveryDeclined;

	private int loadedPlanIndex = -1;

	private bool loading;

	private readonly ListBox lstPlans = new ListBox();

	private readonly ListBox lstRules = new ListBox();

	private readonly TextBox txtName = new TextBox();

	private readonly Button btnAddRule = new Button();

	private readonly Button btnEditRule = new Button();

	private readonly Button btnCopyRule = new Button();

	private readonly Button btnDeleteRule = new Button();

	private readonly Button btnMoveUp = new Button();

	private readonly Button btnMoveDown = new Button();

	private readonly Button btnDeletePlan = new Button();

	private readonly ToolTip toolTip = UiTextApplier.CreateToolTip();

	private ReplacePlan CurrentPlan
	{
		get
		{
			if (loadedPlanIndex < 0 || loadedPlanIndex >= set.Plans.Count)
			{
				return null;
			}
			return set.Plans[loadedPlanIndex];
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public ReplacePlanSettingsForm()
	{
		InitializeComponent();
		RuleLoadResult<ReplacePlanSet> ruleLoadResult = ReplacePlanService.LoadResult();
		if (!ruleLoadResult.Usable && !RuleStoreResetPrompt.ConfirmAndReset((IWin32Window)(object)this, ruleLoadResult, "替换规则库", ReplacePlanService.ResetToDefault))
		{
			storeRecoveryDeclined = true;
			set = new ReplacePlanSet
			{
				ActivePlanId = ""
			};
		}
		else
		{
			set = ReplacePlanService.LoadResult().Value;
		}
		RefreshPlans(FindInitialPlanIndex());
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
	private void InitializeComponent()
	{
		((Control)this).Text = "一键替换方案设置";
		((Form)this).StartPosition = (FormStartPosition)4;
		((Form)this).ClientSize = new Size(1080, 620);
		((Control)this).MinimumSize = new Size(1080, 620);
		((Form)this).MaximizeBox = false;
		((Form)this).MinimizeBox = false;
		((Form)this).FormBorderStyle = (FormBorderStyle)3;
		((Control)this).Font = new Font("Microsoft YaHei UI", 9f);
		((Control)this).BackColor = AppleUiColors.Window;
		((Control)this).Controls.Add((Control)new Label
		{
			Text = "一键替换方案设置",
			Left = 20,
			Top = 16,
			AutoSize = true,
			Font = new Font("Microsoft YaHei UI", 12f, (FontStyle)1),
			ForeColor = AppleUiColors.TextPrimary
		});
		((Control)this).Controls.Add((Control)new Label
		{
			Text = "先选择一个替换方案，再按顺序设置需要执行的替换步骤。",
			Left = 220,
			Top = 22,
			Width = 600,
			ForeColor = Color.DimGray
		});
		AppleGroupBox appleGroupBox = new AppleGroupBox();
		((Control)appleGroupBox).Text = "替换方案";
		((Control)appleGroupBox).Left = 18;
		((Control)appleGroupBox).Top = 50;
		((Control)appleGroupBox).Width = 260;
		((Control)appleGroupBox).Height = 500;
		AppleGroupBox appleGroupBox2 = appleGroupBox;
		((Control)this).Controls.Add((Control)(object)appleGroupBox2);
		((Control)lstPlans).SetBounds(14, 28, 230, 360);
		lstPlans.SelectedIndexChanged += delegate
		{
			HandlePlanSelectionChanged();
		};
		((Control)appleGroupBox2).Controls.Add((Control)(object)lstPlans);
		AddButton((Control)(object)appleGroupBox2, "新增方案", 14, 400, 108, delegate
		{
			AddPlan();
		});
		AddButton((Control)(object)appleGroupBox2, "复制方案", 136, 400, 108, delegate
		{
			CopyPlan();
		});
		((Control)btnDeletePlan).Text = "删除方案";
		((Control)btnDeletePlan).SetBounds(14, 440, 108, 30);
		((Control)btnDeletePlan).Click += delegate
		{
			DeletePlan();
		};
		((Control)appleGroupBox2).Controls.Add((Control)(object)btnDeletePlan);
		AddButton((Control)(object)appleGroupBox2, "设为当前", 136, 440, 108, delegate
		{
			SetActive();
		});
		AppleGroupBox appleGroupBox3 = new AppleGroupBox();
		((Control)appleGroupBox3).Text = "当前方案";
		((Control)appleGroupBox3).Left = 294;
		((Control)appleGroupBox3).Top = 50;
		((Control)appleGroupBox3).Width = 768;
		((Control)appleGroupBox3).Height = 88;
		AppleGroupBox appleGroupBox4 = appleGroupBox3;
		((Control)this).Controls.Add((Control)(object)appleGroupBox4);
		((Control)appleGroupBox4).Controls.Add((Control)new Label
		{
			Text = "方案名称",
			Left = 16,
			Top = 31,
			Width = 75
		});
		((Control)txtName).SetBounds(92, 27, 430, 25);
		((Control)appleGroupBox4).Controls.Add((Control)(object)txtName);
		((Control)appleGroupBox4).Controls.Add((Control)new Label
		{
			Text = "有选中文字时替换选区，否则替换全文。点击“一键替换”将执行当前方案。",
			Left = 92,
			Top = 57,
			Width = 650,
			ForeColor = Color.DimGray
		});
		AppleGroupBox appleGroupBox5 = new AppleGroupBox();
		((Control)appleGroupBox5).Text = "替换步骤（从上到下依次执行）";
		((Control)appleGroupBox5).Left = 294;
		((Control)appleGroupBox5).Top = 150;
		((Control)appleGroupBox5).Width = 768;
		((Control)appleGroupBox5).Height = 400;
		AppleGroupBox appleGroupBox6 = appleGroupBox5;
		((Control)this).Controls.Add((Control)(object)appleGroupBox6);
		((Control)lstRules).SetBounds(14, 28, 610, 350);
		lstRules.SelectedIndexChanged += delegate
		{
			UpdateRuleButtonState();
		};
		((Control)lstRules).DoubleClick += delegate
		{
			EditRule();
		};
		((Control)appleGroupBox6).Controls.Add((Control)(object)lstRules);
		ConfigureRuleButton((Control)(object)appleGroupBox6, btnAddRule, "新增条目", 28, delegate
		{
			AddRule();
		});
		ConfigureRuleButton((Control)(object)appleGroupBox6, btnEditRule, "编辑条目", 68, delegate
		{
			EditRule();
		});
		ConfigureRuleButton((Control)(object)appleGroupBox6, btnCopyRule, "复制条目", 108, delegate
		{
			CopyRule();
		});
		ConfigureRuleButton((Control)(object)appleGroupBox6, btnDeleteRule, "删除条目", 148, delegate
		{
			DeleteRule();
		});
		((Control)btnMoveUp).Text = "上移";
		((Control)btnMoveUp).SetBounds(640, 188, 52, 30);
		((Control)btnMoveUp).Click += delegate
		{
			MoveRule(-1);
		};
		((Control)appleGroupBox6).Controls.Add((Control)(object)btnMoveUp);
		((Control)btnMoveDown).Text = "下移";
		((Control)btnMoveDown).SetBounds(698, 188, 52, 30);
		((Control)btnMoveDown).Click += delegate
		{
			MoveRule(1);
		};
		((Control)appleGroupBox6).Controls.Add((Control)(object)btnMoveDown);
		Button val = new Button
		{
			Text = "确定",
			Left = 842,
			Top = 568,
			Width = 100,
			Height = 34,
			BackColor = Color.FromArgb(32, 105, 190),
			ForeColor = Color.White,
			FlatStyle = (FlatStyle)0
		};
		((Control)val).Click += delegate
		{
			SaveCurrentAndClose();
		};
		((Control)this).Controls.Add((Control)(object)val);
		Button val2 = new Button
		{
			Text = "取消",
			Left = 962,
			Top = 568,
			Width = 100,
			Height = 34,
			DialogResult = (DialogResult)2
		};
		((Control)this).Controls.Add((Control)(object)val2);
		((Form)this).CancelButton = (IButtonControl)(object)val2;
		ConfigureTooltips();
		toolTip.SetToolTip((Control)(object)btnAddRule, "在当前替换方案末尾新增一个条目。");
		toolTip.SetToolTip((Control)(object)btnEditRule, "打开当前所选条目的详细设置。");
		toolTip.SetToolTip((Control)(object)btnCopyRule, "复制当前所选条目并追加到方案末尾。");
		toolTip.SetToolTip((Control)(object)btnDeleteRule, "删除当前所选条目，操作前会再次确认。");
		toolTip.SetToolTip((Control)(object)btnMoveUp, "将当前条目向前移动一步，调整执行顺序。");
		toolTip.SetToolTip((Control)(object)btnMoveDown, "将当前条目向后移动一步，调整执行顺序。");
		toolTip.SetToolTip((Control)(object)btnDeletePlan, "删除当前替换方案，操作前会再次确认。");
		toolTip.SetToolTip((Control)(object)val, "保存全部替换方案和条目并关闭窗口。");
		toolTip.SetToolTip((Control)(object)val2, "放弃本次未保存的替换条目修改。");
		AppleFormStyler.Apply((Form)(object)this, toolTip);
	}

	private int FindInitialPlanIndex()
	{
		int num = set.Plans.FindIndex((ReplacePlan plan) => string.Equals(plan.Id, set.ActivePlanId, StringComparison.OrdinalIgnoreCase));
		if (num < 0)
		{
			return 0;
		}
		return num;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void RefreshPlans(int selectedIndex)
	{
		loading = true;
		try
		{
			lstPlans.Items.Clear();
			foreach (ReplacePlan plan in set.Plans)
			{
				string text = (string.Equals(plan.Id, set.ActivePlanId, StringComparison.OrdinalIgnoreCase) ? "● " : string.Empty);
				lstPlans.Items.Add((object)(text + plan.Name));
			}
			if (lstPlans.Items.Count > 0)
			{
				((ListControl)lstPlans).SelectedIndex = Math.Max(0, Math.Min(selectedIndex, lstPlans.Items.Count - 1));
			}
		}
		finally
		{
			loading = false;
		}
		LoadSelectedPlan(((ListControl)lstPlans).SelectedIndex);
	}

	private void HandlePlanSelectionChanged()
	{
		if (!loading)
		{
			SaveLoadedPlan();
			LoadSelectedPlan(((ListControl)lstPlans).SelectedIndex);
		}
	}

	private void LoadSelectedPlan(int index)
	{
		loadedPlanIndex = index;
		loading = true;
		try
		{
			((Control)txtName).Text = CurrentPlan?.Name ?? string.Empty;
			RefreshRules();
		}
		finally
		{
			loading = false;
		}
		UpdateRuleButtonState();
	}

	private void RefreshRules()
	{
		int selectedIndex = ((ListControl)lstRules).SelectedIndex;
		lstRules.Items.Clear();
		ReplacePlan currentPlan = CurrentPlan;
		if (currentPlan?.Rules != null)
		{
			for (int i = 0; i < currentPlan.Rules.Count; i++)
			{
				lstRules.Items.Add((object)ReplaceRuleSummaryService.Build(currentPlan.Rules[i], i + 1));
			}
		}
		if (lstRules.Items.Count > 0)
		{
			((ListControl)lstRules).SelectedIndex = ((selectedIndex >= 0) ? Math.Min(selectedIndex, lstRules.Items.Count - 1) : 0);
		}
		UpdateRuleButtonState();
	}

	private void UpdateRuleButtonState()
	{
		bool flag = ((ListControl)lstRules).SelectedIndex >= 0;
		((Control)btnEditRule).Enabled = flag;
		((Control)btnCopyRule).Enabled = flag;
		((Control)btnDeleteRule).Enabled = flag;
		((Control)btnMoveUp).Enabled = flag && ((ListControl)lstRules).SelectedIndex > 0;
		((Control)btnMoveDown).Enabled = flag && ((ListControl)lstRules).SelectedIndex < lstRules.Items.Count - 1;
		((Control)btnDeletePlan).Enabled = set != null && set.Plans.Count > 1;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void AddPlan()
	{
		SaveLoadedPlan();
		set.Plans.Add(new ReplacePlan
		{
			Name = "新替换方案"
		});
		RefreshPlans(set.Plans.Count - 1);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void CopyPlan()
	{
		ReplacePlan currentPlan = CurrentPlan;
		if (currentPlan != null)
		{
			SaveLoadedPlan();
			ReplacePlan replacePlan = currentPlan.Clone();
			replacePlan.Id = Guid.NewGuid().ToString("N");
			replacePlan.Name = currentPlan.Name + " 副本";
			set.Plans.Add(replacePlan);
			RefreshPlans(set.Plans.Count - 1);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void DeletePlan()
	{
		if (CurrentPlan == null)
		{
			return;
		}
		if (set.Plans.Count > 1)
		{
			int num = loadedPlanIndex;
			set.Plans.RemoveAt(num);
			if (!set.Plans.Exists((ReplacePlan p) => string.Equals(p.Id, set.ActivePlanId, StringComparison.OrdinalIgnoreCase)))
			{
				set.ActivePlanId = set.Plans[0].Id;
			}
			loadedPlanIndex = -1;
			RefreshPlans(Math.Min(num, set.Plans.Count - 1));
		}
		else
		{
			AppleMessageDialog.Show((IWin32Window)(object)this, "至少保留一个替换方案。", "提示");
		}
	}

	private void SetActive()
	{
		if (CurrentPlan != null)
		{
			SaveLoadedPlan();
			set.ActivePlanId = CurrentPlan.Id;
			RefreshPlans(loadedPlanIndex);
		}
	}

	private void AddRule()
	{
		ReplacePlan currentPlan = CurrentPlan;
		if (currentPlan == null)
		{
			return;
		}
		ReplaceRuleEditorForm replaceRuleEditorForm = new ReplaceRuleEditorForm(new ReplaceRule
		{
			Enabled = true
		});
		try
		{
			if ((int)((Form)replaceRuleEditorForm).ShowDialog((IWin32Window)(object)this) == 1)
			{
				currentPlan.Rules.Add(replaceRuleEditorForm.Rule);
				RefreshRules();
				((ListControl)lstRules).SelectedIndex = currentPlan.Rules.Count - 1;
			}
		}
		finally
		{
			((IDisposable)replaceRuleEditorForm)?.Dispose();
		}
	}

	private void EditRule()
	{
		ReplacePlan currentPlan = CurrentPlan;
		int selectedIndex = ((ListControl)lstRules).SelectedIndex;
		if (currentPlan == null || selectedIndex < 0 || selectedIndex >= currentPlan.Rules.Count)
		{
			return;
		}
		ReplaceRuleEditorForm replaceRuleEditorForm = new ReplaceRuleEditorForm(currentPlan.Rules[selectedIndex]);
		try
		{
			if ((int)((Form)replaceRuleEditorForm).ShowDialog((IWin32Window)(object)this) == 1)
			{
				currentPlan.Rules[selectedIndex] = replaceRuleEditorForm.Rule;
				RefreshRules();
				((ListControl)lstRules).SelectedIndex = selectedIndex;
			}
		}
		finally
		{
			((IDisposable)replaceRuleEditorForm)?.Dispose();
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void CopyRule()
	{
		ReplacePlan currentPlan = CurrentPlan;
		int selectedIndex = ((ListControl)lstRules).SelectedIndex;
		if (currentPlan != null && selectedIndex >= 0 && selectedIndex < currentPlan.Rules.Count)
		{
			ReplaceRule replaceRule = ReplaceRuleNormalizer.Clone(currentPlan.Rules[selectedIndex]);
			replaceRule.Name += " 副本";
			currentPlan.Rules.Insert(selectedIndex + 1, replaceRule);
			RefreshRules();
			((ListControl)lstRules).SelectedIndex = selectedIndex + 1;
		}
	}

	private void DeleteRule()
	{
		ReplacePlan currentPlan = CurrentPlan;
		int selectedIndex = ((ListControl)lstRules).SelectedIndex;
		if (currentPlan != null && selectedIndex >= 0 && selectedIndex < currentPlan.Rules.Count)
		{
			currentPlan.Rules.RemoveAt(selectedIndex);
			RefreshRules();
			if (lstRules.Items.Count > 0)
			{
				((ListControl)lstRules).SelectedIndex = Math.Min(selectedIndex, lstRules.Items.Count - 1);
			}
		}
	}

	private void MoveRule(int delta)
	{
		ReplacePlan currentPlan = CurrentPlan;
		int selectedIndex = ((ListControl)lstRules).SelectedIndex;
		int num = selectedIndex + delta;
		if (currentPlan != null && selectedIndex >= 0 && num >= 0 && num < currentPlan.Rules.Count)
		{
			ReplaceRule item = currentPlan.Rules[selectedIndex];
			currentPlan.Rules.RemoveAt(selectedIndex);
			currentPlan.Rules.Insert(num, item);
			RefreshRules();
			((ListControl)lstRules).SelectedIndex = num;
		}
	}

	private void SaveLoadedPlan()
	{
		if (!loading && CurrentPlan != null && !string.IsNullOrWhiteSpace(((Control)txtName).Text))
		{
			CurrentPlan.Name = ((Control)txtName).Text.Trim();
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void SaveCurrentAndClose()
	{
		if (!PermissionChecker.CanManageRules())
		{
			AppleMessageDialog.Show((IWin32Window)(object)this, PermissionChecker.GetRuleManagementDeniedMessage("一键替换", "设置替换方案"), "一键替换", (MessageBoxButtons)0, (MessageBoxIcon)64);
			return;
		}
		SaveLoadedPlan();
		ReplacePlanService.Save(set);
		((Form)this).DialogResult = (DialogResult)1;
		((Form)this).Close();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void ConfigureTooltips()
	{
		UiTextApplier.ApplyParameterTooltip(toolTip, (Control)(object)this, "Replace.PlanList", "替换方案", (Control)lstPlans);
		UiTextApplier.ApplyParameterTooltip(toolTip, (Control)(object)this, "Replace.PlanName", "方案名称", (Control)txtName);
		UiTextApplier.ApplyParameterTooltip(toolTip, (Control)(object)this, "Replace.Rules", "替换步骤（从上到下依次执行）", (Control)lstRules);
	}

	private static void AddButton(Control parent, string text, int x, int y, int width, EventHandler handler)
	{
		Button val = new Button
		{
			Text = text,
			Left = x,
			Top = y,
			Width = width,
			Height = 30
		};
		((Control)val).Click += handler;
		parent.Controls.Add((Control)(object)val);
	}

	private static void ConfigureRuleButton(Control parent, Button button, string text, int top, EventHandler handler)
	{
		((Control)button).Text = text;
		((Control)button).SetBounds(640, top, 110, 30);
		((Control)button).Click += handler;
		parent.Controls.Add((Control)(object)button);
	}
}
