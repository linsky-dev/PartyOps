using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using DocumentRepository.Models;
using DocumentRepository.Models.Mutations;
using DocumentRepository.Models.Protection;
using DocumentRepository.Models.Recovery;
using DocumentRepository.Models.Safety;
using DocumentRepository.Models.Snapshots;
using DocumentRepository.Models.Tasks;
using DocumentRepository.Services.Configuration;
using DocumentRepository.Services.Logging;
using DocumentRepository.Services.Recovery;
using DocumentRepository.Services.Snapshots;
using DocumentRepository.Services.Tasks;
using DocumentRepository.Services.Ui;
using Microsoft.Office.Interop.Word;

namespace DocumentRepository.Services.Hosting;

public sealed class DocumentSession : IDisposable
{
	internal static Action BeforeUndoForTesting;

	private readonly IDocumentHost host;

	private readonly DocumentSessionOptions options;

	private readonly OperationContext operationContext;

	private readonly List<Document> temporaryDocuments = new List<Document>();

	private readonly DocumentSessionStateMachine stateMachine = new DocumentSessionStateMachine();

	private SessionCommitGuard commitGuard;

	private WriteLeaseGuard writeLeaseGuard;

	private DocumentWriteLease activeWriteLease;

	private DocumentSnapshot restoreSnapshot;

	private SessionRecoveryGuard recoveryGuard;

	private SelectionCheckpoint selectionCheckpoint;

	private bool hasScreenUpdating;

	private bool oldScreenUpdating;

	private bool hasDisplayAlerts;

	private WdAlertLevel oldDisplayAlerts;

	private bool hasEnableEvents;

	private bool oldEnableEvents;

	private bool undoStarted;

	private DateTime undoStartedAtUtc;

	private Exception undoStartFailure;

	private bool disposed;

	private string stage;

	private string objectLocation;

	public string TaskId { get; }

	public DocumentSessionState State => stateMachine.State;

	public HostCapabilities Capabilities => host.Capabilities;

	public bool IsUndoRecordActive => undoStarted;

	public Exception UndoRecordStartFailure => undoStartFailure;

	public RecoveryCopyReceipt RecoveryReceipt
	{
		get
		{
			if (!recoveryGuard.HasReceipt)
			{
				return null;
			}
			return recoveryGuard.Receipt;
		}
	}

