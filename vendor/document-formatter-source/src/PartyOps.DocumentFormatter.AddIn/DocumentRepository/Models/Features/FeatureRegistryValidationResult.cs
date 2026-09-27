using System.Collections.Generic;
using System.Linq;

namespace DocumentRepository.Models.Features;

public class FeatureRegistryValidationResult
{
	private readonly List<string> _errors = new List<string>();

	public bool Success => !_errors.Any();

	public IReadOnlyList<string> Errors => _errors.AsReadOnly();

	public void AddError(string message)
	{
		if (!string.IsNullOrWhiteSpace(message))
		{
			_errors.Add(message);
		}
	}
}
