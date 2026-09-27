using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Xps.Packaging;
using DocumentRepository.Models.Conversion;
using DocumentRepository.Models.Tasks;
using DocumentRepository.Services.FileSafety;
using DocumentRepository.Services.Hosting;
using DocumentRepository.Services.Logging;
using Microsoft.Office.Interop.Word;

namespace DocumentRepository.Services.Conversion;

public static class ImageConversionService
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static List<string> ExportImages(Document document, string sourcePath, string outputFolder, ConvertOptions options, IList<int> pages, TaskRuntimeContext task, Func<string, ConvertConflictDecision> conflictResolver, out bool usedCompatibilityFallback, out bool longImageResolutionAdjusted)
	{
		usedCompatibilityFallback = false;
		longImageResolutionAdjusted = false;
		if (document == null)
		{
			throw new InvalidOperationException("当前文档无效。");
		}
		if (task == null)
		{
			throw new InvalidOperationException("图片转换缺少统一任务运行上下文。");
		}
		if (pages == null || pages.Count == 0)
		{
			throw new InvalidOperationException("未选择要转换的页面。");
		}
		options = ConvertSettingsService.Normalize(options);
		Directory.CreateDirectory(outputFolder);
		if (IsWpsHost(document))
		{
			usedCompatibilityFallback = true;
			LogService.Warn("ImageConversionService WPS direct compatibility renderer.");
			try
			{
				return ExportImagesFromRangeMetafiles(document, sourcePath, outputFolder, options, pages, task, conflictResolver, ref longImageResolutionAdjusted);
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (Exception ex2)
			{
				LogService.Error("ImageConversionService.ExportImages.WpsCompatibility", ex2);
				throw new InvalidOperationException("当前 WPS 暂时无法生成页面图片，请先尝试转换为 PDF。", ex2);
			}
		}
		string text = null;
		try
		{
			text = ExportWholeDocumentToXps(document);
			task.Artifacts.RegisterFile(text, existedBefore: false);
			WaitForExclusiveRead(text, TimeSpan.FromSeconds(8.0));
			if (options.ImageExportMode == ImageExportMode.LongImage)
			{
				return ExportLongImageFromXps(text, sourcePath, outputFolder, options, pages, task, conflictResolver, ref longImageResolutionAdjusted);
			}
			return ExportSingleImagesFromXps(text, sourcePath, outputFolder, options, pages, task, conflictResolver);
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception ex4)
		{
			LogService.Error("ImageConversionService.ExportImages.FullPageRender", ex4);
			try
			{
				usedCompatibilityFallback = true;
				LogService.Warn("ImageConversionService using range metafile compatibility renderer.");
				return ExportImagesFromRangeMetafiles(document, sourcePath, outputFolder, options, pages, task, conflictResolver, ref longImageResolutionAdjusted);
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (Exception ex6)
			{
				LogService.Error("ImageConversionService.ExportImages.RangeMetafileFallback", ex6);
				throw new InvalidOperationException("当前 Word/WPS 暂时无法生成页面图片，请先尝试转换为 PDF。", ex6);
			}
		}
		finally
		{
			try
			{
				if (!string.IsNullOrWhiteSpace(text) && File.Exists(text))
				{
					File.Delete(text);
				}
			}
			catch (Exception ex7)
			{
				LogService.Warn("ImageConversionService.DeleteTempXps", ex7);
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static List<string> ExportImagesFromRangeMetafiles(Document document, string sourcePath, string outputFolder, ConvertOptions options, IList<int> pages, TaskRuntimeContext task, Func<string, ConvertConflictDecision> conflictResolver, ref bool longImageResolutionAdjusted)
	{
		string text = Path.Combine(Path.GetTempPath(), "partyops_page_render_" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(text);
		task.Artifacts.RegisterDirectory(text, existedBefore: false);
		List<string> rendered = new List<string>();
		try
		{
			bool flag = options.ImageExportMode == ImageExportMode.LongImage;
			int num = options.ImageDpi;
			List<ImagePageDimensions> list = new List<ImagePageDimensions>();
			ImageConversionBudget budget;
			while (true)
			{
				list.Clear();
				rendered.Clear();
				for (int i = 0; i < pages.Count; i++)
				{
					int pageNumber = pages[i];
					task.Cancellation.ThrowIfCancellationRequested("准备第 " + pageNumber + " 页兼容图片前");
					ReportImageProgress(task, i + 1, pages.Count + 1, "正在生成第 " + pageNumber + " 页图片");
					string text2 = Path.Combine(text, "page_" + i.ToString("000") + ".png");
					ImagePageDimensions item = RenderPageRange(document, pageNumber, num, text2);
					list.Add(item);
					rendered.Add(text2);
				}
				budget = ImageConversionBudgetService.Evaluate(list, flag, Environment.Is64BitProcess);
				if (budget.Allowed)
				{
					break;
				}
				if (!flag || pages.Count <= 1 || num <= 48)
				{
					ImageConversionBudgetService.EnsureAllowed(budget);
				}
				ImageConversionBudgetService.EnsureAllowed(ImageConversionBudgetService.FitLongImage(list, Environment.Is64BitProcess, out var scale));
				int num2 = Math.Max(48, (int)Math.Floor((double)num * scale));
				if (num2 >= num)
				{
					num2 = num - 1;
				}
				if (num2 < 48)
				{
					ImageConversionBudgetService.EnsureAllowed(budget);
				}
				foreach (string item2 in rendered)
				{
					try
					{
						if (File.Exists(item2))
						{
							File.Delete(item2);
						}
					}
					catch (Exception ex)
					{
						LogService.Warn("ImageConversionService.DeleteAdaptiveRender, exception=" + ex.GetType().Name);
					}
				}
				longImageResolutionAdjusted = true;
				LogService.Warn("ImageConversionService adaptive long-image dpi requested=" + options.ImageDpi + ", effective=" + num2 + ", pages=" + pages.Count);
				num = num2;
			}
			ImageConversionBudgetService.EnsureAllowed(budget);
			string extension = GetExtension(options.ImageFormat);
			if (options.ImageExportMode == ImageExportMode.LongImage)
			{
				string targetPath = Path.Combine(outputFolder, Path.GetFileNameWithoutExtension(sourcePath) + "_长图" + extension);
				targetPath = OutputPathService.ResolveConflict(targetPath, options.SameNamePolicy, conflictResolver);
				if (!string.IsNullOrWhiteSpace(targetPath))
				{
					task.Artifacts.RegisterFile(targetPath, File.Exists(targetPath));
					SafeOutputTransaction.ProduceAndCommit(targetPath, delegate(string temp)
					{
						if (rendered.Count == 1)
						{
							Bitmap val = new Bitmap(rendered[0]);
							try
							{
								SaveBitmap(val, temp, options.ImageFormat);
								return;
							}
							finally
							{
								((IDisposable)val)?.Dispose();
							}
						}
						Bitmap val2 = CombineVertical(rendered, budget);
						try
						{
							SaveBitmap(val2, temp, options.ImageFormat);
						}
						finally
						{
							((IDisposable)val2)?.Dispose();
						}
					});
					return new List<string> { targetPath };
				}
				throw new OperationCanceledException("用户取消了转换。");
			}
			List<string> list2 = new List<string>();
			for (int num3 = 0; num3 < rendered.Count; num3++)
			{
				int num4 = pages[num3];
				string targetPath2 = Path.Combine(outputFolder, Path.GetFileNameWithoutExtension(sourcePath) + "_第" + num4.ToString("000") + "页" + extension);
				targetPath2 = OutputPathService.ResolveConflict(targetPath2, options.SameNamePolicy, conflictResolver);
				if (!string.IsNullOrWhiteSpace(targetPath2))
				{
					task.Artifacts.RegisterFile(targetPath2, File.Exists(targetPath2));
					string renderedFile = rendered[num3];
					SafeOutputTransaction.ProduceAndCommit(targetPath2, delegate(string temp)
					{
						Bitmap val = new Bitmap(renderedFile);
						try
						{
							SaveBitmap(val, temp, options.ImageFormat);
						}
						finally
						{
							((IDisposable)val)?.Dispose();
						}
					});
					list2.Add(targetPath2);
					continue;
				}
				throw new OperationCanceledException("用户取消了转换。");
			}
			return list2;
		}
		finally
		{
			try
			{
				if (Directory.Exists(text))
				{
					Directory.Delete(text, recursive: true);
				}
			}
			catch (Exception ex2)
			{
				LogService.Warn("ImageConversionService.DeleteRangeRenderTemp, exception=" + ex2.GetType().Name);
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static ImagePageDimensions RenderPageRange(Document document, int pageNumber, int dpi, string targetPath)
	{
		Microsoft.Office.Interop.Word.Range value = null;
		Microsoft.Office.Interop.Word.Range value2 = null;
		Microsoft.Office.Interop.Word.Range value3 = null;
		Sections value4 = null;
		Microsoft.Office.Interop.Word.Section value5 = null;
		PageSetup value6 = null;
		try
		{
			object What = WdGoToItem.wdGoToPage;
			object Which = WdGoToDirection.wdGoToFirst;
			object Count = pageNumber;
			object Name = Type.Missing;
			value = document.GoTo(ref What, ref Which, ref Count, ref Name);
			Name = Type.Missing;
			int num = document.ComputeStatistics(WdStatistic.wdStatisticPages, ref Name);
			int num2 = document.Content.End;
			if (pageNumber < num)
			{
				Name = WdGoToItem.wdGoToPage;
				Count = WdGoToDirection.wdGoToFirst;
				Which = pageNumber + 1;
				What = Type.Missing;
				value2 = document.GoTo(ref Name, ref Count, ref Which, ref What);
				num2 = value2.Start;
			}
			What = value.Start;
			Which = num2;
			value3 = document.Range(ref What, ref Which);
			if (value3.EnhMetaFileBits is byte[] array && array.Length != 0)
			{
				value4 = value3.Sections;
				value5 = ((value4.Count > 0) ? value4[1] : null);
				value6 = ((value5 == null) ? document.PageSetup : value5.PageSetup);
				int num3 = Math.Max(1, (int)Math.Round((double)value6.PageWidth / 72.0 * (double)dpi));
				int num4 = Math.Max(1, (int)Math.Round((double)value6.PageHeight / 72.0 * (double)dpi));
				int num5 = Math.Max(0, (int)Math.Round((double)value6.LeftMargin / 72.0 * (double)dpi));
				int num6 = Math.Max(0, (int)Math.Round((double)value6.TopMargin / 72.0 * (double)dpi));
				int width = Math.Max(1, num3 - num5 - Math.Max(0, (int)Math.Round((double)value6.RightMargin / 72.0 * (double)dpi)));
				int height = Math.Max(1, num4 - num6 - Math.Max(0, (int)Math.Round((double)value6.BottomMargin / 72.0 * (double)dpi)));
				using (MemoryStream memoryStream = new MemoryStream(array))
				{
					Metafile val = new Metafile((Stream)memoryStream);
					try
					{
						Bitmap val2 = new Bitmap(num3, num4, (System.Drawing.Imaging.PixelFormat)2498570);
						try
						{
							val2.SetResolution((float)dpi, (float)dpi);
							Graphics val3 = Graphics.FromImage((Image)(object)val2);
							try
							{
								val3.Clear(System.Drawing.Color.White);
								val3.DrawImage((Image)(object)val, new Rectangle(num5, num6, width, height));
							}
							finally
							{
								((IDisposable)val3)?.Dispose();
							}
							((Image)val2).Save(targetPath, ImageFormat.Png);
						}
						finally
						{
							((IDisposable)val2)?.Dispose();
						}
					}
					finally
					{
						((IDisposable)val)?.Dispose();
					}
				}
				return new ImagePageDimensions
				{
					Width = num3,
					Height = num4
				};
			}
			throw new InvalidOperationException("宿主没有返回页面图像数据。");
		}
		finally
		{
			ComObjectRelease.Release(ref value6, "ImageConversionService.RenderPageRange.PageSetup");
			ComObjectRelease.Release(ref value5, "ImageConversionService.RenderPageRange.Section");
			ComObjectRelease.Release(ref value4, "ImageConversionService.RenderPageRange.Sections");
			ComObjectRelease.Release(ref value3, "ImageConversionService.RenderPageRange.Range");
			ComObjectRelease.Release(ref value2, "ImageConversionService.RenderPageRange.Next");
			ComObjectRelease.Release(ref value, "ImageConversionService.RenderPageRange.Start");
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static List<string> ExportSingleImagesFromXps(string xpsPath, string sourcePath, string outputFolder, ConvertOptions options, IList<int> pages, TaskRuntimeContext task, Func<string, ConvertConflictDecision> conflictResolver)
	{
		string extension = GetExtension(options.ImageFormat);
		string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(sourcePath);
		List<string> list = new List<string>();
		XpsDocument val = new XpsDocument(xpsPath, FileAccess.Read);
		try
		{
			DocumentPaginator paginator = val.GetFixedDocumentSequence().DocumentPaginator;
			EnsurePageCount(paginator);
			ImageConversionBudgetService.EnsureAllowed(BuildBudget(paginator, pages, options.ImageDpi, longImage: false));
			for (int i = 0; i < pages.Count; i++)
			{
				int page = pages[i];
				task.Cancellation.ThrowIfCancellationRequested("导出第 " + page + " 页图片前");
				ReportImageProgress(task, i + 1, pages.Count, "正在生成第 " + page + " 页图片");
				string targetPath = Path.Combine(outputFolder, fileNameWithoutExtension + "_第" + page.ToString("000") + "页" + extension);
				targetPath = OutputPathService.ResolveConflict(targetPath, options.SameNamePolicy, conflictResolver);
				if (string.IsNullOrWhiteSpace(targetPath))
				{
					throw new OperationCanceledException("用户取消了转换。");
				}
				task.Artifacts.RegisterFile(targetPath, File.Exists(targetPath));
				SafeOutputTransaction.ProduceAndCommit(targetPath, delegate(string temp)
				{
					SaveXpsPage(paginator, page, options.ImageDpi, temp, options.ImageFormat);
				});
				list.Add(targetPath);
			}
			return list;
		}
		finally
		{
			((IDisposable)val)?.Dispose();
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static List<string> ExportLongImageFromXps(string xpsPath, string sourcePath, string outputFolder, ConvertOptions options, IList<int> pages, TaskRuntimeContext task, Func<string, ConvertConflictDecision> conflictResolver, ref bool longImageResolutionAdjusted)
	{
		string text = Path.Combine(Path.GetTempPath(), "partyops_long_image_" + Guid.NewGuid().ToString("N"));
		task.Artifacts.RegisterDirectory(text, existedBefore: false);
		Directory.CreateDirectory(text);
		List<string> tempFiles = new List<string>();
		try
		{
			XpsDocument val = new XpsDocument(xpsPath, FileAccess.Read);
			try
			{
				DocumentPaginator documentPaginator = val.GetFixedDocumentSequence().DocumentPaginator;
				EnsurePageCount(documentPaginator);
				int num = options.ImageDpi;
				ImageConversionBudget imageConversionBudget = BuildBudget(documentPaginator, pages, num, longImage: true);
				while (!imageConversionBudget.Allowed && pages.Count > 1 && num > 48)
				{
					ImageConversionBudgetService.EnsureAllowed(ImageConversionBudgetService.FitLongImage(imageConversionBudget.Pages, Environment.Is64BitProcess, out var scale));
					int num2 = Math.Max(48, (int)Math.Floor((double)num * scale));
					if (num2 >= num)
					{
						num2 = num - 1;
					}
					if (num2 < 48)
					{
						break;
					}
					num = num2;
					longImageResolutionAdjusted = true;
					imageConversionBudget = BuildBudget(documentPaginator, pages, num, longImage: true);
				}
				ImageConversionBudgetService.EnsureAllowed(imageConversionBudget);
				if (longImageResolutionAdjusted)
				{
					LogService.Warn("ImageConversionService adaptive long-image dpi requested=" + options.ImageDpi + ", effective=" + num + ", pages=" + pages.Count);
				}
				for (int i = 0; i < pages.Count; i++)
				{
					int pageNumber = pages[i];
					task.Cancellation.ThrowIfCancellationRequested("准备第 " + pageNumber + " 页长图素材前");
					ReportImageProgress(task, i + 1, pages.Count + 1, "正在准备第 " + pageNumber + " 页长图素材");
					string text2 = Path.Combine(text, "page_" + i.ToString("000") + ".png");
					SaveXpsPage(documentPaginator, pageNumber, num, text2, ImageFileFormat.Png);
					tempFiles.Add(text2);
				}
			}
			finally
			{
				((IDisposable)val)?.Dispose();
			}
			task.Cancellation.ThrowIfCancellationRequested("合成长图前");
			ReportImageProgress(task, pages.Count + 1, pages.Count + 1, "正在合成长图");
			string extension = GetExtension(options.ImageFormat);
			string targetPath = Path.Combine(outputFolder, Path.GetFileNameWithoutExtension(sourcePath) + "_长图" + extension);
			targetPath = OutputPathService.ResolveConflict(targetPath, options.SameNamePolicy, conflictResolver);
			if (string.IsNullOrWhiteSpace(targetPath))
			{
				throw new OperationCanceledException("用户取消了转换。");
			}
			task.Artifacts.RegisterFile(targetPath, File.Exists(targetPath));
			SafeOutputTransaction.ProduceAndCommit(targetPath, delegate(string temp)
			{
				ImageConversionBudget budget = ReadRenderedPageBudget(tempFiles);
				ImageConversionBudgetService.EnsureAllowed(budget);
				if (tempFiles.Count == 1)
				{
					Bitmap val2 = new Bitmap(tempFiles[0]);
					try
					{
						SaveBitmap(val2, temp, options.ImageFormat);
						return;
					}
					finally
					{
						((IDisposable)val2)?.Dispose();
					}
				}
				Bitmap val3 = CombineVertical(tempFiles, budget);
				try
				{
					SaveBitmap(val3, temp, options.ImageFormat);
				}
				finally
				{
					((IDisposable)val3)?.Dispose();
				}
			});
			return new List<string> { targetPath };
		}
		finally
		{
			try
			{
				if (Directory.Exists(text))
				{
					Directory.Delete(text, recursive: true);
				}
			}
			catch (Exception ex)
			{
				LogService.Warn("ImageConversionService.DeleteLongImageTemp", ex);
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ReportImageProgress(TaskRuntimeContext task, int current, int total, string message)
	{
		if (task == null)
		{
			throw new ArgumentNullException("task");
		}
		total = Math.Max(1, total);
		current = Math.Max(0, Math.Min(current, total));
		int currentStep = 30 + current * 60 / total;
		task.Progress.Report(TaskProgressInfo.Create("一键转换", currentStep, 100, message, "导出图片"));
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string ExportWholeDocumentToXps(Document document)
	{
		string text = Path.Combine(Path.GetTempPath(), "partyops_convert_" + Guid.NewGuid().ToString("N") + ".xps");
		DocumentExportService.ExportXps(document, text);
		return text;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void WaitForExclusiveRead(string path, TimeSpan timeout)
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		Exception innerException = null;
		while (stopwatch.Elapsed < timeout)
		{
			try
			{
				using FileStream fileStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
				if (fileStream.Length > 0)
				{
					return;
				}
			}
			catch (IOException ex)
			{
				innerException = ex;
			}
			catch (UnauthorizedAccessException ex2)
			{
				innerException = ex2;
			}
			Thread.Sleep(100);
		}
		throw new IOException("宿主尚未完成页面文件写入，改用兼容渲染。", innerException);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool IsWpsHost(Document document)
	{
		Microsoft.Office.Interop.Word.Application value = null;
		try
		{
			value = document.Application;
			string text = ((value == null) ? string.Empty : value.Name);
			string text2 = ((value == null) ? string.Empty : value.Path);
			return text.IndexOf("WPS", StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf("Kingsoft", StringComparison.OrdinalIgnoreCase) >= 0 || text2.IndexOf("WPS Office", StringComparison.OrdinalIgnoreCase) >= 0 || text2.IndexOf("Kingsoft", StringComparison.OrdinalIgnoreCase) >= 0;
		}
		catch
		{
			return false;
		}
		finally
		{
			ComObjectRelease.Release(ref value, "ImageConversionService.HostApplication");
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void EnsurePageCount(DocumentPaginator paginator)
	{
		if (paginator == null)
		{
			throw new InvalidOperationException("无法读取页面。");
		}
		try
		{
			if (!paginator.IsPageCountValid)
			{
				paginator.ComputePageCount();
			}
		}
		catch (Exception ex)
		{
			LogService.Warn("ImageConversionService.ComputePageCount", ex);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static ImageConversionBudget BuildBudget(DocumentPaginator paginator, IList<int> pages, int dpi, bool longImage)
	{
		if (paginator != null)
		{
			if (pages == null || pages.Count == 0)
			{
				throw new InvalidOperationException("图片转换预算缺少页面。");
			}
			List<ImagePageDimensions> list = new List<ImagePageDimensions>(pages.Count);
			foreach (int page2 in pages)
			{
				if (page2 < 1 || page2 > paginator.PageCount)
				{
					throw new InvalidOperationException("无法读取第 " + page2 + " 页。");
				}
				DocumentPage page = paginator.GetPage(page2 - 1);
				try
				{
					ImagePageDimensions imagePageDimensions = new ImagePageDimensions();
					System.Windows.Size size = page.Size;
					imagePageDimensions.Width = Math.Max(1, (int)Math.Round(size.Width / 96.0 * (double)dpi));
					size = page.Size;
					imagePageDimensions.Height = Math.Max(1, (int)Math.Round(size.Height / 96.0 * (double)dpi));
					list.Add(imagePageDimensions);
				}
				finally
				{
					((IDisposable)page)?.Dispose();
				}
			}
			return ImageConversionBudgetService.Evaluate(list, longImage, Environment.Is64BitProcess);
		}
		throw new ArgumentNullException("paginator");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void SaveXpsPage(DocumentPaginator paginator, int pageNumber, int dpi, string targetPath, ImageFileFormat format)
	{
		if (paginator != null)
		{
			if (pageNumber < 1 || pageNumber > paginator.PageCount)
			{
				throw new InvalidOperationException("无法读取第 " + pageNumber + " 页。");
			}
			DocumentPage page = paginator.GetPage(pageNumber - 1);
			try
			{
				System.Windows.Size size = page.Size;
				int num = Math.Max(1, (int)Math.Round(size.Width / 96.0 * (double)dpi));
				size = page.Size;
				int num2 = Math.Max(1, (int)Math.Round(size.Height / 96.0 * (double)dpi));
				DrawingVisual val = new DrawingVisual();
				DrawingContext val2 = val.RenderOpen();
				try
				{
					val2.DrawRectangle((System.Windows.Media.Brush)(object)System.Windows.Media.Brushes.White, (System.Windows.Media.Pen)null, new Rect(new System.Windows.Point(0.0, 0.0), page.Size));
					val2.DrawRectangle((System.Windows.Media.Brush)new VisualBrush(page.Visual), (System.Windows.Media.Pen)null, new Rect(new System.Windows.Point(0.0, 0.0), page.Size));
				}
				finally
				{
					((IDisposable)val2)?.Dispose();
				}
				RenderTargetBitmap val3 = new RenderTargetBitmap(num, num2, (double)dpi, (double)dpi, PixelFormats.Pbgra32);
				val3.Render((Visual)(object)val);
				SaveBitmapSource((BitmapSource)val3, targetPath, format);
				return;
			}
			finally
			{
				((IDisposable)page)?.Dispose();
			}
		}
		throw new InvalidOperationException("无法读取页面。");
	}

	private static void SaveBitmapSource(BitmapSource bitmap, string targetPath, ImageFileFormat format)
	{
		BitmapEncoder val = (BitmapEncoder)((format == ImageFileFormat.Jpg) ? new JpegBitmapEncoder
		{
			QualityLevel = 92
		} : new PngBitmapEncoder());
		val.Frames.Add(BitmapFrame.Create(bitmap));
		using FileStream fileStream = File.Create(targetPath);
		val.Save((Stream)fileStream);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static ImageConversionBudget ReadRenderedPageBudget(IList<string> imageFiles)
	{
		if (imageFiles == null || imageFiles.Count == 0)
		{
			throw new InvalidOperationException("没有可合成的图片。");
		}
		List<ImagePageDimensions> list = new List<ImagePageDimensions>(imageFiles.Count);
		foreach (string imageFile in imageFiles)
		{
			Image val = Image.FromFile(imageFile);
			try
			{
				list.Add(new ImagePageDimensions
				{
					Width = val.Width,
					Height = val.Height
				});
			}
			finally
			{
				((IDisposable)val)?.Dispose();
			}
		}
		return ImageConversionBudgetService.Evaluate(list, longImage: true, Environment.Is64BitProcess);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static Bitmap CombineVertical(IList<string> imageFiles, ImageConversionBudget budget)
	{
		if (imageFiles == null || imageFiles.Count == 0)
		{
			throw new InvalidOperationException("没有可合成的图片。");
		}
		if (budget == null)
		{
			throw new ArgumentNullException("budget");
		}
		ImageConversionBudgetService.EnsureAllowed(budget);
		Bitmap val = new Bitmap(budget.OutputWidth, checked((int)budget.OutputHeight), (System.Drawing.Imaging.PixelFormat)2498570);
		Image val2 = Image.FromFile(imageFiles[0]);
		try
		{
			val.SetResolution(val2.HorizontalResolution, val2.VerticalResolution);
		}
		finally
		{
			((IDisposable)val2)?.Dispose();
		}
		Graphics val3 = Graphics.FromImage((Image)(object)val);
		try
		{
			val3.Clear(System.Drawing.Color.White);
			int num = 0;
			foreach (string imageFile in imageFiles)
			{
				Image val4 = Image.FromFile(imageFile);
				try
				{
					int num2 = (budget.OutputWidth - val4.Width) / 2;
					val3.DrawImage(val4, num2, num, val4.Width, val4.Height);
					num += val4.Height;
				}
				finally
				{
					((IDisposable)val4)?.Dispose();
				}
			}
			return val;
		}
		finally
		{
			((IDisposable)val3)?.Dispose();
		}
	}

	private static void SaveBitmap(Bitmap bitmap, string targetPath, ImageFileFormat format)
	{
		if (format == ImageFileFormat.Jpg)
		{
			ImageCodecInfo val = ((IEnumerable<ImageCodecInfo>)ImageCodecInfo.GetImageDecoders()).FirstOrDefault((Func<ImageCodecInfo, bool>)((ImageCodecInfo c) => c.FormatID == ImageFormat.Jpeg.Guid));
			if (val != null)
			{
				EncoderParameters val2 = new EncoderParameters(1);
				try
				{
					val2.Param[0] = new EncoderParameter(Encoder.Quality, 92L);
					((Image)bitmap).Save(targetPath, val, val2);
					return;
				}
				finally
				{
					((IDisposable)val2)?.Dispose();
				}
			}
		}
		((Image)bitmap).Save(targetPath, ImageFormat.Png);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string GetExtension(ImageFileFormat format)
	{
		if (format != ImageFileFormat.Jpg)
		{
			return ".png";
		}
		return ".jpg";
	}
}
