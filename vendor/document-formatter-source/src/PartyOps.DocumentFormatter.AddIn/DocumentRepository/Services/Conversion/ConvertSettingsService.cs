using System;
using System.IO;
using System.Runtime.CompilerServices;
using DocumentRepository.Models.Conversion;
using DocumentRepository.Models.Rules;
using DocumentRepository.Services.Rules;

namespace DocumentRepository.Services.Conversion;

public static class ConvertSettingsService
{
	private static readonly object SyncRoot = new object();

	private static readonly string StorePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DocumentRepository", "ConvertOptions.xml");

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static RuleStoreDescriptor GetStoreDescriptor()
	{
		return new RuleStoreDescriptor
		{
			Id = "convert.options",
			StorePath = StorePath,
			OwnerService = "ConvertSettingsService",
			ModelTypeName = "ConvertOptions"
		};
	}

	public static RuleLoadResult<ConvertOptions> LoadResult()
	{
		lock (SyncRoot)
		{
			return XmlRuleStore.LoadWithSelfHealing(GetStoreDescriptor(), () => new ConvertOptions(), Normalize);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static ConvertOptions Load()
	{
		RuleLoadResult<ConvertOptions> ruleLoadResult = LoadResult();
		if (!ruleLoadResult.Usable)
		{
			throw RuleStoreUnavailableException.From(ruleLoadResult, "转换参数");
		}
		return ruleLoadResult.Value;
	}

	public static RuleLoadResult<ConvertOptions> ResetToDefault()
	{
		lock (SyncRoot)
		{
			return XmlRuleStore.ResetToDefault(GetStoreDescriptor(), () => new ConvertOptions(), Normalize);
		}
	}

	public static void Save(ConvertOptions options)
	{
		lock (SyncRoot)
		{
			XmlRuleStore.Save(GetStoreDescriptor(), options, Normalize);
		}
	}

	public static void SetSelectedFormat(ConvertFormat format)
	{
		ConvertOptions convertOptions = Load();
		convertOptions.SelectedFormat = format;
		Save(convertOptions);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static ConvertOptions Normalize(ConvertOptions options)
	{
		if (options == null)
		{
			throw new InvalidOperationException("Convert options cannot be empty.");
		}
		if (options.CustomOutputFolder == null)
		{
			options.CustomOutputFolder = "";
		}
		if (options.ImagePageRange == null)
		{
			options.ImagePageRange = "";
		}
		if (options.ImageSelectedPages == null)
		{
			options.ImageSelectedPages = "";
		}
		if (options.ImageDpi <= 0)
		{
			options.ImageDpi = 200;
		}
		if (options.ImageDpi < 72)
		{
			options.ImageDpi = 72;
		}
		if (options.ImageDpi > 600)
		{
			options.ImageDpi = 600;
		}
		if (!Enum.IsDefined(typeof(PdfToWordEngine), options.PdfToWordEngine))
		{
			options.PdfToWordEngine = PdfToWordEngine.Local;
		}
		return options;
	}
}
