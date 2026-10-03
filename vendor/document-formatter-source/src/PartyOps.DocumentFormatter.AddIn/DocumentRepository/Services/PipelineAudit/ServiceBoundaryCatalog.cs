using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using DocumentRepository.Models.Services;

namespace DocumentRepository.Services.PipelineAudit;

public static class ServiceBoundaryCatalog
{
	private const string ServicesNamespace = "DocumentRepository.Services";

	private static readonly string[] ForbiddenGenericTypeNames = new string[3] { "Utils", "Common", "Helper" };

	private static readonly Dictionary<string, string> ApprovedManagerTypes = new Dictionary<string, string>(StringComparer.Ordinal) { { "DocumentRepository.Services.Formatting.DocumentStyleManager", "Owns Word style creation and refresh; kept as an explicit formatting boundary." } };

	public static IReadOnlyList<ServiceBoundaryDescriptor> GetAll()
	{
		return CreateDescriptors().AsReadOnly();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static ServiceBoundaryDescriptor GetBySegment(string namespaceSegment)
	{
		if (string.IsNullOrWhiteSpace(namespaceSegment))
		{
			throw new ArgumentException("Namespace segment cannot be empty.", "namespaceSegment");
		}
		return GetAll().FirstOrDefault((ServiceBoundaryDescriptor x) => string.Equals(x.NamespaceSegment, namespaceSegment, StringComparison.Ordinal)) ?? throw new InvalidOperationException("Service boundary descriptor does not exist: " + namespaceSegment);
	}

	public static IReadOnlyDictionary<string, string> GetApprovedManagerTypes()
	{
		return ApprovedManagerTypes;
	}

	public static ServiceBoundaryValidationResult Validate()
	{
		ServiceBoundaryValidationResult result = new ServiceBoundaryValidationResult();
		IReadOnlyList<ServiceBoundaryDescriptor> all = GetAll();
		ValidateDescriptorFields(all, result);
		ValidateUniqueSegments(all, result);
		ValidateRuntimeTypes(all, result);
		return result;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void AssertValid()
	{
		ServiceBoundaryValidationResult serviceBoundaryValidationResult = Validate();
		if (!serviceBoundaryValidationResult.Success)
		{
			throw new InvalidOperationException("Service boundary validation failed: " + string.Join("; ", serviceBoundaryValidationResult.Errors));
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ValidateDescriptorFields(IEnumerable<ServiceBoundaryDescriptor> descriptors, ServiceBoundaryValidationResult result)
	{
		foreach (ServiceBoundaryDescriptor descriptor in descriptors)
		{
			if (string.IsNullOrWhiteSpace(descriptor.NamespaceSegment))
			{
				result.AddError("Service boundary namespace segment cannot be empty.");
			}
			if (string.IsNullOrWhiteSpace(descriptor.Responsibility))
			{
				result.AddError("Service boundary responsibility cannot be empty: " + descriptor.NamespaceSegment);
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ValidateUniqueSegments(IEnumerable<ServiceBoundaryDescriptor> descriptors, ServiceBoundaryValidationResult result)
	{
		foreach (string item in from g in descriptors.Where((ServiceBoundaryDescriptor x) => !string.IsNullOrWhiteSpace(x.NamespaceSegment)).GroupBy<ServiceBoundaryDescriptor, string>((ServiceBoundaryDescriptor x) => x.NamespaceSegment, StringComparer.Ordinal)
			where g.Count() > 1
			select g.Key)
		{
			result.AddError("Duplicate service boundary segment: " + item);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ValidateRuntimeTypes(IReadOnlyList<ServiceBoundaryDescriptor> descriptors, ServiceBoundaryValidationResult result)
	{
		HashSet<string> hashSet = new HashSet<string>(descriptors.Select((ServiceBoundaryDescriptor x) => x.NamespaceSegment), StringComparer.Ordinal);
		foreach (Type serviceType in GetServiceTypes())
		{
			string namespaceSegment = GetNamespaceSegment(serviceType);
			if (!string.IsNullOrWhiteSpace(namespaceSegment))
			{
				if (!hashSet.Contains(namespaceSegment))
				{
					result.AddError("Service namespace segment is not registered: " + serviceType.FullName + " -> " + namespaceSegment);
				}
				if (HasForbiddenGenericName(serviceType))
				{
					result.AddError("Service type uses a generic utility-style name: " + serviceType.FullName);
				}
				if (serviceType.Name.EndsWith("Manager", StringComparison.Ordinal) && !ApprovedManagerTypes.ContainsKey(serviceType.FullName))
				{
					result.AddError("Manager service must be renamed or explicitly approved: " + serviceType.FullName);
				}
			}
			else
			{
				result.AddError("Service type must not live in root Services namespace: " + serviceType.FullName);
			}
		}
	}

	private static IEnumerable<Type> GetServiceTypes()
	{
		return from t in typeof(ServiceBoundaryCatalog).Assembly.GetTypes()
			where t.IsClass && !t.IsNested
			where !string.IsNullOrWhiteSpace(t.Namespace)
			where t.Namespace == "DocumentRepository.Services" || t.Namespace.StartsWith("DocumentRepository.Services.", StringComparison.Ordinal)
			select t;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string GetNamespaceSegment(Type type)
	{
		if (!(type.Namespace == "DocumentRepository.Services"))
		{
			string text = type.Namespace.Substring("DocumentRepository.Services".Length + 1);
			int num = text.IndexOf('.');
			if (num >= 0)
			{
				return text.Substring(0, num);
			}
			return text;
		}
		return string.Empty;
	}

	private static bool HasForbiddenGenericName(Type type)
	{
		return ForbiddenGenericTypeNames.Any((string token) => type.Name.Equals(token, StringComparison.Ordinal) || type.Name.EndsWith(token, StringComparison.Ordinal));
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static List<ServiceBoundaryDescriptor> CreateDescriptors()
	{
		return new List<ServiceBoundaryDescriptor>
		{
			Descriptor("Analysis", ServiceBoundaryKind.DocumentProcessing, "Read document facts and produce analysis models.", allowsDocumentWrite: false, allowsFileWrite: false, allowsFeatureBusinessLogic: true),
			Descriptor("Auth", ServiceBoundaryKind.PublicInfrastructure, "Check feature permissions and authorization boundaries.", allowsDocumentWrite: false, allowsFileWrite: false, allowsFeatureBusinessLogic: false),
			Descriptor("Cleanup", ServiceBoundaryKind.DocumentProcessing, "Remove configured document noise and normalize document structure.", allowsDocumentWrite: true, allowsFileWrite: false, allowsFeatureBusinessLogic: true),
			Descriptor("CompilationFormatting", ServiceBoundaryKind.FeatureDomain, "Detect compilation boundaries and update article lists, separators and owned table-of-contents regions.", allowsDocumentWrite: true, allowsFileWrite: false, allowsFeatureBusinessLogic: true),
			Descriptor("Configuration", ServiceBoundaryKind.PublicInfrastructure, "Import, export, validate and migrate local configuration packages.", allowsDocumentWrite: false, allowsFileWrite: true, allowsFeatureBusinessLogic: false),
			Descriptor("Conversion", ServiceBoundaryKind.FeatureDomain, "Analyze conversion requests and export verified DOCX, PDF, TXT or image outputs.", allowsDocumentWrite: false, allowsFileWrite: true, allowsFeatureBusinessLogic: true),
			Descriptor("Detection", ServiceBoundaryKind.DocumentProcessing, "Detect document structures such as titles, attachments, tables and images.", allowsDocumentWrite: false, allowsFileWrite: false, allowsFeatureBusinessLogic: true),
			Descriptor("Features", ServiceBoundaryKind.PublicInfrastructure, "Register feature descriptors and create commands from feature metadata.", allowsDocumentWrite: false, allowsFileWrite: false, allowsFeatureBusinessLogic: false),
			Descriptor("FileSafety", ServiceBoundaryKind.PublicInfrastructure, "Provide atomic file commits, produced-file integrity validation and path safety helpers.", allowsDocumentWrite: false, allowsFileWrite: true, allowsFeatureBusinessLogic: false),
			Descriptor("Formatting", ServiceBoundaryKind.DocumentProcessing, "Apply formatting plans, styles, objects and small post-processing operations.", allowsDocumentWrite: true, allowsFileWrite: false, allowsFeatureBusinessLogic: true),
			Descriptor("Hosting", ServiceBoundaryKind.PublicInfrastructure, "Own Word/WPS host capabilities, document sessions, application-state recovery and COM release policy.", allowsDocumentWrite: true, allowsFileWrite: false, allowsFeatureBusinessLogic: false),
			Descriptor("Interop", ServiceBoundaryKind.PublicInfrastructure, "Wrap Word/WPS interop state, range inspection and COM-facing helpers.", allowsDocumentWrite: false, allowsFileWrite: false, allowsFeatureBusinessLogic: false),
			Descriptor("Logging", ServiceBoundaryKind.PublicInfrastructure, "Write diagnostic logs and expose logging boundaries.", allowsDocumentWrite: false, allowsFileWrite: true, allowsFeatureBusinessLogic: false),
			Descriptor("Mutations", ServiceBoundaryKind.PublicInfrastructure, "Validate and transactionally execute immutable document mutation plans.", allowsDocumentWrite: true, allowsFileWrite: false, allowsFeatureBusinessLogic: false),
			Descriptor("Normalization", ServiceBoundaryKind.DocumentProcessing, "Normalize document text or heading spacing facts without owning feature flow.", allowsDocumentWrite: true, allowsFileWrite: false, allowsFeatureBusinessLogic: true),
			Descriptor("Performance", ServiceBoundaryKind.PublicInfrastructure, "Capture first-format timing and diagnostic measurements.", allowsDocumentWrite: false, allowsFileWrite: false, allowsFeatureBusinessLogic: false),
			Descriptor("PipelineAudit", ServiceBoundaryKind.QualityGate, "Validate architecture responsibility catalogs and service boundaries.", allowsDocumentWrite: false, allowsFileWrite: false, allowsFeatureBusinessLogic: false),
			Descriptor("Protection", ServiceBoundaryKind.DocumentProcessing, "Read document risk facts and produce conservative risk profiles for protection decisions.", allowsDocumentWrite: false, allowsFileWrite: false, allowsFeatureBusinessLogic: true),
			Descriptor("Recovery", ServiceBoundaryKind.PublicInfrastructure, "Prepare, verify, index and retain file-level recovery copies.", allowsDocumentWrite: false, allowsFileWrite: true, allowsFeatureBusinessLogic: false),
			Descriptor("RedHeader", ServiceBoundaryKind.FeatureDomain, "Build and apply red header layout plans from local templates.", allowsDocumentWrite: true, allowsFileWrite: false, allowsFeatureBusinessLogic: true),
			Descriptor("Rename", ServiceBoundaryKind.FeatureDomain, "Analyze naming tokens, build target names and save renamed documents.", allowsDocumentWrite: true, allowsFileWrite: true, allowsFeatureBusinessLogic: true),
			Descriptor("Replace", ServiceBoundaryKind.FeatureDomain, "Load replace plans, resolve scopes and apply text or format replacements.", allowsDocumentWrite: true, allowsFileWrite: false, allowsFeatureBusinessLogic: true),
			Descriptor("Rules", ServiceBoundaryKind.PublicInfrastructure, "Load, save, validate and catalog local rule stores.", allowsDocumentWrite: false, allowsFileWrite: true, allowsFeatureBusinessLogic: false),
			Descriptor("Safety", ServiceBoundaryKind.PublicInfrastructure, "Evaluate execution admission decisions and rule-store availability boundaries.", allowsDocumentWrite: false, allowsFileWrite: false, allowsFeatureBusinessLogic: false),
			Descriptor("Snapshots", ServiceBoundaryKind.PublicInfrastructure, "Capture immutable document, paragraph and protected-object facts without modifying the document.", allowsDocumentWrite: false, allowsFileWrite: false, allowsFeatureBusinessLogic: false),
			Descriptor("Startup", ServiceBoundaryKind.PublicInfrastructure, "Coordinate startup steps, health checks and controlled fault-injection diagnostics.", allowsDocumentWrite: false, allowsFileWrite: false, allowsFeatureBusinessLogic: false),
			Descriptor("Ui", ServiceBoundaryKind.UiSupport, "Route user confirmations and actionable dialogs through the unified interaction port.", allowsDocumentWrite: false, allowsFileWrite: false, allowsFeatureBusinessLogic: false),
			Descriptor("Tasks", ServiceBoundaryKind.PublicInfrastructure, "Report task progress through common progress reporter interfaces.", allowsDocumentWrite: false, allowsFileWrite: false, allowsFeatureBusinessLogic: false),
			Descriptor("UiText", ServiceBoundaryKind.UiSupport, "Provide centralized UI text registry and UI text application helpers.", allowsDocumentWrite: false, allowsFileWrite: false, allowsFeatureBusinessLogic: false),
			Descriptor("Updates", ServiceBoundaryKind.PublicInfrastructure, "Parse and normalize update metadata without owning transport or installation.", allowsDocumentWrite: false, allowsFileWrite: false, allowsFeatureBusinessLogic: false)
		};
	}

	private static ServiceBoundaryDescriptor Descriptor(string namespaceSegment, ServiceBoundaryKind kind, string responsibility, bool allowsDocumentWrite, bool allowsFileWrite, bool allowsFeatureBusinessLogic)
	{
		return new ServiceBoundaryDescriptor
		{
			NamespaceSegment = namespaceSegment,
			Kind = kind,
			Responsibility = responsibility,
			AllowsDocumentWrite = allowsDocumentWrite,
			AllowsFileWrite = allowsFileWrite,
			AllowsFeatureBusinessLogic = allowsFeatureBusinessLogic
		};
	}
}