	public SessionProtectionEvidence ProtectionEvidence
	{
		get
		{
			if (!recoveryGuard.HasEvidence)
			{
				return null;
			}
			return recoveryGuard.Evidence;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private DocumentSession(IDocumentHost host, DocumentSessionOptions options, OperationContext operationContext)
	{
		this.host = host ?? throw new ArgumentNullException("host");
		if (options == null)
		{
			throw new ArgumentNullException("options");
		}
		this.options = CopyOptions(options);
		this.operationContext = operationContext;
		TaskId = SessionTaskIdResolver.Resolve(operationContext?.TaskId);
		stage = "session-start";
		if (operationContext != null)
		{
			if (string.IsNullOrWhiteSpace(operationContext.TaskId))
			{
				operationContext.TaskId = TaskId;
			}
			operationContext.FeatureId = this.options.FeatureId;
			operationContext.Stage = stage;
		}
		Enter();
		stateMachine.Transition(DocumentSessionState.Prepared);
		commitGuard = new SessionCommitGuard(stateMachine, TaskId);
		writeLeaseGuard = new WriteLeaseGuard(stateMachine);
		bool required = ResolveProtectionRequired(this.options);
		RecoveryProtectionMode expectedEvidenceMode = ((this.options.ProtectionMode != RecoveryProtectionMode.NotApplicable) ? this.options.ProtectionMode : (this.options.RequireRecoveryCopy ? RecoveryProtectionMode.PreferFileCopyAllowConfirmedUndoFallback : RecoveryProtectionMode.NotApplicable));
		recoveryGuard = new SessionRecoveryGuard(required, TaskId, this.options.FeatureId, expectedEvidenceMode);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static DocumentSession Begin(OperationContext context, DocumentSessionOptions options)
	{
		if (context == null)
		{
			throw new ArgumentNullException("context");
		}
		if (context.Application != null)
		{
			HostThreadRuntime.AssertAccess("DocumentSession.Begin.OperationContext");
			return new DocumentSession(new WordWpsDocumentHost(context.Application, context.Document), options, context);
		}
		throw new InvalidOperationException("文档会话缺少 Word/WPS 应用实例。");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static DocumentSession Begin(Application application, Document document, DocumentSessionOptions options)
	{
		HostThreadRuntime.AssertAccess("DocumentSession.Begin.Application");
		return new DocumentSession(new WordWpsDocumentHost(application, document), options, null);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public void SetStage(string value, string location = null)
	{
		stage = (string.IsNullOrWhiteSpace(value) ? "unspecified" : value);
		objectLocation = location;
		if (operationContext != null)
		{
			operationContext.Stage = stage;
			operationContext.ObjectLocation = objectLocation;
		}
		LogService.Info(BuildLogContext("stage"));
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public void RegisterTemporaryDocument(Document document)
	{
		if (document != null && !temporaryDocuments.Contains(document))
		{
			temporaryDocuments.Add(document);
			LogService.Info(BuildLogContext("temporary-document-registered"));
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public void ReplaceTemporaryDocument(Document previous, Document current)
	{
		if (previous != null)
		{
			temporaryDocuments.Remove(previous);
		}
		if (current != null && !temporaryDocuments.Contains(current))
		{
			temporaryDocuments.Add(current);
		}
		LogService.Info(BuildLogContext("temporary-document-replaced"));
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public void Complete()
	{
		commitGuard.AssertLegacyCompleteAllowed();
		stateMachine.Transition(DocumentSessionState.Committed);
		writeLeaseGuard.Invalidate();
		activeWriteLease = null;
		UpdateRecoveryState(RecoveryCopyState.Committed);
		LogService.Info(BuildLogContext("complete"));
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public void MarkVerified(VerificationReceipt receipt)
	{
		commitGuard.MarkVerified(receipt);
		LogService.Info(BuildLogContext("verified"));
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public void Commit(VerificationReceipt receipt)
	{
		commitGuard.Commit(receipt);
		writeLeaseGuard.Invalidate();
		activeWriteLease = null;
		UpdateRecoveryState(RecoveryCopyState.Committed);
		LogService.Info(BuildLogContext("commit"));
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public void MarkChangesStarted()
	{
		if (stateMachine.State != DocumentSessionState.Mutating)
		{
			if (!undoStarted)
			{
				throw new InvalidOperationException(BuildLogContext("changes-without-undo") + "，高可靠文档修改必须位于可撤销事务中。");
			}
			AssertRecoveryReadyForWrite();
			stateMachine.Transition(DocumentSessionState.Mutating);
			LogService.Info(BuildLogContext("changes-started"));
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public RecoveryPreparationResult PrepareRecoveryCopy()
	{
		if (options.RequireRecoveryCopy)
		{
			if (stateMachine.State != DocumentSessionState.Prepared)
			{
				throw new InvalidOperationException("恢复副本必须在文档修改前准备。");
			}
			if (!recoveryGuard.HasReceipt)
			{
				Stopwatch stopwatch = Stopwatch.StartNew();
				string documentStablePath = GetDocumentStablePath();
				if (documentStablePath == null)
				{
					RecoveryPreparationResult recoveryPreparationResult = RecoveryPreparationResult.Denied(ExecutionDecisionReasonCode.RecoverySourceNotSaved, "recovery-source-not-saved");
					TaskEventReporter.Report(JsonlTaskEventJournal.Shared, TaskEventType.RecoveryPreparationDenied, TaskId, options.FeatureId, null, stage, "denied", recoveryPreparationResult.Decision.ReasonCode.ToString(), null, stopwatch.ElapsedMilliseconds);
					LogService.Warn(BuildLogContext("recovery-prepare-denied") + ", reason=" + recoveryPreparationResult.Decision.ReasonCode);
					return recoveryPreparationResult;
				}
				bool sourceIsSaved = ReadDocumentSaved();
				RecoveryPreparationResult recoveryPreparationResult2 = RecoveryCopyService.Prepare(new RecoveryCopyRequest
				{
					TaskId = TaskId,
					FeatureId = options.FeatureId,
					DocumentLifecycleId = DocumentLifecycleRegistry.GetOrCreate(host.Document),
					SourcePath = documentStablePath,
					SourceIsSaved = sourceIsSaved,
					AllowFallback = false
				});
				if (recoveryPreparationResult2.Prepared)
				{
					recoveryGuard.Register(recoveryPreparationResult2.Receipt);
					TaskEventReporter.Report(JsonlTaskEventJournal.Shared, TaskEventType.RecoveryPrepared, TaskId, options.FeatureId, null, stage, "success", recoveryPreparationResult2.Receipt.Coverage.ToString(), null, stopwatch.ElapsedMilliseconds);
					LogService.Info(BuildLogContext("recovery-prepared") + ", coverage=" + recoveryPreparationResult2.Receipt.Coverage.ToString() + ", length=" + recoveryPreparationResult2.Receipt.Length);
				}
				else
				{
					TaskEventReporter.Report(JsonlTaskEventJournal.Shared, TaskEventType.RecoveryPreparationDenied, TaskId, options.FeatureId, null, stage, "denied", recoveryPreparationResult2.Decision.ReasonCode.ToString(), null, stopwatch.ElapsedMilliseconds);
					LogService.Warn(BuildLogContext("recovery-prepare-denied") + ", reason=" + recoveryPreparationResult2.Decision.ReasonCode);
				}
				return recoveryPreparationResult2;
			}
			return RecoveryPreparationResult.Allowed(recoveryGuard.Receipt);
		}
		throw new InvalidOperationException("本会话未要求恢复副本。");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public RecoveryPreparationResult PrepareRecoveryCopyWithFallback()
	{
		if (options.RequireRecoveryCopy)
		{
			if (stateMachine.State == DocumentSessionState.Prepared)
			{
				if (recoveryGuard.HasReceipt)
				{
					return RecoveryPreparationResult.Allowed(recoveryGuard.Receipt);
				}
				string documentStablePath = GetDocumentStablePath();
				if (documentStablePath != null)
				{
					bool sourceIsSaved = ReadDocumentSaved();
					RecoveryPreparationResult recoveryPreparationResult = RecoveryCopyService.Prepare(new RecoveryCopyRequest
					{
						TaskId = TaskId,
						FeatureId = options.FeatureId,
						DocumentLifecycleId = DocumentLifecycleRegistry.GetOrCreate(host.Document),
						SourcePath = documentStablePath,
						SourceIsSaved = sourceIsSaved,
						AllowFallback = true
					});
					if (recoveryPreparationResult.Prepared)
					{
						recoveryGuard.Register(recoveryPreparationResult.Receipt);
					}
					return recoveryPreparationResult;
				}
				return RecoveryPreparationResult.Denied(ExecutionDecisionReasonCode.RecoverySourceNotSaved, "recovery-source-not-saved");
			}
			throw new InvalidOperationException("恢复副本必须在文档修改前准备。");
		}
		throw new InvalidOperationException("本会话未要求恢复副本。");
	}

	public bool TryConfirmUndoOnlyFallback(DocumentRiskProfile profile, IUserInteractionPort interactionPort)
	{
		bool userCancelled;
		return TryConfirmUndoOnlyFallback(profile, interactionPort, out userCancelled);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public bool TryConfirmUndoOnlyFallback(DocumentRiskProfile profile, IUserInteractionPort interactionPort, out bool userCancelled)
	{
		userCancelled = false;
		if (profile == null)
		{
			throw new ArgumentNullException("profile");
		}
		if (interactionPort == null)
		{
			throw new ArgumentNullException("interactionPort");
		}
		if (stateMachine.State == DocumentSessionState.Prepared)
		{
			if (undoStarted)
			{
				if (!recoveryGuard.HasEvidence)
				{
					UserConfirmationRequest request = new UserConfirmationRequest
					{
						Kind = UserConfirmationKind.RecoveryFallbackUndoOnly,
						OfferCancel = true
					};
					UserChoiceResult userChoiceResult = interactionPort.Ask(request);
					if (userChoiceResult == UserChoiceResult.Primary)
					{
						SessionProtectionEvidence value = SessionProtectionEvidence.Create(TaskId, options.FeatureId, DocumentLifecycleRegistry.GetOrCreate(host.Document), RecoveryProtectionMode.PreferFileCopyAllowConfirmedUndoFallback, ProtectionCoverage.ConfirmedUndoOnly, userConfirmed: true);
						recoveryGuard.RegisterEvidence(value);
						LogService.Info(BuildLogContext("undo-only-fallback-confirmed"));
						return true;
					}
					userCancelled = userChoiceResult == UserChoiceResult.Cancelled;
					LogService.Info(BuildLogContext("undo-only-fallback-declined") + ", choice=" + userChoiceResult);
					return false;
				}
				return true;
			}
			throw new InvalidOperationException("仅撤销保护要求真实可用的撤销记录。");
		}
		throw new InvalidOperationException("降级确认必须在文档修改前完成。");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public UserChoiceResult ResolveUnsavedNewDocument(DocumentRiskProfile profile, IUserInteractionPort interactionPort)
	{
		if (profile != null)
		{
			if (interactionPort != null)
			{
				if (profile.State == DocumentRiskState.NewWithContent || profile.State == DocumentRiskState.PathUnavailable)
				{
					if (stateMachine.State == DocumentSessionState.Prepared)
					{
						UserConfirmationRequest request = new UserConfirmationRequest
						{
							Kind = UserConfirmationKind.UnsavedContentSaveContinue,
							OfferCancel = true
						};
						UserChoiceResult userChoiceResult = interactionPort.Ask(request);
						LogService.Info(BuildLogContext("unsaved-new-document-choice") + ", choice=" + userChoiceResult);
						if (userChoiceResult == UserChoiceResult.Secondary)
						{
							if (undoStarted)
							{
								SessionProtectionEvidence value = SessionProtectionEvidence.Create(TaskId, options.FeatureId, DocumentLifecycleRegistry.GetOrCreate(host.Document), RecoveryProtectionMode.PreferFileCopyAllowConfirmedUndoFallback, ProtectionCoverage.ConfirmedUndoOnly, userConfirmed: true);
								recoveryGuard.RegisterEvidence(value);
								return UserChoiceResult.Secondary;
							}
							throw new InvalidOperationException("仅撤销保护要求真实可用的撤销记录。");
						}
						return userChoiceResult;
					}
					throw new InvalidOperationException("文档状态处置必须在文档修改前完成。");
				}
				throw new InvalidOperationException("三项选择仅适用于新建非空或路径不可用文档，实际=" + profile.State);
			}
			throw new ArgumentNullException("interactionPort");
		}
		throw new ArgumentNullException("profile");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	internal SessionProtectionEvidence PrepareProtectionEvidence(DocumentRiskProfile profile)
	{
		if (profile == null)
		{
			throw new ArgumentNullException("profile");
		}
		if (options.ProtectionMode == RecoveryProtectionMode.UndoOnlyForBlankDocument || options.ProtectionMode == RecoveryProtectionMode.UndoOnlyForUnsavedDocument)
		{
			bool flag = options.ProtectionMode == RecoveryProtectionMode.UndoOnlyForBlankDocument;
			if (flag && profile.State != DocumentRiskState.NewBlank)
			{
				throw new InvalidOperationException("空白文档仅撤销保护要求可靠空白画像，实际=" + profile.State);
			}
			if (flag || profile.State == DocumentRiskState.NewWithContent || profile.State == DocumentRiskState.PathUnavailable)
			{
				if (stateMachine.State != DocumentSessionState.Prepared)
				{
					throw new InvalidOperationException("保护证据必须在文档修改前签发。");
				}
				if (!undoStarted)
				{
					throw new InvalidOperationException("空白文档仅撤销保护要求真实可用的撤销记录。");
				}
				string orCreate = DocumentLifecycleRegistry.GetOrCreate(host.Document);
				if (string.Equals(profile.DocumentLifecycleId, orCreate, StringComparison.Ordinal))
				{
					if (recoveryGuard.HasEvidence)
					{
						return recoveryGuard.Evidence;
					}
					Stopwatch stopwatch = Stopwatch.StartNew();
					SessionProtectionEvidence sessionProtectionEvidence = SessionProtectionEvidence.Create(TaskId, options.FeatureId, orCreate, options.ProtectionMode, (!flag) ? ProtectionCoverage.UnsavedInMemoryContent : ProtectionCoverage.NoPreexistingUserContent, userConfirmed: false);
					recoveryGuard.RegisterEvidence(sessionProtectionEvidence);
					TaskEventReporter.Report(JsonlTaskEventJournal.Shared, TaskEventType.RecoveryPrepared, TaskId, options.FeatureId, null, stage, "success", "protection-evidence-undo-only", null, stopwatch.ElapsedMilliseconds);
					LogService.Info(BuildLogContext("protection-evidence-prepared") + ", mode=" + sessionProtectionEvidence.Mode.ToString() + ", coverage=" + sessionProtectionEvidence.Coverage);
					return sessionProtectionEvidence;
				}
				throw new InvalidOperationException("风险画像不属于当前文档，拒绝跨文档复用。");
			}
			throw new InvalidOperationException("未保存文档仅撤销保护要求无稳定路径画像，实际=" + profile.State);
		}
		throw new InvalidOperationException("本会话未启用仅撤销保护模式。");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public string GetRecoveryGuidanceForUser()
	{
		RecoveryCopyReceipt recoveryReceipt = RecoveryReceipt;
		if (recoveryReceipt == null)
		{
			return null;
		}
		string text = ((recoveryReceipt.Coverage == RecoveryCoverage.LastSavedBaseline) ? "（恢复副本是最后保存版本，未保存修改可能不在其中）" : string.Empty);
		return "恢复副本位于：" + ApplicationDataPaths.RecoveryDirectory(recoveryReceipt.RecoveryId) + text;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public DocumentWriteLease BeginMutation(string planId, string sourceSnapshotId, string ruleContentHash = null)
	{
		MarkChangesStarted();
		if (host.Document != null)
		{
			string orCreate = DocumentLifecycleRegistry.GetOrCreate(host.Document);
			commitGuard.RequireVerification(planId, sourceSnapshotId, ruleContentHash);
			object token = writeLeaseGuard.IssueToken(orCreate, planId, sourceSnapshotId);
			if (activeWriteLease == null)
			{
				activeWriteLease = new DocumentWriteLease(writeLeaseGuard, token);
			}
			return activeWriteLease;
		}
		throw new InvalidOperationException("当前会话没有可绑定写入凭证的文档。");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public void RegisterRestoreSnapshot(DocumentSnapshot sourceSnapshot)
	{
		if (sourceSnapshot == null)
		{
			throw new ArgumentNullException("sourceSnapshot");
		}
		if (stateMachine.State == DocumentSessionState.Prepared)
		{
			if (!string.IsNullOrWhiteSpace(sourceSnapshot.StateFingerprint))
			{
				if (restoreSnapshot == null || restoreSnapshot == sourceSnapshot)
				{
					restoreSnapshot = sourceSnapshot;
					return;
				}
				throw new InvalidOperationException("当前会话已登记另一份恢复基线，拒绝替换。");
			}
			throw new InvalidOperationException("恢复基线缺少完整状态指纹，不能用于撤销后验证。");
		}
		throw new InvalidOperationException("恢复基线必须在文档开始修改前登记。");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public void RollbackChanges(DocumentSnapshot sourceSnapshot = null)
	{
		if (stateMachine.State == DocumentSessionState.RolledBack)
		{
			return;
		}
		if (undoStarted)
		{
			stateMachine.Transition(DocumentSessionState.RollingBack);
			writeLeaseGuard.Invalidate();
			activeWriteLease = null;
			TaskEventReporter.Report(JsonlTaskEventJournal.Shared, TaskEventType.RollbackStarted, TaskId, options.FeatureId, null, stage, null, null, null, 0L);
			Exception ex = null;
			DocumentSnapshot documentSnapshot = sourceSnapshot ?? restoreSnapshot;
			try
			{
				EndUndoRecord("rollback-undo-end");
				BeforeUndoForTesting?.Invoke();
				host.UndoLastRecord();
			}
			catch (Exception ex2)
			{
				ex = ex2;
			}
			Exception ex3 = null;
			if (ex == null && documentSnapshot != null)
			{
				try
				{
					if (!string.IsNullOrWhiteSpace(documentSnapshot.RecoveryFingerprint))
					{
						DocumentSnapshotService.RestoreCustomDocumentProperties(documentSnapshot, host.Document);
					}
					DocumentSnapshotService.AssertRecoveryRestored(documentSnapshot, host.Document);
					LogService.Info(BuildLogContext("rollback-restore-verified"));
				}
				catch (Exception ex4)
				{
					ex3 = ex4;
				}
			}
			else if (ex == null && documentSnapshot == null)
			{
				LogService.Info(BuildLogContext("rollback-restore-verification-skipped"));
			}
			bool flag = ex != null;
			bool restoreVerificationFailed = ex3 != null;
			if (RollbackOutcomeResolver.ShouldThrow(flag, restoreVerificationFailed))
			{
				stateMachine.Transition(RollbackOutcomeResolver.Resolve(flag, restoreVerificationFailed));
				UpdateRecoveryState(RecoveryCopyState.RecoveryRequired);
				TaskEventReporter.Report(JsonlTaskEventJournal.Shared, TaskEventType.RollbackFailed, TaskId, options.FeatureId, null, stage, null, null, null, 0L, ex ?? ex3);
				LogService.Error(BuildLogContext(flag ? "rollback-failed" : "rollback-restore-verification-failed"), ex ?? ex3);
				throw new InvalidOperationException("文档回滚后未能恢复到修改前状态。请关闭该文档并选择不保存，避免保留不一致的内容。", ex ?? ex3);
			}
			stateMachine.Transition(RollbackOutcomeResolver.Resolve(undoFailed: false, restoreVerificationFailed: false));
			UpdateRecoveryState(RecoveryCopyState.RolledBackVerified);
			TaskEventReporter.Report(JsonlTaskEventJournal.Shared, TaskEventType.RollbackSucceeded, TaskId, options.FeatureId, null, stage, null, null, null, 0L);
			LogService.Warn(BuildLogContext("rollback-complete"));
			return;
		}
		throw new InvalidOperationException(BuildLogContext("rollback-unavailable") + "，本次会话没有可用的撤销记录。");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	internal void AbortMutationWithoutWrites(DocumentSnapshot sourceSnapshot)
	{
		if (sourceSnapshot == null)
		{
			throw new ArgumentNullException("sourceSnapshot");
		}
		if (stateMachine.State != DocumentSessionState.RolledBack)
		{
			if (stateMachine.State != DocumentSessionState.Mutating && stateMachine.State != DocumentSessionState.Verified)
			{
				throw new InvalidOperationException(BuildLogContext("abort-without-writes-invalid-state"));
			}
			if (!undoStarted)
			{
				throw new InvalidOperationException(BuildLogContext("rollback-unavailable") + "，本次会话没有可用的撤销记录。");
			}
			stateMachine.Transition(DocumentSessionState.RollingBack);
			writeLeaseGuard.Invalidate();
			activeWriteLease = null;
			TaskEventReporter.Report(JsonlTaskEventJournal.Shared, TaskEventType.RollbackStarted, TaskId, options.FeatureId, null, stage, null, null, null, 0L);
			Exception ex = null;
			try
			{
				EndUndoRecord("abort-without-writes-undo-end");
				DocumentSnapshotService.AssertRecoveryRestored(sourceSnapshot, host.Document);
			}
			catch (Exception ex2)
			{
				ex = ex2;
			}
			if (ex != null)
			{
				stateMachine.Transition(DocumentSessionState.RecoveryRequired);
				UpdateRecoveryState(RecoveryCopyState.RecoveryRequired);
				TaskEventReporter.Report(JsonlTaskEventJournal.Shared, TaskEventType.RollbackFailed, TaskId, options.FeatureId, null, stage, null, null, null, 0L, ex);
				LogService.Error(BuildLogContext("abort-without-writes-source-changed"), ex);
				throw new InvalidOperationException("文档在写入开始前已偏离源状态。请关闭该文档并选择不保存，避免保留不一致的内容。", ex);
			}
			stateMachine.Transition(DocumentSessionState.RolledBack);
			UpdateRecoveryState(RecoveryCopyState.RolledBackVerified);
			TaskEventReporter.Report(JsonlTaskEventJournal.Shared, TaskEventType.RollbackSucceeded, TaskId, options.FeatureId, null, stage, null, null, null, 0L);
			LogService.Info(BuildLogContext("rollback-skipped-before-write"));
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public void Dispose()
	{
		if (disposed)
		{
			return;
		}
		disposed = true;
		bool flag = stateMachine.State == DocumentSessionState.Committed;
		bool flag2 = stateMachine.State == DocumentSessionState.Mutating || stateMachine.State == DocumentSessionState.Verified || stateMachine.State == DocumentSessionState.RollingBack || stateMachine.State == DocumentSessionState.RolledBack || stateMachine.State == DocumentSessionState.RecoveryRequired;
		if (undoStarted)
		{
			if (!flag && flag2)
			{
				Recover("rollback-incomplete-session", delegate
				{
					RollbackChanges();
				});
			}
			else
			{
				Recover("undo-end", [MethodImpl(MethodImplOptions.NoInlining)] () =>
				{
					EndUndoRecord("undo-end");
				});
			}
		}
		if (stateMachine.State == DocumentSessionState.Prepared && recoveryGuard.HasReceipt)
		{
			UpdateRecoveryState(RecoveryCopyState.Abandoned);
		}
		if (!flag)
		{
			CloseTemporaryDocuments();
		}
		else
		{
			temporaryDocuments.Clear();
		}
		if (hasEnableEvents)
		{
			Recover("events-restore", delegate
			{
				host.SetEnableEvents(oldEnableEvents);
			});
		}
		if (hasDisplayAlerts)
		{
			Recover("alerts-restore", delegate
			{
				host.SetDisplayAlerts(oldDisplayAlerts);
			});
		}
		if (hasScreenUpdating)
		{
			Recover("screen-updating-restore", delegate
			{
				host.SetScreenUpdating(oldScreenUpdating);
			});
		}
		if (flag && options.RefreshVisibleLayoutOnSuccess)
		{
			Recover("visible-layout-refresh", host.RefreshVisibleLayout);
		}
		if (options.SelectionRestoreMode != SelectionRestoreMode.OriginalSelection)
		{
			if (options.SelectionRestoreMode == SelectionRestoreMode.DocumentStart)
			{
				Recover("selection-document-start", host.MoveSelectionToDocumentStart);
			}
		}
		else
		{
			Recover("selection-restore", delegate
			{
				host.RestoreSelection(selectionCheckpoint);
			});
		}
		stateMachine.Transition(DocumentSessionState.Disposed);
		LogService.Info(BuildLogContext(flag ? "session-disposed" : "session-recovered-after-failure"));
	}

	void IDisposable.Dispose()
	{
		this.Dispose();
	}

	private void AssertRecoveryReadyForWrite()
	{
		if (options.RequireRecoveryCopy || options.ProtectionMode == RecoveryProtectionMode.UndoOnlyForBlankDocument || options.ProtectionMode == RecoveryProtectionMode.UndoOnlyForUnsavedDocument)
		{
			recoveryGuard.AssertReadyForWrite(DocumentLifecycleRegistry.GetOrCreate(host.Document));
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool ResolveProtectionRequired(DocumentSessionOptions value)
	{
		switch (value.ProtectionMode)
		{
		case RecoveryProtectionMode.NotApplicable:
			return value.RequireRecoveryCopy;
		case RecoveryProtectionMode.UndoOnlyForBlankDocument:
		case RecoveryProtectionMode.UndoOnlyForUnsavedDocument:
			if (!value.RequireRecoveryCopy)
			{
				return true;
			}
			throw new InvalidOperationException("仅撤销保护与强制文件副本不能同时启用。");
		case RecoveryProtectionMode.PreferFileCopyAllowConfirmedUndoFallback:
			throw new InvalidOperationException("文件副本优先模式仅用于记录保护证据，不能直接配置为会话保护模式。");
		default:
			throw new ArgumentOutOfRangeException("value.ProtectionMode", value.ProtectionMode, "未知的会话保护模式。");
		}
	}

	private static DocumentSessionOptions CopyOptions(DocumentSessionOptions value)
	{
		return new DocumentSessionOptions
		{
			FeatureId = value.FeatureId,
			OperationName = value.OperationName,
			DisableScreenUpdating = value.DisableScreenUpdating,
			SuppressAlerts = value.SuppressAlerts,
			DisableEvents = value.DisableEvents,
			UseUndoRecord = value.UseUndoRecord,
			RequireRecoveryCopy = value.RequireRecoveryCopy,
			ProtectionMode = value.ProtectionMode,
			RefreshVisibleLayoutOnSuccess = value.RefreshVisibleLayoutOnSuccess,
			SelectionRestoreMode = value.SelectionRestoreMode
		};
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private string GetDocumentStablePath()
	{
		try
		{
			string path = host.Document.Path;
			string fullName = host.Document.FullName;
			if (!string.IsNullOrWhiteSpace(path) && !string.IsNullOrWhiteSpace(fullName) && Path.IsPathRooted(fullName))
			{
				string fullPath = Path.GetFullPath(fullName);
				return File.Exists(fullPath) ? fullPath : null;
			}
			return null;
		}
		catch (Exception ex)
		{
			LogService.Warn(BuildLogContext("recovery-source-path-unavailable") + ", exception=" + ex.GetType().Name);
			return null;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private bool ReadDocumentSaved()
	{
		try
		{
			return host.Document.Saved;
		}
		catch (Exception ex)
		{
			LogService.Warn(BuildLogContext("recovery-source-saved-unavailable") + ", exception=" + ex.GetType().Name);
			return false;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void UpdateRecoveryState(RecoveryCopyState newState)
	{
		if (recoveryGuard.HasReceipt)
		{
			bool flag = RecoveryManifestStore.TryUpdateState(ApplicationDataPaths.RecoveryDirectory(recoveryGuard.Receipt.RecoveryId), newState, DateTime.UtcNow);
			TaskEventReporter.Report(JsonlTaskEventJournal.Shared, TaskEventType.RecoveryStateChanged, TaskId, options.FeatureId, null, stage, flag ? "success" : "warning", newState.ToString(), null, 0L);
			if (!flag)
			{
				LogService.Warn(BuildLogContext("recovery-state-update-failed") + ", target=" + newState);
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void Enter()
	{
		LogService.Info(BuildLogContext("begin") + ", host=" + host.Capabilities.Kind.ToString() + ", hostVersion=" + (host.Capabilities.Version ?? string.Empty));
		if (options.SelectionRestoreMode == SelectionRestoreMode.OriginalSelection)
		{
			RunOptional("selection-capture", delegate
			{
				selectionCheckpoint = host.CaptureSelection();
			});
		}
		if (options.DisableScreenUpdating && host.Capabilities.SupportsScreenUpdating)
		{
			RunOptional("screen-updating-disable", delegate
			{
				hasScreenUpdating = host.TryReadScreenUpdating(out oldScreenUpdating);
				if (hasScreenUpdating)
				{
					host.SetScreenUpdating(value: false);
				}
			});
		}
		if (options.SuppressAlerts && host.Capabilities.SupportsDisplayAlerts)
		{
			RunOptional("alerts-disable", delegate
			{
				hasDisplayAlerts = host.TryReadDisplayAlerts(out oldDisplayAlerts);
				if (hasDisplayAlerts)
				{
					host.SetDisplayAlerts(WdAlertLevel.wdAlertsNone);
				}
			});
		}
		if (options.DisableEvents && host.Capabilities.SupportsEnableEvents)
		{
			RunOptional("events-disable", delegate
			{
				hasEnableEvents = host.TryReadEnableEvents(out oldEnableEvents);
				if (hasEnableEvents)
				{
					host.SetEnableEvents(value: false);
				}
			});
		}
		if (options.UseUndoRecord)
		{
			if (host.Capabilities.SupportsUndoRecord)
			{
				StartUndoRecord();
			}
			else
			{
				LogService.Warn(BuildLogContext("undo-start-unsupported"));
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void StartUndoRecord()
	{
		string text = (string.IsNullOrWhiteSpace(options.OperationName) ? "文档处理" : options.OperationName);
		LogService.Info(BuildLogContext("undo-start-attempt") + ", recordName=" + text);
		try
		{
			host.StartUndoRecord(text);
			undoStarted = true;
			undoStartedAtUtc = DateTime.UtcNow;
			undoStartFailure = null;
			LogService.Info(BuildLogContext("undo-start-success") + ", recordName=" + text);
		}
		catch (Exception ex)
		{
			undoStarted = false;
			undoStartFailure = ex;
			LogService.Error(BuildLogContext("undo-start-failed") + ", recordName=" + text, ex);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void EndUndoRecord(string action)
	{
		if (undoStarted)
		{
			long num = ((undoStartedAtUtc == default(DateTime)) ? 0 : Math.Max(0L, (long)(DateTime.UtcNow - undoStartedAtUtc).TotalMilliseconds));
			LogService.Info(BuildLogContext(action + "-attempt") + ", activeMs=" + num);
			host.EndUndoRecord();
			undoStarted = false;
			undoStartedAtUtc = default(DateTime);
			LogService.Info(BuildLogContext(action + "-success") + ", activeMs=" + num);
		}
	}

	private void RunOptional(string actionName, Action action)
	{
		try
		{
			action();
		}
		catch (Exception ex)
		{
			LogService.Warn(BuildLogContext(actionName), ex);
		}
	}

	private void Recover(string actionName, Action action)
	{
		try
		{
			action();
		}
		catch (Exception ex)
		{
			LogService.Error(BuildLogContext(actionName), ex);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void CloseTemporaryDocuments()
	{
		for (int num = temporaryDocuments.Count - 1; num >= 0; num--)
		{
			Document document = temporaryDocuments[num];
			try
			{
				object SaveChanges = WdSaveOptions.wdDoNotSaveChanges;
				object OriginalFormat = Type.Missing;
				object RouteDocument = Type.Missing;
				document.Close(ref SaveChanges, ref OriginalFormat, ref RouteDocument);
			}
			catch (Exception ex)
			{
				LogService.Error(BuildLogContext("temporary-document-close"), ex);
			}
			finally
			{
				ComObjectRelease.Release(document, BuildLogContext("temporary-document-release"));
			}
		}
		temporaryDocuments.Clear();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private string BuildLogContext(string action)
	{
		return "DocumentSession taskId=" + TaskId + ", feature=" + (options.FeatureId ?? "unknown") + ", operation=" + (options.OperationName ?? "unknown") + ", document=" + GetDocumentIdentity(host.Document) + ", stage=" + (stage ?? "unknown") + ", location=" + (objectLocation ?? "none") + ", action=" + (action ?? "none");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string GetDocumentIdentity(Document document)
	{
		if (document == null)
		{
			return "none";
		}
		try
		{
			string fullName = document.FullName;
			return string.IsNullOrWhiteSpace(fullName) ? document.Name : Path.GetFileName(fullName);
		}
		catch (Exception ex)
		{
			LogService.Warn("DocumentSession.GetDocumentIdentity", ex);
			return "unavailable";
		}
	}
}
