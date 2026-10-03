using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using DocumentRepository.Models;
using DocumentRepository.Models.Rename;

namespace DocumentRepository.Services.Rename;

public static class RenamePlanBuilder
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static RenameExecutionPlan Build(RenameAnalysisResult analysis, RenameRule rule)
	{
		if (analysis == null)
		{
			throw RenameOperationException.Create(RenameFailureReasonCode.UnexpectedFailure, RenameFailureStage.Planning, new ArgumentNullException("analysis"));
		}
		if (analysis.Info != null)
		{
			ThrowIfConfiguredOutputDirectoryIsMissing(rule);
			if (!RenameRuleService.ValidateRule(rule, out var error))
			{
				throw RenameOperationException.Create(RenameFailureReasonCode.RuleInvalid, RenameFailureStage.Planning, new InvalidOperationException(error));
			}
			RenameRuleManager.EnsureRuleParts(rule);
			bool needsMainTitle = RuleUsesPart(rule, "mainTitle");
			bool flag = RuleUsesPart(rule, "docNumber");
			bool needsSubtitle = RuleUsesPart(rule, "subtitle");
			RenameInfo info = analysis.Info;
			string documentNumber = (flag ? info.DocumentNumber : null);
			RenameFailureReasonCode? renameFailureReasonCode = RenameRequirementPolicy.FindMissingContent(needsMainTitle, info.MainTitle, flag, documentNumber, needsSubtitle, info.Subtitle);
			if (renameFailureReasonCode.HasValue)
			{
				throw RenameOperationException.Create(renameFailureReasonCode.Value, RenameFailureStage.Planning);
			}
			RenameRuleManager.RotateWordSelection rotateWordSelection = RenameRuleManager.ResolveRotateWord(rule);
			string rotateWord = ((rotateWordSelection != null) ? rotateWordSelection.Word : "");
			bool flag2 = string.Equals(rule.RenameMode, "copy", StringComparison.OrdinalIgnoreCase);
			string text = (flag2 ? ResolveCopyDirectory(analysis.OriginalDirectory, rule) : analysis.OriginalDirectory);
			if (flag2 && string.IsNullOrWhiteSpace(text))
			{
				throw RenameOperationException.Create(RenameFailureReasonCode.OutputDirectoryMissing, RenameFailureStage.OutputPreparation);
			}
			info.DocumentNumber = documentNumber;
			string text2 = BuildTargetPath(analysis.OriginalPath, text, rule, info, !flag2, rotateWord);
			if (string.IsNullOrWhiteSpace(text2))
			{
				throw RenameOperationException.Create(RenameFailureReasonCode.FilenameInvalid, RenameFailureStage.OutputPreparation);
			}
			return new RenameExecutionPlan
			{
				OriginalPath = analysis.OriginalPath,
				TargetPath = text2,
				CopyMode = flag2,
				Rule = rule,
				Info = info,
				RotateSelection = rotateWordSelection
			};
		}
		throw RenameOperationException.Create(RenameFailureReasonCode.UnexpectedFailure, RenameFailureStage.Planning);
	}

	private static bool RuleUsesPart(RenameRule rule, string partType)
	{
		if (rule != null && rule.Parts != null)
		{
			return rule.Parts.Any((RenameRulePart p) => string.Equals((p.Type ?? "").Trim(), partType, StringComparison.OrdinalIgnoreCase));
		}
		return false;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ThrowIfConfiguredOutputDirectoryIsMissing(RenameRule rule)
	{
		if (rule == null || !string.Equals(rule.RenameMode, "copy", StringComparison.OrdinalIgnoreCase) || !string.Equals(rule.SavePathMode, "custom", StringComparison.OrdinalIgnoreCase) || (!string.IsNullOrWhiteSpace(rule.CustomSaveDirectory) && Directory.Exists(rule.CustomSaveDirectory)))
		{
			return;
		}
		throw RenameOperationException.Create(RenameFailureReasonCode.OutputDirectoryMissing, RenameFailureStage.OutputPreparation);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string ResolveCopyDirectory(string originalDir, RenameRule rule)
	{
		if (rule != null && string.Equals(rule.SavePathMode, "custom", StringComparison.OrdinalIgnoreCase))
		{
			if (!string.IsNullOrWhiteSpace(rule.CustomSaveDirectory))
			{
				if (Directory.Exists(rule.CustomSaveDirectory))
				{
					return rule.CustomSaveDirectory;
				}
				throw RenameOperationException.Create(RenameFailureReasonCode.OutputDirectoryMissing, RenameFailureStage.OutputPreparation);
			}
			throw RenameOperationException.Create(RenameFailureReasonCode.OutputDirectoryMissing, RenameFailureStage.OutputPreparation);
		}
		return originalDir;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string BuildTargetPath(string originalPath, string outputDir, RenameRule rule, RenameInfo info, bool allowOriginalPath, string rotateWord)
	{
		string text = Path.GetExtension(originalPath);
		if (string.IsNullOrWhiteSpace(text))
		{
			text = ".docx";
		}
		List<string> list = new List<string>();
		foreach (RenameRulePart part in rule.Parts)
		{
			switch ((part.Type ?? "").Trim().ToLowerInvariant())
			{
			case "docnumber":
				list.Add((info.DocumentNumber ?? "").Trim());
				break;
			case "rotate":
				list.Add((rotateWord ?? "").Trim());
				break;
			case "maintitle":
				list.Add((info.MainTitle ?? "").Trim());
				break;
			case "date":
				list.Add(DateTime.Now.ToString(rule.DateFormat));
				break;
			case "subtitle":
				list.Add((info.Subtitle ?? "").Trim());
				break;
			case "custom":
				list.Add((part.Text ?? "").Trim());
				break;
			}
		}
		string text2 = RenameRuleService.SanitizeFileName(string.Join("", list.ToArray()));
		if (text2.Length > 120)
		{
			text2 = text2.Substring(0, 120).Trim();
		}
		if (string.IsNullOrWhiteSpace(text2))
		{
			throw RenameOperationException.Create(RenameFailureReasonCode.FilenameInvalid, RenameFailureStage.OutputPreparation);
		}
		try
		{
			return GetUniqueFilePath(outputDir, text2, text, allowOriginalPath ? originalPath : null);
		}
		catch (Exception error)
		{
			throw RenameFailureClassifier.ClassifyOutputFailure(error, RenameFailureStage.OutputPreparation);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string GetUniqueFilePath(string directory, string baseName, string extension, string originalPath)
	{
		string text = Path.Combine(directory, baseName + extension);
		if (originalPath == null || !string.Equals(text, originalPath, StringComparison.OrdinalIgnoreCase))
		{
			if (!File.Exists(text))
			{
				return text;
			}
			for (int i = 1; i < 1000; i++)
			{
				text = Path.Combine(directory, $"{baseName}({i}){extension}");
				if (originalPath == null || !string.Equals(text, originalPath, StringComparison.OrdinalIgnoreCase))
				{
					if (!File.Exists(text))
					{
						return text;
					}
					continue;
				}
				return text;
			}
			return Path.Combine(directory, string.Format("{0}_{1}{2}", baseName, DateTime.Now.ToString("HHmmss"), extension));
		}
		return text;
	}
}
