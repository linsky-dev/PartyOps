namespace DocumentRepository.Services.Formatting.PageNumbers;

internal sealed class PageNumberFontSlots
{
	internal string EastAsianFontName { get; private set; }

	internal string AsciiFontName { get; private set; }

	internal PageNumberFontSlots(string eastAsianFontName, string asciiFontName)
	{
		EastAsianFontName = eastAsianFontName;
		AsciiFontName = asciiFontName;
	}
}
