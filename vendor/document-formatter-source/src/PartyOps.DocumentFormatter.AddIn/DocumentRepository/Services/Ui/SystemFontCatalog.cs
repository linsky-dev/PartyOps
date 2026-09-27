using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;

namespace DocumentRepository.Services.Ui;

public static class SystemFontCatalog
{
	public const string Separator = "──────────────";

	public static readonly string[] PreferredFonts = new string[38]
	{
		"方正小标宋简体", "方正小标宋_GBK", "小标宋_GBK", "方正粗宋简体", "华文宋体", "华文中宋", "思源宋体 Bold", "思源宋体 Regular", "方正书宋简体", "黑体",
		"方正黑体简体", "方正黑体_GBK", "华文黑体", "微软雅黑", "微软正黑体", "华文细黑", "思源黑体 Regular", "宋体", "新宋体", "新细明体",
		"仿宋", "仿宋_GB2312", "方正仿宋简体", "方正仿宋_GBK", "华文仿宋", "楷体", "楷体_GB2312", "方正楷体简体", "方正楷体_GBK", "华文楷体",
		"方正舒体", "华文彩云", "华文琥珀", "华文隶书", "华文行楷", "华文新魏", "幼圆", "Times New Roman"
	};

	private static readonly object SyncRoot = new object();

	private static string[] installedAdditionalFonts;

	public static string[] GetInstalledAdditionalFonts()
	{
		if (installedAdditionalFonts != null)
		{
			return installedAdditionalFonts;
		}
		lock (SyncRoot)
		{
			if (installedAdditionalFonts != null)
			{
				return installedAdditionalFonts;
			}
			HashSet<string> hashSet = new HashSet<string>(PreferredFonts, StringComparer.OrdinalIgnoreCase);
			List<string> list = new List<string>();
			InstalledFontCollection val = new InstalledFontCollection();
			try
			{
				FontFamily[] families = ((FontCollection)val).Families;
				foreach (FontFamily val2 in families)
				{
					if (hashSet.Add(val2.Name))
					{
						list.Add(val2.Name);
					}
				}
			}
			finally
			{
				((IDisposable)val)?.Dispose();
			}
			list.Sort(StringComparer.CurrentCultureIgnoreCase);
			installedAdditionalFonts = list.ToArray();
			return installedAdditionalFonts;
		}
	}
}
