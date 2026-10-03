namespace DocumentRepository.Models.Features;

public sealed class FeatureExecutionOptions
{
	public string InvocationSource { get; set; }

	public bool SuppressUserDialogs { get; set; }

	public static FeatureExecutionOptions External(string invocationSource)
	{
		return new FeatureExecutionOptions
		{
			InvocationSource = invocationSource
		};
	}

	internal static FeatureExecutionOptions NonInteractive(string invocationSource)
	{
		return new FeatureExecutionOptions
		{
			InvocationSource = invocationSource,
			SuppressUserDialogs = true
		};
	}
}
