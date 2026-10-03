using Microsoft.Office.Interop.Word;

namespace DocumentRepository.Services.Hosting;

public sealed class SelectionCheckpoint
{
	internal Range LiveRange { get; set; }

	internal int Start { get; }

	internal int End { get; }

	internal WdStoryType StoryType { get; }

	internal bool HasStoryType { get; }

	internal SelectionCheckpoint(Range liveRange, int start, int end, WdStoryType storyType, bool hasStoryType)
	{
		LiveRange = liveRange;
		Start = start;
		End = end;
		StoryType = storyType;
		HasStoryType = hasStoryType;
	}
}
