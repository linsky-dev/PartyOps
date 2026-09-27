using System.Runtime.CompilerServices;

namespace DocumentRepository;

public static class PageNumberModes
{
	public const string Enabled = "开启";

	public const string Disabled = "关闭";

	public const string HideFirstPage = "首页无页码";

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static string Normalize(string mode, bool legacyEnabled)
	{
		switch (mode)
		{
		default:
			if (!legacyEnabled)
			{
				return "关闭";
			}
			return "开启";
		case "开启":
		case "关闭":
		case "首页无页码":
			return mode;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static bool IsEnabled(string mode, bool legacyEnabled)
	{
		return Normalize(mode, legacyEnabled) != "关闭";
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static bool IsFirstPageHidden(string mode)
	{
		return mode == "首页无页码";
	}
}
