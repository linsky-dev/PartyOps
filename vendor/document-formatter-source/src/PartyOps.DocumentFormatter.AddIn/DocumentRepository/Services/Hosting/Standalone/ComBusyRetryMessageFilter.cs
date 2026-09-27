using System;
using System.Runtime.InteropServices;

namespace DocumentRepository.Services.Hosting.Standalone;

/// <summary>
/// Office/WPS 忙于启动、刷新或显示内部窗口时会暂时拒绝 COM 调用。
/// 在独立版的 STA 线程注册标准 OLE 消息筛选器，让系统在限定时间内安全重试。
/// </summary>
public sealed class ComBusyRetryMessageFilter : IDisposable
{
	private readonly IOleMessageFilter previousFilter;
	private bool disposed;

	private ComBusyRetryMessageFilter(IOleMessageFilter previousFilter)
	{
		this.previousFilter = previousFilter;
	}

	public static ComBusyRetryMessageFilter Register()
	{
		RetryFilter current = new RetryFilter();
		int result = CoRegisterMessageFilter(current, out IOleMessageFilter previous);
		if (result != 0)
		{
			Marshal.ThrowExceptionForHR(result);
		}
		return new ComBusyRetryMessageFilter(previous);
	}

	public void Dispose()
	{
		if (disposed)
		{
			return;
		}
		disposed = true;
		CoRegisterMessageFilter(previousFilter, out _);
	}

	[DllImport("ole32.dll")]
	private static extern int CoRegisterMessageFilter(IOleMessageFilter newFilter, out IOleMessageFilter oldFilter);

	[ComImport]
	[Guid("00000016-0000-0000-C000-000000000046")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	private interface IOleMessageFilter
	{
		[PreserveSig]
		int HandleInComingCall(int callType, IntPtr taskCaller, int tickCount, IntPtr interfaceInfo);

		[PreserveSig]
		int RetryRejectedCall(IntPtr taskCallee, int tickCount, int rejectType);

		[PreserveSig]
		int MessagePending(IntPtr taskCallee, int tickCount, int pendingType);
	}

	private sealed class RetryFilter : IOleMessageFilter
	{
		public int HandleInComingCall(int callType, IntPtr taskCaller, int tickCount, IntPtr interfaceInfo)
		{
			return 0;
		}

		public int RetryRejectedCall(IntPtr taskCallee, int tickCount, int rejectType)
		{
			// 1=SERVERCALL_REJECTED，2=SERVERCALL_RETRYLATER；超过 15 秒后交由上层给出明确错误。
			return (rejectType == 1 || rejectType == 2) && tickCount < 15000 ? 250 : -1;
		}

		public int MessagePending(IntPtr taskCallee, int tickCount, int pendingType)
		{
			return 2;
		}
	}
}
