using System;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using DocumentRepository.Services.Logging;

namespace DocumentRepository;

internal static class NativeLibraryLoader
{
	private static readonly ConcurrentDictionary<string, IntPtr> Loaded = new ConcurrentDictionary<string, IntPtr>(StringComparer.OrdinalIgnoreCase);

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern IntPtr LoadLibrary(string lpFileName);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static bool TryLoadFullPath(string dllPath)
	{
		try
		{
			if (string.IsNullOrWhiteSpace(dllPath))
			{
				return false;
			}
			string fullPath = Path.GetFullPath(dllPath);
			if (!File.Exists(fullPath))
			{
				return false;
			}
			if (Loaded.ContainsKey(fullPath))
			{
				return true;
			}
			IntPtr intPtr = LoadLibrary(fullPath);
			if (!(intPtr == IntPtr.Zero))
			{
				Loaded[fullPath] = intPtr;
				return true;
			}
			LogService.Error("NativeLibraryLoader.LoadLibrary failed: " + fullPath);
			return false;
		}
		catch (Exception ex)
		{
			LogService.Error("NativeLibraryLoader.TryLoadFullPath", ex);
			return false;
		}
	}
}
