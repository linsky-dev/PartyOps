using Microsoft.Office.Interop.Word;

namespace DocumentRepository.Services.Detection.Images;

public static class ImageDetector
{
	public static bool HasImages(Document doc)
	{
		if (doc == null)
		{
			return false;
		}
		try
		{
			if (doc.InlineShapes != null && doc.InlineShapes.Count > 0)
			{
				return true;
			}
		}
		catch
		{
			return true;
		}
		try
		{
			return doc.Shapes != null && doc.Shapes.Count > 0;
		}
		catch
		{
			return true;
		}
	}

	public static bool HasImages(Range range)
	{
		if (range == null)
		{
			return false;
		}
		try
		{
			if (range.InlineShapes != null && range.InlineShapes.Count > 0)
			{
				return true;
			}
		}
		catch
		{
			return true;
		}
		try
		{
			return range.ShapeRange != null && range.ShapeRange.Count > 0;
		}
		catch
		{
			return true;
		}
	}

	public static bool HasEligiblePictures(Document doc)
	{
		if (doc != null)
		{
			return ImageSnapshotService.Capture(doc).EligibleCount > 0;
		}
		return false;
	}

	public static bool HasEligiblePictures(Range range)
	{
		if (range != null)
		{
			return ImageSnapshotService.Capture(range).EligibleCount > 0;
		}
		return false;
	}
}
