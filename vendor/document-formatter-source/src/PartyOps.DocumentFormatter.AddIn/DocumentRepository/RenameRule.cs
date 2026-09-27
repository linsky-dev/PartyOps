using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace DocumentRepository;

public class RenameRule
{
	public string Id { get; set; }

	public string Name { get; set; }

	public List<RenameRulePart> Parts { get; set; }

	public string DateFormat { get; set; }

	public string RenameMode { get; set; }

	public string SavePathMode { get; set; }

	public string CustomSaveDirectory { get; set; }

	public string RotateWords { get; set; }

	public int RotateIndex { get; set; }

	public bool ResetRotateOnStartup { get; set; }

	[MethodImpl(MethodImplOptions.NoInlining)]
	public RenameRule()
	{
		Id = Guid.NewGuid().ToString("N");
		Name = "新规则";
		DateFormat = "yyyy.MM.dd";
		RenameMode = "online";
		SavePathMode = "source";
		CustomSaveDirectory = "";
		RotateWords = "";
		RotateIndex = 0;
		ResetRotateOnStartup = false;
		Parts = new List<RenameRulePart>();
	}

	public RenameRule Clone()
	{
		return new RenameRule
		{
			Id = Id,
			Name = Name,
			Parts = CloneParts(Parts),
			DateFormat = DateFormat,
			RenameMode = RenameMode,
			SavePathMode = SavePathMode,
			CustomSaveDirectory = CustomSaveDirectory,
			RotateWords = RotateWords,
			RotateIndex = RotateIndex,
			ResetRotateOnStartup = ResetRotateOnStartup
		};
	}

	private static List<RenameRulePart> CloneParts(List<RenameRulePart> parts)
	{
		List<RenameRulePart> list = new List<RenameRulePart>();
		if (parts == null)
		{
			return list;
		}
		foreach (RenameRulePart part in parts)
		{
			if (part != null)
			{
				list.Add(part.Clone());
			}
		}
		return list;
	}
}
