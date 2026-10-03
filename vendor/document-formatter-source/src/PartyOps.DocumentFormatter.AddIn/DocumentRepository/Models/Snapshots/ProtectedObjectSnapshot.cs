namespace DocumentRepository.Models.Snapshots;

public sealed class ProtectedObjectSnapshot
{
	public int Fields { get; set; }

	public int Hyperlinks { get; set; }

	public int Bookmarks { get; set; }

	public int Comments { get; set; }

	public int ContentControls { get; set; }

	public int InlineShapes { get; set; }

	public int Shapes { get; set; }

	public int Tables { get; set; }

	public ProtectedObjectSnapshot DeepCopy()
	{
		return new ProtectedObjectSnapshot
		{
			Fields = Fields,
			Hyperlinks = Hyperlinks,
			Bookmarks = Bookmarks,
			Comments = Comments,
			ContentControls = ContentControls,
			InlineShapes = InlineShapes,
			Shapes = Shapes,
			Tables = Tables
		};
	}
}
