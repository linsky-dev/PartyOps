using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using DocumentRepository.Models.Features;
using DocumentRepository.Services.Features;

namespace DocumentRepository.Services.PipelineAudit;

public static class FeatureSmokeTestCatalog
{
	public static IReadOnlyList<FeatureSmokeTestDescriptor> GetAll()
	{
		return CreateDescriptors().AsReadOnly();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static FeatureRegistryValidationResult Validate()
	{
		FeatureRegistryValidationResult featureRegistryValidationResult = new FeatureRegistryValidationResult();
		IReadOnlyList<FeatureSmokeTestDescriptor> all = GetAll();
		if (!all.Any())
		{
			featureRegistryValidationResult.AddError("Feature smoke test catalog is empty.");
		}
		ValidateRequiredFields(all, featureRegistryValidationResult);
		ValidateUniqueAreas(all, featureRegistryValidationResult);
		ValidateFeatureCoverage(all, featureRegistryValidationResult);
		return featureRegistryValidationResult;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ValidateRequiredFields(IEnumerable<FeatureSmokeTestDescriptor> descriptors, FeatureRegistryValidationResult result)
	{
		foreach (FeatureSmokeTestDescriptor descriptor in descriptors)
		{
			if (descriptor != null)
			{
				if (string.IsNullOrWhiteSpace(descriptor.FeatureId))
				{
					result.AddError("Feature smoke test feature id cannot be empty.");
				}
				if (string.IsNullOrWhiteSpace(descriptor.Area))
				{
					result.AddError("Feature smoke test area cannot be empty: " + descriptor.FeatureId);
				}
				if (string.IsNullOrWhiteSpace(descriptor.Assertion))
				{
					result.AddError("Feature smoke test assertion cannot be empty: " + descriptor.FeatureId + "/" + descriptor.Area);
				}
			}
			else
			{
				result.AddError("Feature smoke test descriptor cannot be empty.");
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ValidateUniqueAreas(IEnumerable<FeatureSmokeTestDescriptor> descriptors, FeatureRegistryValidationResult result)
	{
		foreach (string item in from g in descriptors.Where((FeatureSmokeTestDescriptor x) => x != null && !string.IsNullOrWhiteSpace(x.FeatureId) && !string.IsNullOrWhiteSpace(x.Area)).GroupBy<FeatureSmokeTestDescriptor, string>([MethodImpl(MethodImplOptions.NoInlining)] (FeatureSmokeTestDescriptor x) => x.FeatureId.Trim() + "/" + x.Area.Trim(), StringComparer.OrdinalIgnoreCase)
			where g.Count() > 1
			select g.Key)
		{
			result.AddError("Duplicate feature smoke test area: " + item);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ValidateFeatureCoverage(IEnumerable<FeatureSmokeTestDescriptor> descriptors, FeatureRegistryValidationResult result)
	{
		HashSet<string> hashSet = new HashSet<string>(from x in FeatureRegistry.GetAll()
			select x.Id, StringComparer.OrdinalIgnoreCase);
		HashSet<string> hashSet2 = new HashSet<string>(from x in descriptors
			where x != null && !string.IsNullOrWhiteSpace(x.FeatureId)
			select x.FeatureId, StringComparer.OrdinalIgnoreCase);
		foreach (FeatureSmokeTestDescriptor item in descriptors.Where((FeatureSmokeTestDescriptor x) => x != null && !string.IsNullOrWhiteSpace(x.FeatureId)))
		{
			if (!hashSet.Contains(item.FeatureId))
			{
				result.AddError("Feature smoke test references unknown feature: " + item.FeatureId);
			}
		}
		foreach (FeatureDescriptor item2 in FeatureRegistry.GetAll())
		{
			if (!hashSet2.Contains(item2.Id))
			{
				result.AddError("Feature has no smoke test descriptor: " + item2.Id);
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static List<FeatureSmokeTestDescriptor> CreateDescriptors()
	{
		return new List<FeatureSmokeTestDescriptor>
		{
			Item("format", "MainTitleRecognition", "Main title recognition keeps working.", required: true),
			Item("format", "SubtitleRecognition", "Subtitle recognition keeps working.", required: true),
			Item("format", "SignatureRecognition", "Signature and date recognition keeps working.", required: true),
			Item("redheader", "TemplateParameters", "Red header templates expose required parameters.", required: true),
			Item("rename", "TitlePreview", "Rename preview can use the detected main title.", required: true),
			Item("rename", "DocumentNumberPreview", "Rename preview can use the detected document number.", required: true),
			Item("rename", "SubtitlePreview", "Rename preview can use the detected subtitle.", required: true),
			Item("replace", "ActiveReplacePlan", "Replace feature keeps an active local plan entry.", required: true),
			Item("convert", "DefaultConvertRule", "Convert feature keeps a default conversion rule.", required: true),
			Item("pdf-to-word", "ConversionPipeline", "PDF to Word keeps source validation, planning, conversion and output verification wired together.", required: true)
		};
	}

	private static FeatureSmokeTestDescriptor Item(string featureId, string area, string assertion, bool required)
	{
		return new FeatureSmokeTestDescriptor
		{
			FeatureId = featureId,
			Area = area,
			Assertion = assertion,
			Required = required
		};
	}
}
