using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using DocumentRepository.Models;
using DocumentRepository.Models.Mutations;
using DocumentRepository.Models.Rules;
using DocumentRepository.Models.Tasks;

namespace DocumentRepository.Services.Ui;

public static class UserOutcomeMapper
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static CommandResult MapRecoveredFailure(string featureDisplayName, bool recoveryRequired, string recoveryGuidance, string recoveryId)
	{
		if (recoveryRequired)
		{
			CommandResult commandResult = CommandResult.FailResult(featureDisplayName + "执行失败，且文档回滚后未能确认恢复。请立即关闭该文档并选择不保存，避免保留不一致内容。" + ((recoveryGuidance == null) ? string.Empty : ("\r\n" + recoveryGuidance)));
			commandResult.OutcomeKind = UserOutcomeKind.RecoveryRequired;
			commandResult.RecoveryId = recoveryId;
			return commandResult;
		}
		CommandResult commandResult2 = CommandResult.FailResult("本次" + featureDisplayName + "未应用，原文档已恢复。");
		commandResult2.OutcomeKind = UserOutcomeKind.FailedButRecovered;
		return commandResult2;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static CommandResult MapSuccessWithWarnings(string successMessage, IReadOnlyList<VerificationFinding> warnings)
	{
		if (warnings != null && warnings.Count != 0)
		{
			for (int i = 0; i < warnings.Count; i++)
			{
				if (warnings[i] == null || warnings[i].Severity != VerificationSeverity.Warning)
				{
					throw new InvalidOperationException("成功结果只能携带质量警告，严重验证问题不得降级展示。");
				}
			}
			CommandResult commandResult = CommandResult.WarningResult(successMessage + "\r\n" + DescribeWarnings(warnings));
			commandResult.Warnings = warnings;
			return commandResult;
		}
		return CommandResult.SuccessResult(successMessage);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static string DescribeWarnings(IReadOnlyList<VerificationFinding> warnings)
	{
		if (warnings != null && warnings.Count != 0)
		{
			List<string> list = new List<string>();
			Dictionary<string, int> dictionary = new Dictionary<string, int>(StringComparer.Ordinal);
			for (int i = 0; i < warnings.Count; i++)
			{
				VerificationFinding verificationFinding = warnings[i];
				if (verificationFinding != null)
				{
					string text = verificationFinding.UserMessageKey ?? "warn.unknown";
					if (!dictionary.TryGetValue(text, out var value))
					{
						list.Add(text);
						value = 0;
					}
					dictionary[text] = value + Math.Max(1, verificationFinding.AffectedCount);
				}
			}
			StringBuilder stringBuilder = new StringBuilder();
			stringBuilder.Append("排版结果已保留，有以下提示：");
			for (int j = 0; j < list.Count; j++)
			{
				stringBuilder.Append("\r\n• ").Append(UserInteractionText.ResolveWarning(list[j]));
				int num = dictionary[list[j]];
				if (num > 1)
				{
					stringBuilder.Append("（共 ").Append(num).Append(" 处）");
				}
			}
			return stringBuilder.ToString();
		}
		return string.Empty;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static CommandResult AttachRuleStoreNotice<T>(CommandResult result, RuleLoadResult<T> loadResult)
	{
		if (result == null || !result.Success || loadResult == null)
		{
			return result;
		}
		string code;
		string userMessageKey;
		if (!string.Equals(loadResult.ReasonCode, "rule-store-recovered-default", StringComparison.Ordinal))
		{
			if (loadResult.Status != RuleLoadStatus.InMemoryFallback)
			{
				return result;
			}
			code = "rule-store-in-memory-default:" + SafeStoreId(loadResult.StoreId);
			userMessageKey = "warn.rule-store.memory";
		}
		else
		{
			code = "rule-store-recovered-default:" + SafeStoreId(loadResult.StoreId);
			userMessageKey = "warn.rule-store.recovered";
		}
		VerificationFinding item = new VerificationFinding(code, VerificationSeverity.Warning, "configuration", userMessageKey);
		List<VerificationFinding> list = new List<VerificationFinding>();
		if (result.Warnings != null)
		{
			list.AddRange(result.Warnings);
		}
		list.Add(item);
		string text = UserInteractionText.ResolveWarning(userMessageKey);
		string message = (string.IsNullOrWhiteSpace(result.Message) ? (text + "。") : (result.Message.TrimEnd(Array.Empty<char>()) + "\r\n" + text + "。"));
		return result.WithAdditionalWarnings(message, list);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string SafeStoreId(string storeId)
	{
		if (!string.IsNullOrWhiteSpace(storeId))
		{
			string text = storeId.Trim();
			int num = 0;
			while (true)
			{
				if (num >= text.Length)
				{
					if (text.Length > 64)
					{
						return "unknown";
					}
					return text;
				}
				char c = text[num];
				if ((c < 'a' || c > 'z') && (c < 'A' || c > 'Z') && (c < '0' || c > '9') && c != '-' && c != '_' && c != '.')
				{
					break;
				}
				num++;
			}
			return "unknown";
		}
		return "unknown";
	}
}
