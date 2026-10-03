using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using DocumentRepository.Models.Images;
using DocumentRepository.Services.Hosting;
using Microsoft.Office.Interop.Word;

namespace DocumentRepository.Services.Formatting.Images;

internal sealed class ImageExecutionAnchorSet : IDisposable
{
	private sealed class Record
	{
		public ImageFormattingTarget Target;

		public Microsoft.Office.Interop.Word.Range LiveRange;
	}

	private readonly List<Record> records = new List<Record>();

	private bool disposed;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static ImageExecutionAnchorSet Create(Document document, IList<ImageFormattingTarget> targets)
	{
		if (document != null)
		{
			if (targets == null)
			{
				throw new ArgumentNullException("targets");
			}
			HostThreadRuntime.AssertAccess("ImageExecutionAnchorSet.Create");
			ImageExecutionAnchorSet imageExecutionAnchorSet = new ImageExecutionAnchorSet();
			Microsoft.Office.Interop.Word.Range value = null;
			try
			{
				value = document.Content;
				foreach (ImageFormattingTarget target in targets)
				{
					if (target != null)
					{
						int num = Math.Max(value.Start, Math.Min(target.AnchorStart, value.End));
						List<Record> list = imageExecutionAnchorSet.records;
						Record obj = new Record
						{
							Target = target
						};
						object Start = num;
						object End = num;
						obj.LiveRange = document.Range(ref Start, ref End);
						list.Add(obj);
					}
				}
				return imageExecutionAnchorSet;
			}
			catch
			{
				imageExecutionAnchorSet.Dispose();
				throw;
			}
			finally
			{
				ComObjectRelease.Release(ref value, "ImageExecutionAnchorSet.content");
			}
		}
		throw new ArgumentNullException("document");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public int GetCurrentStart(ImageFormattingTarget target)
	{
		if (disposed)
		{
			throw new ObjectDisposedException("ImageExecutionAnchorSet");
		}
		foreach (Record record in records)
		{
			if (record.Target != target)
			{
				continue;
			}
			if (record.LiveRange != null)
			{
				return record.LiveRange.Start;
			}
			throw new InvalidOperationException("图片执行锚点已经失效。");
		}
		throw new InvalidOperationException("找不到计划图片的执行锚点。");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public void Dispose()
	{
		if (!disposed)
		{
			disposed = true;
			for (int num = records.Count - 1; num >= 0; num--)
			{
				ComObjectRelease.Release(ref records[num].LiveRange, "ImageExecutionAnchorSet.range");
			}
			records.Clear();
		}
	}

	void IDisposable.Dispose()
	{
		this.Dispose();
	}
}
