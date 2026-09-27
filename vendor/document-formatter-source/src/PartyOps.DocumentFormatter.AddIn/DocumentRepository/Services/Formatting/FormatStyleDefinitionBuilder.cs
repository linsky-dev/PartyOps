namespace DocumentRepository.Services.Formatting;

internal static class FormatStyleDefinitionBuilder
{
	public static FormatTextStyleDefinition Build(FormatConfig config)
	{
		return new FormatTextStyleDefinition(config);
	}
}
