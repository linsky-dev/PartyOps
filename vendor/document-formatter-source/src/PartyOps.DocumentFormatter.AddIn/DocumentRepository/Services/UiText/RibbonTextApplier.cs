using System.Reflection;
using System.Runtime.CompilerServices;

namespace DocumentRepository.Services.UiText;

public static class RibbonTextApplier
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void Apply(object ribbonControl, string key)
	{
		if (ribbonControl != null)
		{
			UiTextMeta uiTextMeta = UiTextRegistry.Get(key);
			SetStringProperty(ribbonControl, "Label", uiTextMeta.Text);
			SetStringProperty(ribbonControl, "ScreenTip", uiTextMeta.TooltipEnabled ? uiTextMeta.Text : "");
			SetStringProperty(ribbonControl, "SuperTip", uiTextMeta.TooltipEnabled ? uiTextMeta.Tooltip : "");
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void ApplyDynamic(object ribbonControl, string label, string tooltip)
	{
		if (ribbonControl != null)
		{
			SetStringProperty(ribbonControl, "Label", label);
			SetStringProperty(ribbonControl, "ScreenTip", (label ?? "").TrimStart('●', ' '));
			SetStringProperty(ribbonControl, "SuperTip", tooltip);
		}
	}

	private static void SetStringProperty(object target, string propertyName, string value)
	{
		try
		{
			PropertyInfo property = target.GetType().GetProperty(propertyName);
			if (!(property == null) && property.CanWrite && !(property.PropertyType != typeof(string)))
			{
				property.SetValue(target, value ?? "", null);
			}
		}
		catch
		{
		}
	}
}
