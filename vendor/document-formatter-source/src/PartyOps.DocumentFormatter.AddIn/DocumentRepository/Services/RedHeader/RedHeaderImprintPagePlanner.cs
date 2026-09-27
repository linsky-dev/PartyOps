using System;
using System.Runtime.CompilerServices;
using DocumentRepository.Models.RedHeader;

namespace DocumentRepository.Services.RedHeader;

public static class RedHeaderImprintPagePlanner
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ResolveAdvancePages(int currentPage, float currentY, RedHeaderLayoutPlan plan)
	{
		if (plan == null)
		{
			throw new ArgumentNullException("plan");
		}
		plan.EnsureSealed();
		if (!plan.ImprintEnabled)
		{
			return 0;
		}
		if (currentPage > 0)
		{
			if (float.IsNaN(currentY) || float.IsInfinity(currentY) || !(currentY >= 0f))
			{
				throw new ArgumentOutOfRangeException("currentY", "版记分页规划收到无效纵坐标。");
			}
			bool flag = currentY <= plan.ImprintTargetY;
			if (!plan.TemplateForExecution.ImprintOnEvenPage)
			{
				return (!flag) ? 1 : 0;
			}
			if (currentPage % 2 == 0)
			{
				if (!flag)
				{
					return 2;
				}
				return 0;
			}
			return 1;
		}
		throw new ArgumentOutOfRangeException("currentPage", "版记分页规划收到无效页码。");
	}
}
