using System;
using System.Reflection;
using Microsoft.Office.Tools.Ribbon;

namespace DocumentRepository.Services.Interop;

/// <summary>
/// 隔离 Office Core PIA 缺失时的窄范围兼容访问。
/// COM 枚举的底层 ABI 为 32 位整数；Ribbon 类型则由已安装的 VSTO 运行时提供。
/// </summary>
internal static class OfficeInteropCompatibility
{
	internal const int MsoFalse = 0;

	internal const int MsoTrue = -1;

	internal const int MsoLineSolid = 1;

	internal const int MsoArrowheadNone = 1;

	private const int RibbonControlSizeLarge = 1;

	internal static object GetRibbonUi(RibbonBase ribbon)
	{
		if (ribbon == null)
		{
			throw new ArgumentNullException(nameof(ribbon));
		}
		PropertyInfo property = typeof(RibbonBase).GetProperty("RibbonUI", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		return property?.GetValue(ribbon, null);
	}

	internal static void InvalidateControl(object ribbonUi, string controlId)
	{
		if (ribbonUi == null || string.IsNullOrWhiteSpace(controlId))
		{
			return;
		}
		PropertyInfo property = typeof(RibbonBase).GetProperty("RibbonUI", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		MethodInfo method = property?.PropertyType.GetMethod("InvalidateControl", new Type[1] { typeof(string) });
		if (method == null)
		{
			throw new MissingMethodException("VSTO RibbonUI.InvalidateControl(string) 不可用。");
		}
		method.Invoke(ribbonUi, new object[1] { controlId });
	}

	internal static void SetLargeControlSize(RibbonControl control)
	{
		if (control == null)
		{
			throw new ArgumentNullException(nameof(control));
		}
		PropertyInfo property = control.GetType().GetProperty("ControlSize", BindingFlags.Instance | BindingFlags.Public);
		if (property == null || !property.CanWrite || !property.PropertyType.IsEnum)
		{
			throw new MissingMemberException(control.GetType().FullName, "ControlSize");
		}
		property.SetValue(control, Enum.ToObject(property.PropertyType, RibbonControlSizeLarge), null);
	}
}
