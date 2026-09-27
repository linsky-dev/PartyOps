using System;
using DocumentRepository.Models.Protection;
using DocumentRepository.Services.Detection;
using DocumentRepository.Services.Features;
using DocumentRepository.Services.PipelineAudit;
using DocumentRepository.Services.Rules;

namespace RecognitionRegressionTests;

internal static class Program
{
    private static int failures;
    private static int assertions;

    private static int Main()
    {
        Run("功能注册表", () => AssertTrue(FeatureRegistry.Validate().Success, "功能注册表应有效"));
        Run("功能冒烟目录", () =>
        {
            var result = FeatureSmokeTestCatalog.Validate();
            AssertTrue(result.Success, "冒烟目录应覆盖全部功能：" + string.Join("；", result.Errors));
        });
        Run("流水线职责目录", () =>
        {
            var result = PipelineResponsibilityCatalog.Validate();
            AssertTrue(result.Success, "流水线职责应有效：" + string.Join("；", result.Errors));
        });
        Run("服务边界目录", () =>
        {
            var result = ServiceBoundaryCatalog.Validate();
            AssertTrue(result.Success, "服务边界应有效：" + string.Join("；", result.Errors));
        });
        Run("规则目录", () => AssertTrue(RuleCatalogService.ValidateCatalog().Success, "规则目录应有效"));
		Run("保护证据模式", () =>
		{
			AssertEqual(ProtectionCoverage.NoPreexistingUserContent, SessionProtectionEvidence.Create("task-blank", "format", "document-1", RecoveryProtectionMode.UndoOnlyForBlankDocument, ProtectionCoverage.NoPreexistingUserContent, false).Coverage, "空白文档保护证据错误");
			AssertEqual(ProtectionCoverage.UnsavedInMemoryContent, SessionProtectionEvidence.Create("task-unsaved", "format", "document-2", RecoveryProtectionMode.UndoOnlyForUnsavedDocument, ProtectionCoverage.UnsavedInMemoryContent, false).Coverage, "未保存文档保护证据错误");
			AssertEqual(ProtectionCoverage.ConfirmedUndoOnly, SessionProtectionEvidence.Create("task-confirmed", "format", "document-3", RecoveryProtectionMode.PreferFileCopyAllowConfirmedUndoFallback, ProtectionCoverage.ConfirmedUndoOnly, true).Coverage, "确认降级保护证据错误");
			AssertThrows<ArgumentOutOfRangeException>(() => SessionProtectionEvidence.Create("task-invalid", "format", "document-4", (RecoveryProtectionMode)int.MaxValue, ProtectionCoverage.ConfirmedUndoOnly, true), "未知保护模式应被拒绝");
		});

        Run("数字日期识别", () => AssertTrue(SignatureDetector.IsDateLine("2026年8月28日"), "数字日期未识别"));
        Run("中文日期识别", () => AssertTrue(SignatureDetector.IsDateLine("二〇二六年八月二十八日"), "中文日期未识别"));
        Run("非日期拒绝", () => AssertFalse(SignatureDetector.IsDateLine("今天发布"), "普通文本不应识别为日期"));
        Run("机关落款识别", () => AssertTrue(SignatureDetector.IsSignatureLine("贵州省人民政府"), "机关名称应识别为落款"));
        Run("标点落款拒绝", () => AssertFalse(SignatureDetector.IsSignatureLine("贵州省人民政府。"), "含句号文本不应识别为落款"));
        Run("机构后缀识别", () => AssertTrue(SignatureDetector.HasOrgSuffix("贵州省人民政府办公厅"), "办公厅后缀未识别"));
        Run("动作词后缀拒绝", () => AssertFalse(SignatureDetector.HasOrgSuffix("即日起施行"), "施行不应识别为机构"));

        Run("附件单独标记", () => AssertTrue(AttachmentDetector.IsAttachmentListMarkerAloneText("附件："), "附件标记未识别"));
        Run("附件首项", () => AssertTrue(AttachmentDetector.IsAttachmentListFirstText("附件：1. 工作方案"), "附件首项未识别"));
        Run("附件列表项", () => AssertTrue(AttachmentDetector.IsAttachmentListItemText("2、责任清单"), "附件列表项未识别"));
        Run("主标题候选", () => AssertTrue(MainTitleCandidateDetector.IsMainTitleCandidate("关于进一步规范公文排版工作的通知"), "主标题候选未识别"));
        Run("副标题候选", () => AssertTrue(MainTitleCandidateDetector.IsSubTitleCandidate("工作报告——在全体会议上的讲话"), "副标题候选未识别"));
        Run("括号副标题", () => AssertTrue(MainTitleCandidateDetector.IsParenthesizedSubTitleCandidate("（征求意见稿）"), "括号副标题未识别"));
        Run("标题长度上限", () => AssertFalse(MainTitleCandidateDetector.IsMainTitleCandidate(new string('政', 71)), "超长标题应拒绝"));
        Run("标题正文边界", () => AssertEqual(4, MixedContentDetector.FindTitleBodyBoundary("一、标题。正文内容"), "边界位置错误"));
        Run("无边界文本", () => AssertEqual(-1, MixedContentDetector.FindTitleBodyBoundary("没有终止标点"), "无边界文本应返回 -1"));
        Run("标题文本规范化", () => AssertEqual("示例标题", TitleDetector.NormalizeText("  示例标题\r\a "), "标题清洗结果错误"));

        Console.WriteLine("识别回归：{0} 项断言，{1} 项失败。", assertions, failures);
        return failures == 0 ? 0 : 1;
    }

    private static void Run(string name, Action test)
    {
        try
        {
            test();
            Console.WriteLine("[通过] " + name);
        }
        catch (Exception exception)
        {
            failures++;
            Console.Error.WriteLine("[失败] {0}：{1}", name, exception.Message);
        }
    }

    private static void AssertTrue(bool value, string message)
    {
        assertions++;
        if (!value) throw new InvalidOperationException(message);
    }

    private static void AssertFalse(bool value, string message) => AssertTrue(!value, message);

    private static void AssertEqual<T>(T expected, T actual, string message)
    {
        assertions++;
        if (!Equals(expected, actual))
        {
            throw new InvalidOperationException($"{message}；期望={expected}，实际={actual}");
        }
    }

	private static void AssertThrows<TException>(Action action, string message) where TException : Exception
	{
		assertions++;
		try
		{
			action();
		}
		catch (TException)
		{
			return;
		}
		throw new InvalidOperationException(message);
	}
}
