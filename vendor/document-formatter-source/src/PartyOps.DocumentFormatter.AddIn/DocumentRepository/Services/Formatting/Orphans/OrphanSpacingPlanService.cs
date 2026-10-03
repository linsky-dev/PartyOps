using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace DocumentRepository.Services.Formatting.Orphans;

public static class OrphanSpacingPlanService
{
	private static readonly float[] AdjustmentPoints = new float[] { 0.300000012f, 0.400000006f, 0.5f, 0.600000024f };

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static IReadOnlyList<float> Build(float originalSpacing)
	{
		if (!float.IsNaN(originalSpacing) && !float.IsInfinity(originalSpacing) && !(Math.Abs(originalSpacing) > 100f))
		{
			List<float> list = new List<float>(AdjustmentPoints.Length);
			float[] adjustmentPoints = AdjustmentPoints;
			foreach (float num in adjustmentPoints)
			{
				list.Add(originalSpacing - num);
			}
			return list;
		}
		throw new InvalidOperationException("孤字修复无法读取一致的原始字间距。");
	}
}
