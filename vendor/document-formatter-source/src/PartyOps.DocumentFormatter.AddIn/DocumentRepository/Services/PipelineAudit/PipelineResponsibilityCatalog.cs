using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using DocumentRepository.Models.Features;
using DocumentRepository.Models.Pipelines;
using DocumentRepository.Services.Features;

namespace DocumentRepository.Services.PipelineAudit;

public static class PipelineResponsibilityCatalog
{
	private static readonly string[] RefreshRequiredFeatures = new string[1] { "format" };

	private static readonly string[] VerificationRequiredFeatures = new string[6] { "format", "replace", "redheader", "rename", "convert", "pdf-to-word" };

	public static IReadOnlyList<PipelineResponsibilityDescriptor> GetAll()
	{
		return CreateDescriptors().AsReadOnly();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static IReadOnlyList<PipelineResponsibilityDescriptor> GetByFeature(string featureId)
	{
		if (string.IsNullOrWhiteSpace(featureId))
		{
			throw new ArgumentException("Feature id cannot be empty.", "featureId");
		}
		return (from x in GetAll()
			where string.Equals(x.FeatureId, featureId, StringComparison.OrdinalIgnoreCase)
			select x).ToList().AsReadOnly();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static PipelineResponsibilityValidationResult Validate()
	{
		PipelineResponsibilityValidationResult pipelineResponsibilityValidationResult = new PipelineResponsibilityValidationResult();
		IReadOnlyList<PipelineResponsibilityDescriptor> all = GetAll();
		if (all.Any())
		{
			ValidateDescriptorFields(all, pipelineResponsibilityValidationResult);
			ValidateFeatureCoverage(all, pipelineResponsibilityValidationResult);
			ValidateStageRules(all, pipelineResponsibilityValidationResult);
			ValidateOwnerTypes(all, pipelineResponsibilityValidationResult);
			return pipelineResponsibilityValidationResult;
		}
		pipelineResponsibilityValidationResult.AddError("Pipeline responsibility catalog is empty.");
		return pipelineResponsibilityValidationResult;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void AssertValid()
	{
		PipelineResponsibilityValidationResult pipelineResponsibilityValidationResult = Validate();
		if (pipelineResponsibilityValidationResult.Success)
		{
			return;
		}
		throw new InvalidOperationException("Pipeline responsibility catalog validation failed: " + string.Join("; ", pipelineResponsibilityValidationResult.Errors));
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ValidateDescriptorFields(IEnumerable<PipelineResponsibilityDescriptor> descriptors, PipelineResponsibilityValidationResult result)
	{
		foreach (PipelineResponsibilityDescriptor descriptor in descriptors)
		{
			if (string.IsNullOrWhiteSpace(descriptor.FeatureId))
			{
				result.AddError("Pipeline descriptor feature id cannot be empty.");
			}
			if (string.IsNullOrWhiteSpace(descriptor.StepName))
			{
				result.AddError("Pipeline descriptor step name cannot be empty: " + descriptor.FeatureId);
			}
			if (string.IsNullOrWhiteSpace(descriptor.OwnerTypeName))
			{
				result.AddError("Pipeline descriptor owner type cannot be empty: " + descriptor.FeatureId + "/" + descriptor.StepName);
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ValidateFeatureCoverage(IReadOnlyList<PipelineResponsibilityDescriptor> descriptors, PipelineResponsibilityValidationResult result)
	{
		foreach (FeatureDescriptor feature in FeatureRegistry.GetAll())
		{
			List<PipelineResponsibilityDescriptor> list = descriptors.Where((PipelineResponsibilityDescriptor x) => string.Equals(x.FeatureId, feature.Id, StringComparison.OrdinalIgnoreCase)).ToList();
			if (list.Any())
			{
				RequireKind(feature.Id, list, PipelineResponsibilityKind.Analyze, result);
				RequireKind(feature.Id, list, PipelineResponsibilityKind.Plan, result);
				RequireKind(feature.Id, list, PipelineResponsibilityKind.Apply, result);
				RequireKind(feature.Id, list, PipelineResponsibilityKind.Result, result);
				if (RefreshRequiredFeatures.Contains<string>(feature.Id, StringComparer.OrdinalIgnoreCase))
				{
					RequireKind(feature.Id, list, PipelineResponsibilityKind.Refresh, result);
				}
				if (VerificationRequiredFeatures.Contains<string>(feature.Id, StringComparer.OrdinalIgnoreCase))
				{
					RequireKind(feature.Id, list, PipelineResponsibilityKind.Verify, result);
				}
			}
			else
			{
				result.AddError("Feature has no pipeline responsibility descriptors: " + feature.Id);
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void RequireKind(string featureId, IEnumerable<PipelineResponsibilityDescriptor> descriptors, PipelineResponsibilityKind kind, PipelineResponsibilityValidationResult result)
	{
		if (!descriptors.Any((PipelineResponsibilityDescriptor x) => x.Kind == kind))
		{
			result.AddError("Feature is missing pipeline stage " + kind.ToString() + ": " + featureId);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ValidateStageRules(IEnumerable<PipelineResponsibilityDescriptor> descriptors, PipelineResponsibilityValidationResult result)
	{
		foreach (PipelineResponsibilityDescriptor descriptor in descriptors)
		{
			if (descriptor.Kind == PipelineResponsibilityKind.Analyze && descriptor.WritesDocument)
			{
				result.AddError("Analyze stage must not write document: " + Describe(descriptor));
			}
			if (descriptor.Kind == PipelineResponsibilityKind.Plan)
			{
				if (descriptor.ReadsDocument)
				{
					result.AddError("Plan stage must not read Word/WPS document directly: " + Describe(descriptor));
				}
				if (descriptor.WritesDocument)
				{
					result.AddError("Plan stage must not write document: " + Describe(descriptor));
				}
				if (descriptor.WritesFileSystem)
				{
					result.AddError("Plan stage must not write file system: " + Describe(descriptor));
				}
			}
			if (descriptor.Kind == PipelineResponsibilityKind.Verify && descriptor.WritesDocument)
			{
				result.AddError("Verify stage must not write document: " + Describe(descriptor));
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ValidateOwnerTypes(IEnumerable<PipelineResponsibilityDescriptor> descriptors, PipelineResponsibilityValidationResult result)
	{
		HashSet<string> hashSet = new HashSet<string>(from type in typeof(PipelineResponsibilityCatalog).Assembly.GetTypes()
			select type.Name, StringComparer.Ordinal);
		foreach (PipelineResponsibilityDescriptor descriptor in descriptors)
		{
			string[] array = (descriptor.OwnerTypeName ?? string.Empty).Split(new char[1] { '/' }, StringSplitOptions.RemoveEmptyEntries);
			for (int num = 0; num < array.Length; num++)
			{
				string text = array[num].Trim();
				if (!hashSet.Contains(text))
				{
					result.AddError("Pipeline owner type does not exist: " + Describe(descriptor) + " -> " + text);
				}
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string Describe(PipelineResponsibilityDescriptor descriptor)
	{
		return descriptor.FeatureId + "/" + descriptor.StepName + "/" + descriptor.OwnerTypeName;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static List<PipelineResponsibilityDescriptor> CreateDescriptors()
	{
		List<PipelineResponsibilityDescriptor> list = new List<PipelineResponsibilityDescriptor>();
		list.AddRange(Feature("format", Stage(PipelineResponsibilityKind.Analyze, "Build document element facts", "DocumentAnalysisService / ParagraphElementClassifier", readsDocument: true, writesDocument: false, writesFileSystem: false), Stage(PipelineResponsibilityKind.Plan, "Build immutable format mutation plan from isolated planning copy", "FormatExecutionPlanBuilder", readsDocument: false, writesDocument: false, writesFileSystem: false), Stage(PipelineResponsibilityKind.Apply, "Apply paragraph and object styles", "ParagraphStyleApplyService / FormatDocumentElementService", readsDocument: true, writesDocument: true, writesFileSystem: false), Stage(PipelineResponsibilityKind.Refresh, "Refresh planned anchor positions without reclassification", "FormatExecutionAnchorSet", readsDocument: true, writesDocument: false, writesFileSystem: false), Stage(PipelineResponsibilityKind.Verify, "Verify planned styles, protected objects and selection boundary", "FormatPlanVerifier", readsDocument: true, writesDocument: false, writesFileSystem: false), Stage(PipelineResponsibilityKind.Result, "Return format command result", "FormatPipeline / FormatCommand", readsDocument: false, writesDocument: false, writesFileSystem: false)));
		list.AddRange(Feature("replace", Stage(PipelineResponsibilityKind.Analyze, "Resolve replace scope", "ReplaceScopeService", readsDocument: true, writesDocument: false, writesFileSystem: false), Stage(PipelineResponsibilityKind.Plan, "Load replace plan", "ReplacePlanService", readsDocument: false, writesDocument: false, writesFileSystem: false), Stage(PipelineResponsibilityKind.Apply, "Apply text and format replacements", "TextReplaceService / FormatReplaceService", readsDocument: true, writesDocument: true, writesFileSystem: false), Stage(PipelineResponsibilityKind.Verify, "Verify replacement result and scope boundary", "ReplaceVerifier", readsDocument: true, writesDocument: false, writesFileSystem: false), Stage(PipelineResponsibilityKind.Result, "Return replace summary", "ReplacePipeline / ReplaceCommand", readsDocument: false, writesDocument: false, writesFileSystem: false)));
		list.AddRange(Feature("redheader", Stage(PipelineResponsibilityKind.Analyze, "Capture document snapshot and first/last section geometry", "RedHeaderAnalysisService", readsDocument: true, writesDocument: false, writesFileSystem: false), Stage(PipelineResponsibilityKind.Plan, "Build red header layout plan", "RedHeaderLayoutPlanner", readsDocument: false, writesDocument: false, writesFileSystem: false), Stage(PipelineResponsibilityKind.Apply, "Generate red header and imprint table", "RedHeaderGenerationService", readsDocument: true, writesDocument: true, writesFileSystem: false), Stage(PipelineResponsibilityKind.Verify, "Verify red header text, shapes, imprint and final page", "RedHeaderVerifier", readsDocument: true, writesDocument: false, writesFileSystem: false), Stage(PipelineResponsibilityKind.Result, "Return red header result", "RedHeaderPipeline / RedHeaderCommand", readsDocument: false, writesDocument: false, writesFileSystem: false)));
		list.AddRange(Feature("rename", Stage(PipelineResponsibilityKind.Analyze, "Extract title and naming tokens", "RenameAnalysisService", readsDocument: true, writesDocument: false, writesFileSystem: false), Stage(PipelineResponsibilityKind.Plan, "Build rename execution plan", "RenamePlanBuilder", readsDocument: false, writesDocument: false, writesFileSystem: false), Stage(PipelineResponsibilityKind.Apply, "Save renamed document", "RenameExecutor / SaveRenameService", readsDocument: true, writesDocument: true, writesFileSystem: true), Stage(PipelineResponsibilityKind.Verify, "Verify output integrity and active document path", "RenameVerifier", readsDocument: true, writesDocument: false, writesFileSystem: false), Stage(PipelineResponsibilityKind.Result, "Return rename message", "RenamePipeline / RenameCommand", readsDocument: false, writesDocument: false, writesFileSystem: false)));
		list.AddRange(Feature("convert", Stage(PipelineResponsibilityKind.Analyze, "Read current document conversion facts", "ConvertDocumentAnalyzer", readsDocument: true, writesDocument: false, writesFileSystem: false), Stage(PipelineResponsibilityKind.Plan, "Build conversion plan", "ConvertPlanBuilder", readsDocument: false, writesDocument: false, writesFileSystem: false), Stage(PipelineResponsibilityKind.Apply, "Export target files", "ConvertExecutor / DocumentExportService / ImageConversionService", readsDocument: true, writesDocument: false, writesFileSystem: true), Stage(PipelineResponsibilityKind.Verify, "Verify conversion outputs", "ConvertVerifier", readsDocument: false, writesDocument: false, writesFileSystem: false), Stage(PipelineResponsibilityKind.Result, "Return converted files", "ConvertPipeline / ConvertCommand", readsDocument: false, writesDocument: false, writesFileSystem: false)));
		list.AddRange(Feature("pdf-to-word", Stage(PipelineResponsibilityKind.Analyze, "Validate and inspect the PDF source", "PdfToWordSourceResolver / PdfReadingAdapter", readsDocument: false, writesDocument: false, writesFileSystem: false), Stage(PipelineResponsibilityKind.Plan, "Build the PDF to Word execution plan", "PdfToWordPlanBuilder", readsDocument: false, writesDocument: false, writesFileSystem: false), Stage(PipelineResponsibilityKind.Apply, "Convert PDF contents and commit the DOCX output", "PdfToWordConversionService / LocalPdfToWordEngine", readsDocument: true, writesDocument: false, writesFileSystem: true), Stage(PipelineResponsibilityKind.Verify, "Verify DOCX integrity and reopened output", "PdfToWordVerifier", readsDocument: true, writesDocument: false, writesFileSystem: false), Stage(PipelineResponsibilityKind.Result, "Return the PDF to Word result", "PdfToWordPipeline / PdfToWordCommand", readsDocument: false, writesDocument: false, writesFileSystem: false)));
		return list;
	}

	private static IEnumerable<PipelineResponsibilityDescriptor> Feature(string featureId, params PipelineResponsibilityDescriptor[] descriptors)
	{
		foreach (PipelineResponsibilityDescriptor pipelineResponsibilityDescriptor in descriptors)
		{
			pipelineResponsibilityDescriptor.FeatureId = featureId;
			pipelineResponsibilityDescriptor.Required = true;
			yield return pipelineResponsibilityDescriptor;
		}
	}

	private static PipelineResponsibilityDescriptor Stage(PipelineResponsibilityKind kind, string stepName, string ownerTypeName, bool readsDocument, bool writesDocument, bool writesFileSystem)
	{
		return new PipelineResponsibilityDescriptor
		{
			Kind = kind,
			StepName = stepName,
			OwnerTypeName = ownerTypeName,
			ReadsDocument = readsDocument,
			WritesDocument = writesDocument,
			WritesFileSystem = writesFileSystem
		};
	}
}
