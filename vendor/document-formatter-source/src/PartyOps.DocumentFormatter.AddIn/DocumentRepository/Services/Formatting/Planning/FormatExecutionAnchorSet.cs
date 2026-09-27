using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using DocumentRepository.Models;
using DocumentRepository.Services.Hosting;
using DocumentRepository.Services.Interop;
using DocumentRepository.Services.Logging;
using DocumentRepository.Services.Snapshots;
using Microsoft.Office.Interop.Word;

namespace DocumentRepository.Services.Formatting.Planning;

internal sealed class FormatExecutionAnchorSet : IDisposable
{
	private sealed class AnchorRecord
	{
		public int OriginalStart;

		public DocumentElement Element;

		public Microsoft.Office.Interop.Word.Range LiveRange;
	}

	private sealed class SignatureAnchorPair
	{
		public SignatureBlock Block;

		public readonly List<SignatureLineAnchor> Signatures = new List<SignatureLineAnchor>();

		public AnchorRecord Date;
	}

	private sealed class SignatureLineAnchor
	{
		public SignatureLine Line;

		public AnchorRecord Anchor;
	}

	private readonly Document document;

	private readonly List<AnchorRecord> records = new List<AnchorRecord>();

	private readonly List<SignatureAnchorPair> signaturePairs = new List<SignatureAnchorPair>();

	private bool disposed;

