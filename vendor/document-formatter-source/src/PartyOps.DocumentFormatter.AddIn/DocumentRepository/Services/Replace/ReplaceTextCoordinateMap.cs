using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using DocumentRepository.Models.Replace;

namespace DocumentRepository.Services.Replace;

public sealed class ReplaceTextCoordinateMap
{
	private readonly string text;

	private readonly bool collapseCellMarkers;

	private readonly int[] cellMarkerOffsets;

	private ReplaceTextCoordinateMap(string text, bool collapseCellMarkers, int[] cellMarkerOffsets)
	{
		this.text = text;
		this.collapseCellMarkers = collapseCellMarkers;
		this.cellMarkerOffsets = cellMarkerOffsets;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static ReplaceTextCoordinateMap Create(string text, int rangeLength)
	{
		text = text ?? string.Empty;
		if (rangeLength >= 0)
		{
			List<int> list = new List<int>();
			for (int i = 0; i < text.Length; i++)
			{
				if (IsCellMarkerSecondCharacter(text, i))
				{
					list.Add(i);
				}
			}
			int[] array = list.ToArray();
			if (rangeLength == text.Length)
			{
				return new ReplaceTextCoordinateMap(text, collapseCellMarkers: false, array);
			}
			if (array.Length != 0 && rangeLength == text.Length - array.Length)
			{
				return new ReplaceTextCoordinateMap(text, collapseCellMarkers: true, array);
			}
			throw ReplaceOperationException.Create(ReplaceFailureReasonCode.TableBoundaryUnsafe, ReplaceFailureStage.ApplyRules);
		}
		throw new ArgumentOutOfRangeException("rangeLength");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public int ToRangeOffset(int textOffset)
	{
		if (textOffset >= 0 && textOffset <= text.Length)
		{
			if (collapseCellMarkers && IsBoundaryInsideCellMarker(textOffset))
			{
				throw ReplaceOperationException.Create(ReplaceFailureReasonCode.TableBoundaryUnsafe, ReplaceFailureStage.ApplyRules);
			}
			if (!collapseCellMarkers)
			{
				return textOffset;
			}
			return textOffset - CountMarkersBefore(textOffset);
		}
		throw new ArgumentOutOfRangeException("textOffset");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public void AssertSafeTarget(int start, int end)
	{
		if (start >= 0 && end >= start && end <= text.Length)
		{
			if (collapseCellMarkers && (IsBoundaryInsideCellMarker(start) || IsBoundaryInsideCellMarker(end)))
			{
				throw ReplaceOperationException.Create(ReplaceFailureReasonCode.TableBoundaryUnsafe, ReplaceFailureStage.ApplyRules);
			}
			for (int i = 0; i < cellMarkerOffsets.Length; i++)
			{
				int num = cellMarkerOffsets[i];
				if (start < num + 1 && end > num - 1)
				{
					throw ReplaceOperationException.Create(ReplaceFailureReasonCode.TableBoundaryUnsafe, ReplaceFailureStage.ApplyRules);
				}
			}
			return;
		}
		throw new ArgumentOutOfRangeException("start");
	}

	private bool IsBoundaryInsideCellMarker(int offset)
	{
		return Array.BinarySearch(cellMarkerOffsets, offset) >= 0;
	}

	private int CountMarkersBefore(int offset)
	{
		int num = Array.BinarySearch(cellMarkerOffsets, offset);
		if (num < 0)
		{
			return ~num;
		}
		return num;
	}

	private static bool IsCellMarkerSecondCharacter(string value, int index)
	{
		if (index > 0 && index < value.Length && value[index] == '\a')
		{
			return value[index - 1] == '\r';
		}
		return false;
	}
}
