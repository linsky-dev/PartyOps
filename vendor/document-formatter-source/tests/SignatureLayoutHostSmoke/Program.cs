using System;
using System.Collections.Generic;
using DocumentRepository.Models.Signatures;
using DocumentRepository.Services.Formatting.Signatures;
using DocumentRepository.Services.Hosting;

namespace SignatureLayoutHostSmoke;

internal static class Program
{
    private static int failures;
    private static int assertions;

    private static int Main()
    {
        Run("网格落款布局", VerifyGridLayout);
        Run("非法数值降级", VerifyUnsafeValueFallback);
        Run("超出版心降级", VerifyOverflowFallback);
        Run("负缩进钳制", VerifyNegativeIndentClamp);
        Console.WriteLine("落款布局冒烟：{0} 项断言，{1} 项失败。", assertions, failures);
        return failures == 0 ? 0 : 1;
    }

    private static void VerifyGridLayout()
    {
        SignatureLayoutPlan plan = SignatureLayoutPlanner.Plan(CreateRequest(500f, 100f, 150f, 80f, withSeal: false));
        AssertEqual(SignaturePlanDegradation.None, plan.Degradation, "正常输入不应降级");
        AssertNear(10f, plan.UnitPt, "网格单位错误");
        AssertNear(90f, plan.SignatureLines[0].RightIndentPt, "短落款缩进错误");
        AssertNear(20f, plan.SignatureLines[1].RightIndentPt, "最长落款缩进错误");
        AssertNear(70f, plan.Date.RightIndentPt, "日期缩进错误");
    }

    private static void VerifyUnsafeValueFallback()
    {
        SignatureLayoutRequest request = CreateRequest(500f, float.NaN, 150f, 80f, withSeal: false);
        SignatureLayoutPlan plan = SignatureLayoutPlanner.Plan(request);
        AssertTrue((plan.Degradation & SignaturePlanDegradation.UnsafeValueRejected) != 0, "非法宽度必须安全降级");
        AssertNear(0f, plan.SignatureLines[0].RightIndentPt, "非法输入缩进应归零");
    }

    private static void VerifyOverflowFallback()
    {
        SignatureLayoutPlan plan = SignatureLayoutPlanner.Plan(CreateRequest(500f, 600f, 150f, 80f, withSeal: false));
        AssertTrue((plan.Degradation & SignaturePlanDegradation.ExceedsContentWidth) != 0, "超宽落款必须标记降级");
        AssertNear(0f, plan.SignatureLines[0].RightIndentPt, "超宽落款缩进应归零");
    }

    private static void VerifyNegativeIndentClamp()
    {
        SignatureLayoutPlan plan = SignatureLayoutPlanner.Plan(CreateRequest(500f, 480f, 470f, 10f, withSeal: true));
        AssertTrue((plan.Degradation & SignaturePlanDegradation.NegativeIndentClamped) != 0, "负缩进必须钳制");
        AssertNear(0f, plan.SignatureLines[0].RightIndentPt, "钳制后的缩进应为零");
    }

    private static SignatureLayoutRequest CreateRequest(float contentWidth, float firstWidth, float secondWidth, float dateWidth, bool withSeal)
    {
        return new SignatureLayoutRequest
        {
            Host = DocumentHostKind.MicrosoftWord,
            GridEnabled = true,
            CharsPerLine = 50,
            ContentWidthPt = contentWidth,
            FontSizePt = 16f,
            WithSeal = withSeal,
            SignatureLines = new List<SignatureLayoutLineInput>
            {
                Line(firstWidth),
                Line(secondWidth)
            },
            Date = Line(dateWidth)
        };
    }

    private static SignatureLayoutLineInput Line(float width)
    {
        return new SignatureLayoutLineInput
        {
            WidthPt = width,
            Source = SignatureWidthSource.StaticTable,
            Morphology = SignatureTextMorphology.PureCjk
        };
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

    private static void AssertEqual<T>(T expected, T actual, string message)
    {
        assertions++;
        if (!Equals(expected, actual)) throw new InvalidOperationException(message);
    }

    private static void AssertNear(float expected, float actual, string message)
    {
        assertions++;
        if (Math.Abs(expected - actual) > 0.01f)
        {
            throw new InvalidOperationException($"{message}；期望={expected}，实际={actual}");
        }
    }
}
