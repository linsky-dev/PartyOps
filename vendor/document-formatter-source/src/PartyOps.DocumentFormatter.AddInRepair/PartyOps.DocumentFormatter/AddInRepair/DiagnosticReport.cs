using System;
using System.Collections.Generic;
using System.Text;

namespace PartyOps.DocumentFormatter.AddInRepair;

internal sealed class DiagnosticReport
{
	private readonly List<DiagnosticItem> _items = new List<DiagnosticItem>();

	public IList<DiagnosticItem> Items => _items;

	public string InstallDirectory { get; set; }

	public string BackupPath { get; set; }

	public bool HasErrors => _items.Exists((DiagnosticItem item) => item.Severity == DiagnosticSeverity.Error);

	public bool HasWarnings => _items.Exists((DiagnosticItem item) => item.Severity == DiagnosticSeverity.Warning);

	public bool HasRepairableProblems => _items.Exists((DiagnosticItem item) => item.Repairable);

	public void Add(DiagnosticSeverity severity, string title, string detail, bool repairable)
	{
		_items.Add(new DiagnosticItem
		{
			Severity = severity,
			Title = (title ?? string.Empty),
			Detail = (detail ?? string.Empty),
			Repairable = repairable
		});
	}

	public string ToDisplayText()
	{
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.AppendLine("partyops公文排版助手加载项检测结果");
		stringBuilder.AppendLine("检测时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
		stringBuilder.AppendLine("安装目录：" + (InstallDirectory ?? "未确定"));
		stringBuilder.AppendLine(new string('-', 72));
		foreach (DiagnosticItem item in _items)
		{
			string text = ((item.Severity == DiagnosticSeverity.Error) ? "[错误]" : ((item.Severity == DiagnosticSeverity.Warning) ? "[提醒]" : "[正常]"));
			stringBuilder.AppendLine(text + " " + item.Title);
			if (!string.IsNullOrWhiteSpace(item.Detail))
			{
				stringBuilder.AppendLine("       " + item.Detail);
			}
		}
		if (!string.IsNullOrWhiteSpace(BackupPath))
		{
			stringBuilder.AppendLine(new string('-', 72));
			stringBuilder.AppendLine("修复前备份：" + BackupPath);
		}
		stringBuilder.AppendLine(new string('-', 72));
		stringBuilder.AppendLine(HasErrors ? "结论：发现需要处理的问题。可修复项仅涉及partyops公文排版助手自身。" : (HasWarnings ? "结论：发现需要进一步核实的提醒，不能判定加载链路完全正常。" : "结论：未发现影响加载的注册、运行时或策略问题。"));
		return stringBuilder.ToString();
	}
}
