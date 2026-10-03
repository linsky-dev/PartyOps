using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using DocumentRepository.Services.Logging;

namespace DocumentRepository.Services.Hosting;

public static class HostOptionalComProperty
{
	private const uint DispEUnknownName = 2147614726u;

	private const uint DispEMemberNotFound = 2147614723u;

	private static readonly object SyncRoot = new object();

	private static readonly HashSet<string> Unsupported = new HashSet<string>(StringComparer.Ordinal);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static bool TrySet(object target, string propertyName, object value, string capabilityName)
	{
		if (target == null)
		{
			throw new ArgumentNullException("target");
		}
		if (string.IsNullOrWhiteSpace(propertyName))
		{
			throw new ArgumentException("COM 属性名为空。", "propertyName");
		}
		string text = target.GetType().FullName + "." + propertyName;
		lock (SyncRoot)
		{
			if (Unsupported.Contains(text))
			{
				return false;
			}
		}
		try
		{
			target.GetType().InvokeMember(propertyName, BindingFlags.SetProperty, null, target, new object[1] { value });
			return true;
		}
		catch (TargetInvocationException ex) when (IsOptionalMemberUnavailable(ex.InnerException))
		{
			MarkUnsupported(text, capabilityName, ex.InnerException);
			return false;
		}
		catch (Exception exception) when (IsOptionalMemberUnavailable(exception))
		{
			MarkUnsupported(text, capabilityName, exception);
			return false;
		}
	}

	private static bool IsOptionalMemberUnavailable(Exception exception)
	{
		if (!(exception is MissingMemberException) && !(exception is MissingMethodException))
		{
			if (!(exception is COMException ex))
			{
				return false;
			}
			uint errorCode = (uint)ex.ErrorCode;
			if (errorCode != 2147614726u)
			{
				return errorCode == 2147614723u;
			}
			return true;
		}
		return true;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void MarkUnsupported(string key, string capabilityName, Exception exception)
	{
		bool flag;
		lock (SyncRoot)
		{
			flag = Unsupported.Add(key);
		}
		if (flag)
		{
			LogService.Warn("HostOptionalComProperty.Unsupported capability=" + (capabilityName ?? key) + " member=" + key, exception);
		}
	}
}
