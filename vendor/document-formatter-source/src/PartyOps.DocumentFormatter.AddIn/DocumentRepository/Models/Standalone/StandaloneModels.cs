using System;
using System.Collections.Generic;
using System.Threading;

namespace DocumentRepository.Models.Standalone;

public enum OfficeHostPreference
{
	Auto,
	Wps,
	Word
}

public sealed class FeatureChoice
{
	public FeatureChoice(string id, string displayName, string outputSuffix)
	{
		Id = id;
		DisplayName = displayName;
		OutputSuffix = outputSuffix;
	}

	public string Id { get; }

	public string DisplayName { get; }

	public string OutputSuffix { get; }

	public override string ToString()
	{
		return DisplayName;
	}
}

public sealed class HostChoice
{
	public HostChoice(OfficeHostPreference value, string displayName)
	{
		Value = value;
		DisplayName = displayName;
	}

	public OfficeHostPreference Value { get; }

	public string DisplayName { get; }

	public override string ToString()
	{
		return DisplayName;
	}
}

public sealed class StandaloneBatchRequest
{
	public IReadOnlyList<string> SourcePaths { get; set; }

	public string OutputDirectory { get; set; }

	public string FeatureId { get; set; }

	public string FeatureDisplayName { get; set; }

	public string OutputSuffix { get; set; }

	public OfficeHostPreference HostPreference { get; set; }

	public bool ExportDocx { get; set; }

	public bool ExportPdf { get; set; }

	public bool ExportTxt { get; set; }

	public CancellationToken CancellationToken { get; set; }
}

/// <summary>
/// 独立版文档引擎自检请求。自检只读打开源文件并把结果另存到输出目录，不执行任何业务功能。
/// </summary>
public sealed class StandaloneHostProbeRequest
{
	public string SourcePath { get; set; }

	public string OutputDirectory { get; set; }

	public OfficeHostPreference HostPreference { get; set; }

	public CancellationToken CancellationToken { get; set; }
}

public sealed class ProcessingProgress
{
	public int Completed { get; set; }

	public int Total { get; set; }

	public int Percent { get; set; }

	public string SourcePath { get; set; }

	public string Message { get; set; }
}

public sealed class StandaloneJobResult
{
	public string SourcePath { get; set; }

	public bool Success { get; set; }

	public bool Cancelled { get; set; }

	public string Message { get; set; }

	public string HostDisplayName { get; set; }

	public List<string> OutputPaths { get; } = new List<string>();
}

public sealed class StandaloneBatchResult
{
	public List<StandaloneJobResult> Jobs { get; } = new List<StandaloneJobResult>();

	public int SuccessCount => Jobs.FindAll(job => job.Success).Count;

	public int FailureCount => Jobs.FindAll(job => !job.Success && !job.Cancelled).Count;

	public int CancelledCount => Jobs.FindAll(job => job.Cancelled).Count;
}
