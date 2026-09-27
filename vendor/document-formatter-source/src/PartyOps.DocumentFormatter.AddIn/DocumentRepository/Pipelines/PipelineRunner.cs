using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using DocumentRepository.Models;
using DocumentRepository.Services.Logging;

namespace DocumentRepository.Pipelines;

public class PipelineRunner
{
	private readonly List<IPipelineStep> steps = new List<IPipelineStep>();

	public string Name { get; set; } = "UnnamedPipeline";

	public int ExecutedCount { get; private set; }

	public int SkippedCount { get; private set; }

	public int FailedCount { get; private set; }

	[MethodImpl(MethodImplOptions.NoInlining)]
	public PipelineRunner AddStep(IPipelineStep step)
	{
		if (step == null)
		{
			throw new ArgumentNullException("step");
		}
		steps.Add(step);
		return this;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public PipelineRunner AddSteps(params IPipelineStep[] pipelineSteps)
	{
		if (pipelineSteps != null)
		{
			foreach (IPipelineStep step in pipelineSteps)
			{
				AddStep(step);
			}
			return this;
		}
		throw new ArgumentNullException("pipelineSteps");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public List<StepResult> ExecuteAll(OperationContext context)
	{
		if (context == null)
		{
			throw new ArgumentNullException("context");
		}
		List<StepResult> list = new List<StepResult>();
		ExecutedCount = 0;
		SkippedCount = 0;
		FailedCount = 0;
		foreach (IPipelineStep step in steps)
		{
			StepResult stepResult = step.Execute(context);
			if (stepResult != null)
			{
				list.Add(stepResult);
				if (!stepResult.Success)
				{
					FailedCount++;
				}
				else
				{
					ExecutedCount++;
				}
				if (!stepResult.Success && !stepResult.CanContinue)
				{
					LogService.Warn("[" + Name + "] 因 [" + step.Name + "] 关键错误中断，已执行 " + list.Count + "/" + steps.Count + " 步。");
					break;
				}
				continue;
			}
			throw new InvalidOperationException("流水线步骤返回 null：" + step.Name);
		}
		return list;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public PipelineRunner()
	{
	}
}
