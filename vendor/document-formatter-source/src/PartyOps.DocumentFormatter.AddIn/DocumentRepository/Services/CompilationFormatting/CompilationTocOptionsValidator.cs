using System;
using System.Runtime.CompilerServices;
using DocumentRepository.Models.CompilationFormatting;

namespace DocumentRepository.Services.CompilationFormatting;

public static class CompilationTocOptionsValidator
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void Validate(CompilationTocOptions toc, string prefix)
	{
		if (toc == null)
		{
			throw new InvalidOperationException(prefix + " 不能为空。");
		}
		if (!string.IsNullOrWhiteSpace(toc.TitleText))
		{
			if (string.IsNullOrWhiteSpace(toc.TitleFontName))
			{
				throw new InvalidOperationException(prefix + ".目录标题字体不能为空。");
			}
			if (!string.IsNullOrWhiteSpace(toc.EntryFontNameFarEast))
			{
				if (!string.IsNullOrWhiteSpace(toc.EntryFontNameAscii))
				{
					EnsureRange(toc.TitleFontSize, 5f, 72f, prefix + ".目录标题字号");
					EnsureRange(toc.TitleSpaceBefore, 0f, 200f, prefix + ".目录标题段前");
					EnsureRange(toc.TitleSpaceAfter, 0f, 200f, prefix + ".目录标题段后");
					EnsureRange(toc.EntryFontSize, 5f, 72f, prefix + ".目录条目字号");
					EnsureRange(toc.EntryLineSpacingPoints, 5f, 200f, prefix + ".目录条目行距");
					if (!Enum.IsDefined(typeof(CompilationTocTitleAlignment), toc.TitleAlignment))
					{
						throw new InvalidOperationException(prefix + ".目录标题对齐方式非法：" + (int)toc.TitleAlignment);
					}
					if (Enum.IsDefined(typeof(CompilationTocLeaderStyle), toc.LeaderStyle))
					{
						if (!Enum.IsDefined(typeof(CompilationExistingTocPolicy), toc.ExistingTocPolicy))
						{
							throw new InvalidOperationException(prefix + ".既有目录处理方式非法：" + (int)toc.ExistingTocPolicy);
						}
						return;
					}
					throw new InvalidOperationException(prefix + ".目录前导符非法：" + (int)toc.LeaderStyle);
				}
				throw new InvalidOperationException(prefix + ".目录条目英文数字字体不能为空。");
			}
			throw new InvalidOperationException(prefix + ".目录条目中文字体不能为空。");
		}
		throw new InvalidOperationException(prefix + ".目录标题文字不能为空。");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void EnsureRange(float value, float min, float max, string name)
	{
		if (value < min || value > max)
		{
			throw new InvalidOperationException(name + " 超出范围 [" + min + ", " + max + "]：" + value);
		}
	}
}
