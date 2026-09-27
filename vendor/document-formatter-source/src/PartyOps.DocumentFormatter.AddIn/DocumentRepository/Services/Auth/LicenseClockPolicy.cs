using System;

namespace DocumentRepository.Services.Auth;

internal static class LicenseClockPolicy
{
	internal static readonly TimeSpan DefaultRollbackTolerance = TimeSpan.FromMinutes(10.0);

	internal static bool IsRollback(DateTime currentLocalTime, DateTime? highWatermarkLocalTime, TimeSpan tolerance)
	{
		if (!highWatermarkLocalTime.HasValue)
		{
			return false;
		}
		if (tolerance < TimeSpan.Zero)
		{
			tolerance = TimeSpan.Zero;
		}
		return currentLocalTime < highWatermarkLocalTime.Value.Subtract(tolerance);
	}

	internal static DateTime SelectHighWatermark(DateTime currentLocalTime, DateTime? existingHighWatermarkLocalTime)
	{
		if (!existingHighWatermarkLocalTime.HasValue)
		{
			return currentLocalTime;
		}
		if (!(currentLocalTime > existingHighWatermarkLocalTime.Value))
		{
			return existingHighWatermarkLocalTime.Value;
		}
		return currentLocalTime;
	}
}
