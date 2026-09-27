using System;

namespace DocumentRepository.Services.Hosting.Standalone;

/// <summary>
/// 为不具备 Windows COM 注册表的平台注入 WPS 原生 RPC 应用对象。
/// 这里只提供宿主激活和生命周期接缝，所有排版规则仍由原
/// StandaloneBatchProcessor 及其既有服务执行。
/// </summary>
public static class PortableOfficeHostRuntime
{
	private static readonly object SyncRoot = new object();

	private static Registration registration;

	public static bool IsRegistered
	{
		get
		{
			lock (SyncRoot)
			{
				return registration != null;
			}
		}
	}

	public static void Register(
		Func<object> activate,
		Func<object, bool> owns,
		Func<object, long> identity,
		Action<object> quit,
		Action<object> release)
	{
		if (activate == null)
		{
			throw new ArgumentNullException(nameof(activate));
		}
		if (owns == null)
		{
			throw new ArgumentNullException(nameof(owns));
		}
		if (identity == null)
		{
			throw new ArgumentNullException(nameof(identity));
		}
		if (quit == null)
		{
			throw new ArgumentNullException(nameof(quit));
		}
		if (release == null)
		{
			throw new ArgumentNullException(nameof(release));
		}
		lock (SyncRoot)
		{
			if (registration != null)
			{
				throw new InvalidOperationException("跨平台 WPS 宿主已注册，拒绝在同一进程中替换实现。");
			}
			registration = new Registration(activate, owns, identity, quit, release);
		}
	}

	internal static bool TryActivate(out object application, out string failure)
	{
		Registration current = GetRegistration();
		if (current == null)
		{
			application = null;
			failure = "跨平台 WPS 宿主未注册";
			return false;
		}
		try
		{
			application = current.Activate();
			if (application == null)
			{
				failure = "跨平台 WPS RPC 未返回应用对象";
				return false;
			}
			failure = null;
			return true;
		}
		catch (Exception ex)
		{
			application = null;
			failure = ex.Message;
			return false;
		}
	}

	internal static bool TryQuit(object value)
	{
		Registration current = GetRegistration();
		if (current == null || value == null || !current.Owns(value))
		{
			return false;
		}
		current.Quit(value);
		return true;
	}

	internal static bool TryRelease(object value)
	{
		Registration current = GetRegistration();
		if (current == null || value == null || !current.Owns(value))
		{
			return false;
		}
		current.Release(value);
		return true;
	}

	internal static bool TryGetIdentity(object value, out long identity)
	{
		Registration current = GetRegistration();
		if (current == null || value == null || !current.Owns(value))
		{
			identity = 0L;
			return false;
		}
		identity = current.Identity(value);
		return identity != 0L;
	}

	private static Registration GetRegistration()
	{
		lock (SyncRoot)
		{
			return registration;
		}
	}

	private sealed class Registration
	{
		internal Registration(
			Func<object> activate,
			Func<object, bool> owns,
			Func<object, long> identity,
			Action<object> quit,
			Action<object> release)
		{
			Activate = activate;
			Owns = owns;
			Identity = identity;
			Quit = quit;
			Release = release;
		}

		internal Func<object> Activate { get; }

		internal Func<object, bool> Owns { get; }

		internal Func<object, long> Identity { get; }

		internal Action<object> Quit { get; }

		internal Action<object> Release { get; }
	}
}
