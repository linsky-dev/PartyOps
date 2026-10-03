using System;
using System.Runtime.CompilerServices;
using DocumentRepository.Models;

namespace DocumentRepository.Services.Formatting;

internal sealed class FormatTextStyleDefinition
{
	public FormatConfig Config { get; }

	public TextStyle Body { get; }

	public TextStyle MainTitle { get; }

	public TextStyle Level1 { get; }

	public TextStyle Level2 { get; }

	public TextStyle Level3 { get; }

	public string BodyEnglishFontName { get; }

	public float MainTitleSpaceAfter { get; }

	[MethodImpl(MethodImplOptions.NoInlining)]
	public FormatTextStyleDefinition(FormatConfig config)
	{
		if (config != null)
		{
			Config = config;
			Body = Config.Body ?? throw new FormatException("正文参数缺失。");
			MainTitle = Config.MainTitle ?? throw new FormatException("主标题参数缺失。");
			Level1 = Config.Level1 ?? throw new FormatException("一级标题参数缺失。");
			Level2 = Config.Level2 ?? throw new FormatException("二级标题参数缺失。");
			Level3 = Config.Level3 ?? throw new FormatException("三级标题参数缺失。");
			BodyEnglishFontName = EnglishNumberFontScopePolicy.ResolveStyleFont(Config, ElementType.Body, Body.FontName);
			MainTitleSpaceAfter = Config.MainTitle.SpaceAfter;
			return;
		}
		throw new ArgumentNullException("config");
	}

	public TextStyle GetTextStyle(ElementType type)
	{
		return type switch
		{
			ElementType.Level2Title => Level2, 
			ElementType.MainTitle => MainTitle, 
			ElementType.Level3Title => Level3, 
			ElementType.Level1Title => Level1, 
			_ => Body, 
		};
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public string GetEnglishFontName(ElementType type, TextStyle style)
	{
		if (style == null)
		{
			throw new ArgumentNullException("style");
		}
		return EnglishNumberFontScopePolicy.ResolveStyleFont(Config, type, style.FontName);
	}
}
