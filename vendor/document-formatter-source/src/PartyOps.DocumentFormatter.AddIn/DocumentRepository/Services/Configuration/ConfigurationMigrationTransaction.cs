using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using DocumentRepository.Services.FileSafety;

namespace DocumentRepository.Services.Configuration;

internal sealed class ConfigurationMigrationTransaction
{
	private readonly string _backupDirectory;

	private readonly List<ConfigurationFileSnapshot> _snapshots;

	private ConfigurationMigrationTransaction(string backupDirectory, List<ConfigurationFileSnapshot> snapshots)
	{
		_backupDirectory = backupDirectory;
		_snapshots = snapshots;
	}

	public static ConfigurationMigrationTransaction Capture(string backupDirectory, IEnumerable<string> paths)
	{
		List<ConfigurationFileSnapshot> list = new List<ConfigurationFileSnapshot>();
		foreach (string path in paths)
		{
			bool flag = File.Exists(path);
			string text = Path.Combine(backupDirectory, Path.GetFileName(path));
			if (flag)
			{
				File.Copy(path, text, overwrite: false);
			}
			list.Add(new ConfigurationFileSnapshot(path, text, flag));
		}
		return new ConfigurationMigrationTransaction(backupDirectory, list);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public void Rollback()
	{
		List<Exception> list = new List<Exception>();
		foreach (ConfigurationFileSnapshot snapshot in _snapshots)
		{
			try
			{
				if (snapshot.Existed)
				{
					AtomicFileService.WriteFileAtomically(snapshot.Path, delegate(string tempPath)
					{
						File.Copy(snapshot.BackupPath, tempPath, overwrite: false);
					});
				}
				else if (File.Exists(snapshot.Path))
				{
					File.Delete(snapshot.Path);
				}
			}
			catch (Exception innerException)
			{
				list.Add(new IOException("Failed to restore configuration file: " + snapshot.Path, innerException));
			}
		}
		if (File.Exists(ApplicationDataPaths.Migration440Marker))
		{
			try
			{
				File.Delete(ApplicationDataPaths.Migration440Marker);
			}
			catch (Exception item)
			{
				list.Add(item);
			}
		}
		if (list.Count > 0)
		{
			throw new AggregateException("Configuration rollback failed. Backup=" + _backupDirectory, list);
		}
	}
}
