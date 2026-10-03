using System.Collections.Generic;

namespace DocumentRepository.Models.Tasks;

public sealed class TaskCleanupResult
{
	public List<string> Removed { get; private set; } = new List<string>();

	public List<string> Retained { get; private set; } = new List<string>();

	public List<string> Errors { get; private set; } = new List<string>();

	public bool Success => Errors.Count == 0;
}
