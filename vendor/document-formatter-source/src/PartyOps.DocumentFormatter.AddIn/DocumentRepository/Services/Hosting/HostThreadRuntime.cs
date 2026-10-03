using System;
using System.Runtime.CompilerServices;
using System.Threading;
using DocumentRepository.Services.Logging;

namespace DocumentRepository.Services.Hosting;

public static class HostThreadRuntime
{
	private static readonly object SyncRoot = new object();

	private static int hostThreadId;

	private static ApartmentState hostApartmentState = ApartmentState.Unknown;

	private static string initializationSource;

	public static bool IsInitialized => Volatile.Read(ref hostThreadId) != 0;

	public static int HostThreadId => Volatile.Read(ref hostThreadId);

	public static bool IsHostThread
	{
		get
		{
			int num = Volatile.Read(ref hostThreadId);
			if (num != 0)
			{
				return Thread.CurrentThread.ManagedThreadId == num;
			}
			return false;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void Initialize(string source)
	{
		int managedThreadId = Thread.CurrentThread.ManagedThreadId;
		ApartmentState apartmentState = Thread.CurrentThread.GetApartmentState();
		lock (SyncRoot)
		{
			if (hostThreadId == 0)
			{
				hostThreadId = managedThreadId;
				hostApartmentState = apartmentState;
				initializationSource = (string.IsNullOrWhiteSpace(source) ? "unspecified" : source);
				LogService.Info("HOST_THREAD initialized threadId=" + hostThreadId + ", apartment=" + hostApartmentState.ToString() + ", source=" + initializationSource);
			}
			else if (hostThreadId != managedThreadId)
			{
				ThrowThreadViolation(source, managedThreadId, apartmentState);
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void AssertAccess(string operation)
	{
		int num = Volatile.Read(ref hostThreadId);
		int managedThreadId = Thread.CurrentThread.ManagedThreadId;
		ApartmentState apartmentState = Thread.CurrentThread.GetApartmentState();
		if (num == 0)
		{
			throw new InvalidOperationException("Word/WPS host thread has not been initialized. operation=" + Normalize(operation) + ", currentThreadId=" + managedThreadId);
		}
		if (num != managedThreadId)
		{
			ThrowThreadViolation(operation, managedThreadId, apartmentState);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ThrowThreadViolation(string operation, int currentThreadId, ApartmentState currentApartmentState)
	{
		string text = "Word/WPS COM access rejected outside the host thread. operation=" + Normalize(operation) + ", expectedThreadId=" + hostThreadId + ", currentThreadId=" + currentThreadId + ", hostApartment=" + hostApartmentState.ToString() + ", currentApartment=" + currentApartmentState.ToString() + ", initializedBy=" + Normalize(initializationSource);
		LogService.Error("HOST_THREAD violation " + text);
		throw new InvalidOperationException(text);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string Normalize(string value)
	{
		if (!string.IsNullOrWhiteSpace(value))
		{
			return value;
		}
		return "unspecified";
	}
}
