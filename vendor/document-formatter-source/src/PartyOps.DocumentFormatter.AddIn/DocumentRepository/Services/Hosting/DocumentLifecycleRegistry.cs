using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using DocumentRepository.Services.Hosting.Standalone;
using Microsoft.Office.Interop.Word;

namespace DocumentRepository.Services.Hosting;

public static class DocumentLifecycleRegistry
{
	private static readonly Dictionary<long, string> Ids = new Dictionary<long, string>();

	private static readonly object SyncRoot = new object();

	public static int Count
	{
		get
		{
			lock (SyncRoot)
			{
				return Ids.Count;
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static string GetOrCreate(Document document)
	{
		if (document == null)
		{
			throw new ArgumentNullException("document");
		}
		long comIdentity = GetComIdentity(document);
		lock (SyncRoot)
		{
			if (!Ids.TryGetValue(comIdentity, out var value))
			{
				value = Guid.NewGuid().ToString("N");
				Ids.Add(comIdentity, value);
			}
			return value;
		}
	}

	public static bool TryGet(Document document, out string lifecycleId)
	{
		lifecycleId = null;
		if (document == null)
		{
			return false;
		}
		long comIdentity = GetComIdentity(document);
		lock (SyncRoot)
		{
			return Ids.TryGetValue(comIdentity, out lifecycleId);
		}
	}

	public static bool Release(Document document)
	{
		if (document == null)
		{
			return false;
		}
		long comIdentity = GetComIdentity(document);
		lock (SyncRoot)
		{
			return Ids.Remove(comIdentity);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static string Rotate(Document document)
	{
		if (document == null)
		{
			throw new ArgumentNullException("document");
		}
		long comIdentity = GetComIdentity(document);
		lock (SyncRoot)
		{
			string text = Guid.NewGuid().ToString("N");
			Ids[comIdentity] = text;
			return text;
		}
	}

	public static void Clear()
	{
		lock (SyncRoot)
		{
			Ids.Clear();
		}
	}

	private static long GetComIdentity(object value)
	{
		if (PortableOfficeHostRuntime.TryGetIdentity(value, out long identity))
		{
			return identity;
		}
		IntPtr intPtr = IntPtr.Zero;
		try
		{
			intPtr = Marshal.GetIUnknownForObject(value);
			return intPtr.ToInt64();
		}
		finally
		{
			if (intPtr != IntPtr.Zero)
			{
				Marshal.Release(intPtr);
			}
		}
	}
}
