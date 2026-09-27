using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using DocumentRepository.Models;
using DocumentRepository.Models.CompilationFormatting;
using DocumentRepository.Models.Features;
using DocumentRepository.Models.Formatting;
using DocumentRepository.Models.FormattingPlan;
using DocumentRepository.Models.Mutations;
using DocumentRepository.Models.Protection;
using DocumentRepository.Models.Snapshots;
using DocumentRepository.Models.Tasks;
using DocumentRepository.Services.Analysis;
using DocumentRepository.Services.CompilationFormatting;
using DocumentRepository.Services.Formatting;
using DocumentRepository.Services.Formatting.Planning;
using DocumentRepository.Services.Hosting;
using DocumentRepository.Services.Logging;
using DocumentRepository.Services.Protection;
using DocumentRepository.Services.Snapshots;
using DocumentRepository.Services.Tasks;
using Microsoft.Office.Interop.Word;

namespace DocumentRepository.Pipelines.CompilationFormatting;

public static class CompilationFormattingPipeline
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static CommandResult Execute(OperationContext context, CompilationRouteResolution route)
	{
		if (context == null)
		{
			throw new ArgumentNullException("context");
		}
		if (route != null)
		{
			CompilationRouteDecision decision = route.Decision;
			CompilationArticleList articleList = route.ArticleList;
			CompilationDocumentScanResult scan = route.Scan;
			if (decision == null)
			{
				return CompilationFailurePresentation.PreWriteResult(CompilationFormatFailureReasonCode.Unknown);
			}
			FormatConfig currentConfig = context.CurrentConfig;
			CompilationFormatOptions compilationFormatOptions = currentConfig?.CompilationFormatOptions;
			if (currentConfig == null || compilationFormatOptions == null)
			{
				return CompilationFailurePresentation.PreWriteResult(CompilationFormatFailureReasonCode.ConfigInvalid);
			}
			bool flag = decision.Kind == CompilationRouteKind.CompilationFull;
			if (!context.IsBatchMode && !context.SuppressUserDialogs)
			{
				if (context.UserInterface != null)
				{
					if (scan != null && scan.ScanErrors != null && scan.ScanErrors.Count > 0)
					{
						return CompilationFailurePresentation.PreWriteResult(CompilationFormatFailureReasonCode.ScanFailed, string.Join("；", scan.ScanErrors));
					}
					if (scan != null && scan.UnsupportedRegionMarkers.Count > 0)
					{
						return CompilationFailurePresentation.PreWriteResult(CompilationFormatFailureReasonCode.UnsupportedRegionMarker, string.Join("、", scan.UnsupportedRegionMarkers));
					}
					if (articleList != null && articleList.Articles.Count != 0)
					{
						if (compilationFormatOptions.FrontMatterMode != CompilationFrontMatterMode.Reject || articleList.FrontMatterEndParagraphIndex < 0)
						{
							CompilationConfirmationRequest request = BuildConfirmationRequest(articleList, decision, compilationFormatOptions, flag);
							CompilationConfirmationResult compilationConfirmationResult = context.UserInterface.ConfirmCompilation(request);
							if (compilationConfirmationResult == null || !compilationConfirmationResult.Confirmed)
							{
								return CompilationFailurePresentation.PreWriteResult(CompilationFormatFailureReasonCode.UserCancelled);
							}
							if (decision.NeedsExpandConfirmation && !compilationConfirmationResult.ExpandToFullArticles)
							{
								return CompilationFailurePresentation.PreWriteResult(CompilationFormatFailureReasonCode.ExpandNotConfirmed);
							}
							Document document = context.Document;
							CompilationManifest manifest = (UsesVisibleMarkers(scan) ? new CompilationManifest() : CompilationManifestService.Read(document));
							string text = CompilationSeparatorService.FindConflict(document, articleList, manifest, compilationFormatOptions, scan);
							if (text == null)
							{
								DocumentSession documentSession = null;
								TaskProgressSession taskProgressSession = null;
								bool flag2 = false;
								bool flag3 = false;
								string text2 = null;
								DocumentSnapshot documentSnapshot = null;
								DocumentWriteLease writeLease = null;
								CompilationFormatFailureStage compilationFormatFailureStage = CompilationFormatFailureStage.Entry;
								try
								{
									DocumentSessionOptions documentSessionOptions = DocumentSessionOptions.Create("format-compilation", "汇编排版");
									documentSessionOptions.UseUndoRecord = true;
									documentSessionOptions.SuppressAlerts = false;
									documentSessionOptions.RequireRecoveryCopy = true;
									documentSessionOptions.SelectionRestoreMode = SelectionRestoreMode.DocumentStart;
									DocumentRiskProfile documentRiskProfile = DocumentRiskProfiler.Capture(document);
									InteractiveProtectionCoordinator.ConfigureBeforeSession(documentSessionOptions, context, documentRiskProfile);
									documentSession = DocumentSession.Begin(context, documentSessionOptions);
									documentSession.SetStage("compilation-prepare");
									if (!documentSession.Capabilities.SupportsUndoRecord || !documentSession.IsUndoRecordActive)
									{
										return CompilationFailurePresentation.PreWriteResult(CompilationFormatFailureReasonCode.UndoUnavailable);
									}
									InteractiveProtectionCoordinator.Outcome outcome = InteractiveProtectionCoordinator.ResolveBeforeWrite(documentSessionOptions, documentSession, context, documentRiskProfile);
									if (outcome.Resolution != InteractiveProtectionCoordinator.Resolution.Cancelled)
									{
										if (outcome.Resolution == InteractiveProtectionCoordinator.Resolution.Failed)
										{
											return CompilationFailurePresentation.PreWriteResult(CompilationFormatFailureReasonCode.ProtectionUnavailable);
										}
										compilationFormatFailureStage = CompilationFormatFailureStage.Verify;
										documentSession.SetStage("compilation-source-snapshot");
										documentSnapshot = DocumentSnapshotService.Capture(document);
										documentSession.RegisterRestoreSnapshot(documentSnapshot);
										string planId = "compilation-" + Guid.NewGuid().ToString("N");
										string ruleContentHash = FormatConfigFingerprint.Build(compilationFormatOptions);
										writeLease = documentSession.BeginMutation(planId, documentSnapshot.SnapshotId, ruleContentHash);
										taskProgressSession = new TaskProgressSession(context.UserInterface.CreateProgress("format-compilation", "正在汇编排版..."), "format-compilation", "汇编排版");
										taskProgressSession.Start("汇编排版", 100, "建立文章边界");
										CompilationMutationWarmupService.Execute(document, writeLease);
										bool num = UsesVisibleMarkers(scan);
										compilationFormatFailureStage = CompilationFormatFailureStage.Boundary;
										CompilationManifest compilationManifest;
										if (!num)
										{
											compilationManifest = CompilationManifestService.Read(document);
											if (compilationManifest == null)
											{
												throw new CompilationTocUpdateException(CompilationFormatFailureReasonCode.ManifestUnreadable, "文档中没有可验证的汇编排版文章清单。");
											}
										}
										else
										{
											string formatTemplateName = ((ConfigManager.CurrentTemplateIndex >= 0 && ConfigManager.TemplateNames != null && ConfigManager.CurrentTemplateIndex < ConfigManager.TemplateNames.Length) ? ConfigManager.TemplateNames[ConfigManager.CurrentTemplateIndex] : "");
											compilationManifest = CompilationBoundaryService.ConvertMarkersToBoundaries(document, scan, articleList, formatTemplateName, compilationFormatOptions);
										}
										scan = null;
										List<int> orderIndexes = CompilationSeparatorService.RecordUserBreaksBeforeTitles(document, articleList, compilationManifest);
										List<int> list = (flag ? AllIndexes(articleList.Articles.Count) : decision.SelectedArticleIndexes);
										int count = list.Count;
										compilationFormatFailureStage = CompilationFormatFailureStage.ArticleFormat;
										for (int i = 0; i < count; i++)
										{
											CompilationArticleInfo compilationArticleInfo = articleList.Articles[list[i]];
											if (taskProgressSession != null)
											{
												if (taskProgressSession.CancellationRequested)
												{
													throw new OperationCanceledException("compilation-progress-cancelled");
												}
												taskProgressSession.Report(TaskProgressInfo.Create("汇编排版", 5 + (int)((long)i * 80L / Math.Max(1, count)), 100, "正在排版第 " + (i + 1) + "/" + count + " 篇：《" + compilationArticleInfo.Title + "》", "逐篇排版"));
											}
											FormatArticle(context, documentSession, compilationArticleInfo, !flag);
										}
										CompilationSeparatorService.RecreateUserBreaksBeforeTitles(document, articleList, orderIndexes);
										CompilationTocOutcome compilationTocOutcome = CompilationTocOutcome.Failed;
										compilationFormatFailureStage = CompilationFormatFailureStage.PageSetup;
										if (flag)
										{
											compilationTocOutcome = ApplyDocumentLevelStages(context, documentSession, taskProgressSession, compilationFormatOptions, articleList, compilationManifest);
										}
										else
										{
											compilationManifest.TocMayBeStale = true;
											CompilationManifestService.Write(document, compilationManifest);
										}
										compilationFormatFailureStage = CompilationFormatFailureStage.Verify;
										documentSession.SetStage("compilation-verify");
										VerificationReceipt receipt = CompilationOperationVerificationService.Verify(context.TaskId, planId, documentSnapshot, ruleContentHash, document, articleList.Articles.Count, compilationFormatOptions, flag, compilationTocOutcome, context.CurrentConfig);
										documentSession.MarkVerified(receipt);
										documentSession.Commit(receipt);
										flag2 = true;
										taskProgressSession?.Complete(flag ? "汇编排版完成" : "部分文章排版完成");
										if (!flag && context.UserInterface != null)
										{
											context.UserInterface.ShowMessage("部分汇编排版完成", "已完成所选文章的排版。目录可能已过期：请选择目录区域后再次点击“一键排版”更新目录。", FeatureMessageKind.Information);
										}
										if (flag)
										{
											if (!compilationFormatOptions.GenerateToc)
											{
												return CommandResult.SuccessResult("汇编排版完成，当前模板未启用目录。");
											}
											return compilationTocOutcome switch
											{
												CompilationTocOutcome.SkippedByExistingTocPolicy => CommandResult.SuccessResult("汇编排版完成；检测到用户已有目录，已按当前设置不生成自动目录。"), 
												CompilationTocOutcome.Created => CommandResult.SuccessResult("汇编排版完成，已生成目录。"), 
												CompilationTocOutcome.Updated => CommandResult.SuccessResult("汇编排版完成，已更新目录。"), 
												_ => CommandResult.SuccessResult("汇编排版完成。"), 
											};
										}
										return CommandResult.SuccessResult("部分文章排版完成。");
									}
									return CommandResult.CancelledResult(outcome.Message ?? "已取消汇编排版，未对文档做任何修改。");
								}
								catch (OperationCanceledException ex)
								{
									flag3 = true;
									LogService.Warn("CompilationFormattingPipeline canceled", ex);
									LogService.Warn("CompilationFormattingPipeline failure-stage=" + compilationFormatFailureStage);
									if (TryRollbackChangedSession(documentSession, documentSnapshot, writeLease, "CompilationFormattingPipeline.Rollback", out var _) != null)
									{
										flag3 = false;
										text2 = "汇编排版取消后恢复文档失败。";
										return CompilationFailurePresentation.PostWriteResult(recoveredVerified: false, documentSession?.GetRecoveryGuidanceForUser());
									}
									CommandResult commandResult = CompilationFailurePresentation.PreWriteResult(CompilationFormatFailureReasonCode.ProgressCancelled);
									text2 = commandResult.Message;
									return commandResult;
								}
								catch (CompilationTocUpdateException ex2)
								{
									LogService.Error("CompilationFormattingPipeline.Execute", ex2);
									LogService.Warn("CompilationFormattingPipeline failure-stage=" + compilationFormatFailureStage);
									if (TryRollbackChangedSession(documentSession, documentSnapshot, writeLease, "CompilationFormattingPipeline.Rollback", out var rollbackAttempted2) != null || !rollbackAttempted2)
									{
										text2 = "汇编排版失败且无法确认文档已恢复。";
										return CompilationFailurePresentation.ToCommandResult(new CompilationFormatFailure(CompilationFormatFailureReasonCode.RecoveryUnconfirmed, CompilationFormatFailureStage.Recovery, DocumentSafetyDisposition.RecoveryUnconfirmed), documentSession?.GetRecoveryGuidanceForUser());
									}
									CommandResult commandResult2 = CompilationFailurePresentation.ToCommandResult(new CompilationFormatFailure(ex2.ReasonCode, compilationFormatFailureStage, DocumentSafetyDisposition.RecoveredVerified));
									text2 = commandResult2.Message;
									return commandResult2;
								}
								catch (Exception ex3)
								{
									LogService.Error("CompilationFormattingPipeline.Execute", ex3);
									LogService.Warn("CompilationFormattingPipeline failure-stage=" + compilationFormatFailureStage);
									if (documentSession == null || documentSession.State == DocumentSessionState.Prepared)
									{
										CommandResult commandResult3 = CompilationFailurePresentation.ToCommandResult(new CompilationFormatFailure(CompilationFormatFailureReasonCode.Unknown, compilationFormatFailureStage, DocumentSafetyDisposition.Unchanged));
										text2 = commandResult3.Message;
										return commandResult3;
									}
									bool rollbackAttempted3;
									bool flag4 = TryRollbackChangedSession(documentSession, documentSnapshot, writeLease, "CompilationFormattingPipeline.Rollback", out rollbackAttempted3) == null && rollbackAttempted3;
									CommandResult commandResult4 = CompilationFailurePresentation.ToCommandResult(new CompilationFormatFailure((compilationFormatFailureStage == CompilationFormatFailureStage.Verify) ? CompilationFormatFailureReasonCode.VerificationFailed : CompilationFormatFailureReasonCode.ExecutionInterruptedRolledBack, flag4 ? compilationFormatFailureStage : CompilationFormatFailureStage.Recovery, flag4 ? DocumentSafetyDisposition.RecoveredVerified : DocumentSafetyDisposition.RecoveryUnconfirmed), (flag4 || documentSession == null) ? null : documentSession.GetRecoveryGuidanceForUser());
									text2 = commandResult4.Message;
									return commandResult4;
								}
								finally
								{
									if (taskProgressSession != null)
									{
										try
										{
											if (!flag2)
											{
												if (!flag3)
												{
													taskProgressSession.Fail(text2 ?? "汇编排版失败");
												}
												else
												{
													taskProgressSession.Cancel(text2 ?? "汇编排版已取消");
												}
											}
											taskProgressSession.Dispose();
										}
										catch (Exception ex4)
										{
											LogService.Warn("CompilationFormattingPipeline.CloseProgress", ex4);
										}
									}
									documentSession?.Dispose();
								}
							}
							return CompilationFailurePresentation.PreWriteResult(CompilationFormatFailureReasonCode.SeparatorConflict, text);
						}
						return CompilationFailurePresentation.PreWriteResult(CompilationFormatFailureReasonCode.FrontMatterRejected);
					}
					return CompilationFailurePresentation.PreWriteResult(route.UsesVisibleMarkers ? CompilationFormatFailureReasonCode.MarkerStructureInvalid : CompilationFormatFailureReasonCode.ManifestUnreadable, route.StructureFailure);
				}
				return CompilationFailurePresentation.PreWriteResult(CompilationFormatFailureReasonCode.UiServiceMissing);
			}
			return CompilationFailurePresentation.PreWriteResult(CompilationFormatFailureReasonCode.NonInteractiveMode);
		}
		throw new ArgumentNullException("route");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static Exception TryRollbackChangedSession(DocumentSession session, DocumentSnapshot sourceSnapshot, DocumentWriteLease writeLease, string logContext, out bool rollbackAttempted)
	{
		rollbackAttempted = false;
		if (session == null || session.State != DocumentSessionState.RolledBack)
		{
			if (session != null && session.State == DocumentSessionState.RecoveryRequired)
			{
				rollbackAttempted = true;
				return new InvalidOperationException("session-recovery-required");
			}
			if (session == null || (session.State != DocumentSessionState.Mutating && session.State != DocumentSessionState.Verified))
			{
				return null;
			}
			try
			{
				rollbackAttempted = true;
				if (sourceSnapshot == null)
				{
					throw new InvalidOperationException("compilation-restore-snapshot-missing");
				}
				if (writeLease != null && writeLease.HasConfirmedWrite)
				{
					session.RollbackChanges(sourceSnapshot);
				}
				else
				{
					session.AbortMutationWithoutWrites(sourceSnapshot);
				}
				return null;
			}
			catch (Exception ex)
			{
				LogService.Error(logContext, ex);
				return ex;
			}
		}
		rollbackAttempted = true;
		return null;
	}

	private static bool UsesVisibleMarkers(CompilationDocumentScanResult scan)
	{
		if (scan != null)
		{
			return scan.BodyMarkerParagraphIndexes.Count > 0;
		}
		return false;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void FormatArticle(OperationContext context, DocumentSession session, CompilationArticleInfo article, bool verifyOutsideScope)
	{
		Document document = context.Document;
		Microsoft.Office.Interop.Word.Range value = null;
		Microsoft.Office.Interop.Word.Range value2 = null;
		Microsoft.Office.Interop.Word.Range value3 = null;
		try
		{
			if (document.Bookmarks.Exists(article.BeginBookmarkName) && document.Bookmarks.Exists(article.EndBookmarkName))
			{
				Bookmarks bookmarks = document.Bookmarks;
				object Index = article.BeginBookmarkName;
				value = bookmarks.get_Item(ref Index).Range;
				Bookmarks bookmarks2 = document.Bookmarks;
				Index = article.EndBookmarkName;
				value2 = bookmarks2.get_Item(ref Index).Range;
				if (value2.Start > value.Start)
				{
					Index = value.Start;
					object End = value2.Start;
					value3 = document.Range(ref Index, ref End);
					FormatPlanExecutor.Execute(FormatExecutionPlanBuilder.PrepareAndBuild(context, value3, FormatExecutionScope.CompilationArticle, session, null, null, verifyOutsideScope), context.Application, document, value3, session, null);
					return;
				}
				throw new InvalidOperationException("第 " + article.OrderIndex + " 篇的文章范围无效，已停止执行。");
			}
			throw new InvalidOperationException("第 " + article.OrderIndex + " 篇的内部文章边界已损坏或丢失，已停止执行。");
		}
		finally
		{
			ComObjectRelease.Release(ref value3, "CompilationFormat.ArticleRange");
			ComObjectRelease.Release(ref value2, "CompilationFormat.ArticleEnd");
			ComObjectRelease.Release(ref value, "CompilationFormat.ArticleBegin");
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static CompilationTocOutcome ApplyDocumentLevelStages(OperationContext context, DocumentSession session, TaskProgressSession progress, CompilationFormatOptions options, CompilationArticleList articleList, CompilationManifest manifest)
	{
		Application application = context.Application;
		Document document = context.Document;
		FormatConfig currentConfig = context.CurrentConfig;
		session.SetStage("compilation-page-setup");
		progress?.Report(TaskProgressInfo.Create("汇编排版", 88, 100, "应用页面参数", "全文阶段"));
		FormatContext formatContext = new FormatContext
		{
			Application = application,
			Document = document,
			IsSelectionMode = false,
			Config = currentConfig,
			Analysis = DocumentAnalysisService.AnalyzeStructuralFlags(document)
		};
		FormatPagePreparationService.ApplyPageSetup(document, application, formatContext);
		bool flag = false;
		CompilationTocOutcome compilationTocOutcome = CompilationTocOutcome.Failed;
		if (options.GenerateToc)
		{
			session.SetStage("compilation-toc");
			progress?.Report(TaskProgressInfo.Create("汇编排版", 90, 100, "生成目录", "全文阶段"));
			List<CompilationTocEntry> list = new List<CompilationTocEntry>();
			foreach (CompilationArticleInfo article in articleList.Articles)
			{
				list.Add(new CompilationTocEntry
				{
					Title = article.Title,
					BookmarkName = article.BeginBookmarkName,
					OrderIndex = article.OrderIndex
				});
			}
			compilationTocOutcome = CompilationTocUpdateService.InsertOrUpdateTocRegion(document, list, options.TocOptions, options.TocPosition);
			flag = (compilationTocOutcome == CompilationTocOutcome.Created || compilationTocOutcome == CompilationTocOutcome.Updated) && options.TocOptions != null && !options.TocOptions.IncludeTocPageInContinuousNumbering;
			if (flag)
			{
				CompilationTocUpdateService.EnsureTocSectionBreaks(document);
			}
		}
		session.SetStage("compilation-breaks");
		if (CompilationSeparatorService.Reconcile(document, articleList, manifest, options))
		{
			CompilationManifestService.Write(document, manifest);
		}
		session.SetStage("compilation-page-numbers");
		progress?.Report(TaskProgressInfo.Create("汇编排版", 95, 100, "设置页码", "全文阶段"));
		ApplyPageNumbers(document, application, formatContext, options, articleList);
		if (flag)
		{
			session.SetStage("compilation-page-numbers-exclude-toc");
			CompilationTocUpdateService.FinalizeTocNumberingExclusion(document, options.PageNumberMode == CompilationPageNumberMode.Continuous);
		}
		if (options.GenerateToc && (compilationTocOutcome == CompilationTocOutcome.Created || compilationTocOutcome == CompilationTocOutcome.Updated))
		{
			session.SetStage("compilation-toc-refresh");
			CompilationTocService.RefreshRegionPageRefs(document);
		}
		if (currentConfig.EnableOrphanCharFix)
		{
			session.SetStage("compilation-orphan-fix");
			progress?.Report(TaskProgressInfo.Create("汇编排版", 97, 100, "最终孤字处理", "全文阶段"));
			DocumentAnalysisResult documentAnalysisResult = DocumentAnalysisService.AnalyzeElements(document, currentConfig);
			OrphanCharFormatter.FixDocument(document, documentAnalysisResult.Elements);
		}
		return compilationTocOutcome;
	}

	private static void ApplyPageNumbers(Document doc, Application app, FormatContext pageContext, CompilationFormatOptions options, CompilationArticleList articleList)
	{
		switch (options.PageNumberMode)
		{
		case CompilationPageNumberMode.RestartPerArticle:
			RestartPageNumbersPerSection(doc, app, pageContext, articleList);
			break;
		case CompilationPageNumberMode.Remove:
			PageSetupManager.DeletePageNumbers(doc);
			break;
		case CompilationPageNumberMode.NoChange:
			break;
		default:
			ClearRestartFlags(doc);
			FormatPagePreparationService.ApplyPageNumbers(doc, app, pageContext);
			break;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ClearRestartFlags(Document doc)
	{
		int count = doc.Sections.Count;
		for (int i = 1; i <= count; i++)
		{
			Section value = null;
			try
			{
				value = doc.Sections[i];
				HeadersFooters footers = value.Footers;
				foreach (HeaderFooter item in footers)
				{
					if (item != null)
					{
						try
						{
							item.PageNumbers.RestartNumberingAtSection = false;
						}
						finally
						{
							ComObjectRelease.Release(item, "CompilationFormat.ClearFooter");
						}
					}
				}
				ComObjectRelease.Release(footers, "CompilationFormat.ClearFooters");
			}
			finally
			{
				ComObjectRelease.Release(ref value, "CompilationFormat.ClearSection");
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void RestartPageNumbersPerSection(Document doc, Application app, FormatContext pageContext, CompilationArticleList articleList)
	{
		FormatPagePreparationService.ApplyPageNumbers(doc, app, pageContext);
		HashSet<int> hashSet = new HashSet<int>();
		foreach (CompilationArticleInfo article in articleList.Articles)
		{
			Microsoft.Office.Interop.Word.Range value = null;
			try
			{
				if (!doc.Bookmarks.Exists(article.BeginBookmarkName))
				{
					throw new InvalidOperationException("第 " + article.OrderIndex + " 篇的内部文章边界已损坏或丢失，已停止执行。");
				}
				Bookmarks bookmarks = doc.Bookmarks;
				object Index = article.BeginBookmarkName;
				value = bookmarks.get_Item(ref Index).Range;
				Section value2 = value.Sections[1];
				int index = value2.Index;
				ComObjectRelease.Release(ref value2, "CompilationFormat.ArticleSection");
				hashSet.Add(index);
			}
			finally
			{
				ComObjectRelease.Release(ref value, "CompilationFormat.ArticleBeginRange");
			}
		}
		int count = doc.Sections.Count;
		for (int i = 1; i <= count; i++)
		{
			Section value3 = null;
			try
			{
				value3 = doc.Sections[i];
				bool flag = hashSet.Contains(i);
				HeadersFooters footers = value3.Footers;
				foreach (HeaderFooter item in footers)
				{
					if (item == null)
					{
						continue;
					}
					try
					{
						item.PageNumbers.RestartNumberingAtSection = flag;
						if (flag)
						{
							item.PageNumbers.StartingNumber = 1;
						}
					}
					finally
					{
						ComObjectRelease.Release(item, "CompilationFormat.Footer");
					}
				}
				ComObjectRelease.Release(footers, "CompilationFormat.Footers");
			}
			finally
			{
				ComObjectRelease.Release(ref value3, "CompilationFormat.Section");
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static CompilationConfirmationRequest BuildConfirmationRequest(CompilationArticleList articleList, CompilationRouteDecision decision, CompilationFormatOptions options, bool isFull)
	{
		CompilationConfirmationRequest compilationConfirmationRequest = new CompilationConfirmationRequest
		{
			IsFullDocument = isFull,
			GenerateToc = options.GenerateToc,
			StartEachArticleOnNewPage = options.StartEachArticleOnNewPage,
			OfferExpandToFullArticles = decision.NeedsExpandConfirmation,
			FrontMatterModeText = ((options.FrontMatterMode == CompilationFrontMatterMode.Reject) ? "拒绝" : "保留"),
			PageNumberModeText = PageNumberModeText(options.PageNumberMode)
		};
		if (isFull)
		{
			foreach (CompilationArticleInfo article in articleList.Articles)
			{
				compilationConfirmationRequest.Articles.Add(article);
			}
		}
		else
		{
			foreach (int selectedArticleIndex in decision.SelectedArticleIndexes)
			{
				compilationConfirmationRequest.Articles.Add(articleList.Articles[selectedArticleIndex]);
				compilationConfirmationRequest.SelectedArticleIndexes.Add(selectedArticleIndex);
			}
		}
		return compilationConfirmationRequest;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string PageNumberModeText(CompilationPageNumberMode mode)
	{
		return mode switch
		{
			CompilationPageNumberMode.RestartPerArticle => "每篇重新开始", 
			CompilationPageNumberMode.NoChange => "不调整", 
			CompilationPageNumberMode.Remove => "删除", 
			_ => "全文连续", 
		};
	}

	private static List<int> AllIndexes(int count)
	{
		List<int> list = new List<int>(count);
		for (int i = 0; i < count; i++)
		{
			list.Add(i);
		}
		return list;
	}
}
