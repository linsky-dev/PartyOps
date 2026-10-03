using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using DocumentRepository.Models.Tasks;
using DocumentRepository.Services.Logging;

namespace DocumentRepository.Services.Tasks;

public sealed class TaskArtifactRegistry : ITaskArtifactRegistry
{
	private sealed class Entry
	{
		public string Path;

		public bool IsDirectory;

		public bool ExistedBefore;
	}

	private readonly List<Entry> entries = new List<Entry>();

	public void RegisterFile(string path, bool existedBefore)
	{
		Register(path, isDirectory: false, existedBefore);
	}

	void ITaskArtifactRegistry.RegisterFile(string path, bool existedBefore)
	{
		this.RegisterFile(path, existedBefore);
	}

	public void RegisterDirectory(string path, bool existedBefore)
	{
		Register(path, isDirectory: true, existedBefore);
	}

	void ITaskArtifactRegistry.RegisterDirectory(string path, bool existedBefore)
	{
		this.RegisterDirectory(path, existedBefore);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public TaskCleanupResult Cleanup()
	{
		TaskCleanupResult taskCleanupResult = new TaskCleanupResult();
		for (int num = entries.Count - 1; num >= 0; num--)
		{
			Entry entry = entries[num];
			if (entry.ExistedBefore)
			{
				taskCleanupResult.Retained.Add(entry.Path);
			}
			else
			{
				try
				{
					if (entry.IsDirectory)
					{
						if (Directory.Exists(entry.Path))
						{
							Directory.Delete(entry.Path, recursive: true);
						}
					}
					else if (File.Exists(entry.Path))
					{
						File.Delete(entry.Path);
					}
					taskCleanupResult.Removed.Add(entry.Path);
				}
				catch (Exception ex)
				{
					taskCleanupResult.Errors.Add(entry.Path + "：" + ex.Message);
					LogService.Warn("TaskArtifactRegistry.Cleanup " + entry.Path, ex);
				}
			}
		}
		return taskCleanupResult;
	}

	TaskCleanupResult ITaskArtifactRegistry.Cleanup()
	{
		return this.Cleanup();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void Register(string path, bool isDirectory, bool existedBefore)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			throw new ArgumentException("Artifact path cannot be empty.", "path");
		}
		entries.Add(new Entry
		{
			Path = Path.GetFullPath(path),
			IsDirectory = isDirectory,
			ExistedBefore = existedBefore
		});
	}
}