	private FormatExecutionAnchorSet(Document document)
	{
		this.document = document;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static FormatExecutionAnchorSet Create(Document document, DocumentElementList elements, IList<SignatureBlock> signatureBlocks, string planId)
	{
		if (document != null)
		{
			if (elements == null || elements.Items == null)
			{
				throw new InvalidOperationException("创建排版锚点缺少一次性识别结果。");
			}
			FormatExecutionAnchorSet formatExecutionAnchorSet = new FormatExecutionAnchorSet(document);
			Microsoft.Office.Interop.Word.Range value = null;
			try
			{
				value = document.Content;
				int end = value.End;
				for (int i = 0; i < elements.Items.Count; i++)
				{
					DocumentElement documentElement = elements.Items[i];
					if (documentElement != null && !documentElement.IsEmpty && documentElement.RangeEnd > documentElement.RangeStart)
					{
						int num = Math.Max(value.Start, Math.Min(documentElement.RangeStart, end));
						int end2 = Math.Max(num, Math.Min(documentElement.RangeEnd, end));
						int num2 = WordDocumentBoundary.ResolveAnchorPosition(num, end2, value.Start, end);
						List<AnchorRecord> list = formatExecutionAnchorSet.records;
						AnchorRecord obj = new AnchorRecord
						{
							OriginalStart = documentElement.RangeStart,
							Element = documentElement
						};
						object Start = num2;
						object End = num2;
						obj.LiveRange = document.Range(ref Start, ref End);
						list.Add(obj);
					}
				}
				if (signatureBlocks != null)
				{
					foreach (SignatureBlock signatureBlock in signatureBlocks)
					{
						if (signatureBlock == null)
						{
							continue;
						}
						SignatureAnchorPair signatureAnchorPair = new SignatureAnchorPair
						{
							Block = signatureBlock,
							Date = formatExecutionAnchorSet.FindByOriginalStart(signatureBlock.DateRangeStart)
						};
						if (signatureBlock.SignatureLines != null && signatureBlock.SignatureLines.Count > 0)
						{
							foreach (SignatureLine signatureLine2 in signatureBlock.SignatureLines)
							{
								if (signatureLine2 != null)
								{
									signatureAnchorPair.Signatures.Add(new SignatureLineAnchor
									{
										Line = signatureLine2,
										Anchor = formatExecutionAnchorSet.FindByOriginalStart(signatureLine2.RangeStart)
									});
								}
							}
						}
						else
						{
							SignatureLine signatureLine = new SignatureLine
							{
								ParagraphIndex = signatureBlock.SignatureParagraphIndex,
								RangeStart = signatureBlock.SignatureRangeStart,
								RangeEnd = signatureBlock.SignatureRangeEnd,
								Text = signatureBlock.SignatureText
							};
							signatureBlock.SetSignatureLines(new SignatureLine[1] { signatureLine });
							signatureAnchorPair.Signatures.Add(new SignatureLineAnchor
							{
								Line = signatureLine,
								Anchor = formatExecutionAnchorSet.FindByOriginalStart(signatureLine.RangeStart)
							});
						}
						formatExecutionAnchorSet.signaturePairs.Add(signatureAnchorPair);
					}
				}
			}
			finally
			{
				ComObjectRelease.Release(ref value, "FormatAnchors.Create.Content");
			}
			formatExecutionAnchorSet.RefreshPositions();
			return formatExecutionAnchorSet;
		}
		throw new ArgumentNullException("document");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public void RefreshPositions()
	{
		if (!disposed)
		{
			List<Tuple<int, int>> list = new List<Tuple<int, int>>();
			ParagraphTextSnapshot paragraphTextSnapshot = ParagraphTextSnapshot.Capture(document);
			if (!paragraphTextSnapshot.IsReliable)
			{
				LogService.Warn("FormatExecutionAnchorSet.RefreshPositions text snapshot unavailable, fallback: " + paragraphTextSnapshot.FailureReason);
				Paragraphs value = null;
				try
				{
					value = document.Paragraphs;
					int num = value?.Count ?? 0;
					for (int i = 1; i <= num; i++)
					{
						Paragraph value2 = null;
						Microsoft.Office.Interop.Word.Range value3 = null;
						try
						{
							value2 = value[i];
							value3 = value2.Range;
							list.Add(Tuple.Create(value3.Start, value3.End));
						}
						finally
						{
							ComObjectRelease.Release(ref value3, "FormatAnchors.Refresh.ParagraphRange");
							ComObjectRelease.Release(ref value2, "FormatAnchors.Refresh.Paragraph");
						}
					}
				}
				finally
				{
					if (value != null)
					{
						ComObjectRelease.Release(ref value, "FormatAnchors.Refresh.Paragraphs");
					}
				}
			}
			else
			{
				for (int j = 0; j < paragraphTextSnapshot.Paragraphs.Count; j++)
				{
					WordParagraphTextMap.Entry entry = paragraphTextSnapshot.Paragraphs[j];
					list.Add(Tuple.Create(entry.RangeStart, entry.RangeEnd));
				}
			}
			foreach (AnchorRecord record in records)
			{
				if (record.LiveRange == null)
				{
					throw new InvalidOperationException("排版执行 Range 锚点已失效。");
				}
				int num2 = FindParagraphIndex(list, record.LiveRange.Start);
				Tuple<int, int> tuple = list[num2 - 1];
				record.Element.RangeStart = tuple.Item1;
				record.Element.RangeEnd = tuple.Item2;
				record.Element.ParagraphIndex = num2;
			}
			{
				foreach (SignatureAnchorPair signaturePair in signaturePairs)
				{
					SignatureLine signatureLine = null;
					SignatureLine signatureLine2 = null;
					foreach (SignatureLineAnchor signature in signaturePair.Signatures)
					{
						if (signature.Anchor == null)
						{
							throw new InvalidOperationException("联合落款署名锚点已失效。");
						}
						signature.Line.RangeStart = signature.Anchor.Element.RangeStart;
						signature.Line.RangeEnd = signature.Anchor.Element.RangeEnd;
						signature.Line.ParagraphIndex = signature.Anchor.Element.ParagraphIndex;
						if (signatureLine == null)
						{
							signatureLine = signature.Line;
						}
						signatureLine2 = signature.Line;
					}
					if (signatureLine != null)
					{
						signaturePair.Block.SignatureRangeStart = signatureLine.RangeStart;
						signaturePair.Block.SignatureRangeEnd = signatureLine2.RangeEnd;
						signaturePair.Block.SignatureParagraphIndex = signatureLine.ParagraphIndex;
						signaturePair.Block.SignatureText = signatureLine.Text;
					}
					if (signaturePair.Date != null)
					{
						signaturePair.Block.DateRangeStart = signaturePair.Date.Element.RangeStart;
						signaturePair.Block.DateRangeEnd = signaturePair.Date.Element.RangeEnd;
						signaturePair.Block.DateParagraphIndex = signaturePair.Date.Element.ParagraphIndex;
					}
				}
				return;
			}
		}
		throw new ObjectDisposedException("FormatExecutionAnchorSet");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public void Dispose()
	{
		if (!disposed)
		{
			disposed = true;
			for (int num = records.Count - 1; num >= 0; num--)
			{
				ComObjectRelease.Release(ref records[num].LiveRange, "FormatAnchors.Dispose.Range");
			}
			records.Clear();
			signaturePairs.Clear();
		}
	}

	void IDisposable.Dispose()
	{
		this.Dispose();
	}

	private AnchorRecord FindByOriginalStart(int start)
	{
		foreach (AnchorRecord record in records)
		{
			if (record.OriginalStart == start)
			{
				return record;
			}
		}
		return null;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int FindParagraphIndex(IList<Tuple<int, int>> bounds, int position)
	{
		for (int i = 0; i < bounds.Count; i++)
		{
			Tuple<int, int> tuple = bounds[i];
			if (position >= tuple.Item1 && position < tuple.Item2)
			{
				return i + 1;
			}
		}
		throw new InvalidOperationException("无法把排版锚点映射到当前段落：" + position);
	}
}
