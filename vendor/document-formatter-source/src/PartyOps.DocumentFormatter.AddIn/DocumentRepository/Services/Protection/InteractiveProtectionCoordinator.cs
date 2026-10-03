using System;
using System.Runtime.CompilerServices;
using DocumentRepository.Models;
using DocumentRepository.Models.Protection;
using DocumentRepository.Models.Recovery;
using DocumentRepository.Models.Safety;
using DocumentRepository.Services.Hosting;
using DocumentRepository.Services.Recovery;
using DocumentRepository.Services.Ui;

namespace DocumentRepository.Services.Protection;

public static class InteractiveProtectionCoordinator
{
	public enum Resolution
	{
		Proceed,
		Cancelled,
		Failed
	}

	public sealed class Outcome
	{
		public Resolution Resolution { get; private set; }

		public string Message { get; private set; }

		public DocumentRiskProfile EffectiveProfile { get; private set; }

		public ExecutionDecisionReasonCode ReasonCode { get; private set; }

		private Outcome(Resolution resolution, string message, DocumentRiskProfile effectiveProfile, ExecutionDecisionReasonCode reasonCode)
		{
			Resolution = resolution;
			Message = message;
			EffectiveProfile = effectiveProfile;
			ReasonCode = reasonCode;
		}

		public static Outcome Proceed(DocumentRiskProfile effectiveProfile)
		{
			return new Outcome(Resolution.Proceed, null, effectiveProfile, ExecutionDecisionReasonCode.None);
		}

		public static Outcome Cancel(string message, DocumentRiskProfile effectiveProfile)
		{
			return new Outcome(Resolution.Cancelled, message, effectiveProfile, ExecutionDecisionReasonCode.None);
		}

		public static Outcome Fail(string message, DocumentRiskProfile effectiveProfile, ExecutionDecisionReasonCode reasonCode)
		{
			return new Outcome(Resolution.Failed, message, effectiveProfile, reasonCode);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void ConfigureBeforeSession(DocumentSessionOptions sessionOptions, OperationContext context, DocumentRiskProfile profile)
	{
		if (sessionOptions == null)
		{
			throw new ArgumentNullException("sessionOptions");
		}
		if (context == null)
		{
			throw new ArgumentNullException("context");
		}
		if (profile != null)
		{
			if (!context.IsBatchMode && sessionOptions.RequireRecoveryCopy)
			{
				if (profile.State == DocumentRiskState.NewBlank)
				{
					sessionOptions.RequireRecoveryCopy = false;
					sessionOptions.ProtectionMode = RecoveryProtectionMode.UndoOnlyForBlankDocument;
				}
				else if (profile.State == DocumentRiskState.NewWithContent || profile.State == DocumentRiskState.PathUnavailable)
				{
					sessionOptions.RequireRecoveryCopy = false;
					sessionOptions.ProtectionMode = RecoveryProtectionMode.UndoOnlyForUnsavedDocument;
				}
			}
			return;
		}
		throw new ArgumentNullException("profile");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static Outcome ResolveBeforeWrite(DocumentSessionOptions sessionOptions, DocumentSession session, OperationContext context, DocumentRiskProfile initialProfile)
	{
		if (sessionOptions != null)
		{
			if (session == null)
			{
				throw new ArgumentNullException("session");
			}
			if (context != null)
			{
				if (initialProfile != null)
				{
					IUserInteractionPort userInteractionPort = UserInteraction.Resolve(context);
					if (sessionOptions.ProtectionMode != RecoveryProtectionMode.UndoOnlyForBlankDocument && sessionOptions.ProtectionMode != RecoveryProtectionMode.UndoOnlyForUnsavedDocument)
					{
						if (sessionOptions.RequireRecoveryCopy)
						{
							RecoveryPreparationResult recoveryPreparationResult = session.PrepareRecoveryCopy();
							if (!recoveryPreparationResult.Prepared)
							{
								if (!context.IsBatchMode && IsFallbackEligible(recoveryPreparationResult.Decision.ReasonCode))
								{
									UserConfirmationRequest request = new UserConfirmationRequest
									{
										Kind = UserConfirmationKind.RecoveryFallbackDirectory,
										OfferCancel = true
									};
									if (userInteractionPort.Ask(request) == UserChoiceResult.Primary)
									{
										RecoveryPreparationResult recoveryPreparationResult2 = session.PrepareRecoveryCopyWithFallback();
										if (recoveryPreparationResult2.Prepared)
										{
											return Outcome.Proceed(initialProfile);
										}
										recoveryPreparationResult = recoveryPreparationResult2;
									}
								}
								if (context.IsBatchMode)
								{
									return Outcome.Fail(RecoveryUserMessages.Resolve(recoveryPreparationResult.Decision.ReasonCode), initialProfile, recoveryPreparationResult.Decision.ReasonCode);
								}
								if (!session.TryConfirmUndoOnlyFallback(initialProfile, userInteractionPort, out var userCancelled))
								{
									if (userCancelled)
									{
										return Outcome.Cancel("已取消。", initialProfile);
									}
									return Outcome.Fail(RecoveryUserMessages.Resolve(recoveryPreparationResult.Decision.ReasonCode), initialProfile, recoveryPreparationResult.Decision.ReasonCode);
								}
							}
						}
					}
					else
					{
						session.PrepareProtectionEvidence(initialProfile);
					}
					return Outcome.Proceed(initialProfile);
				}
				throw new ArgumentNullException("initialProfile");
			}
			throw new ArgumentNullException("context");
		}
		throw new ArgumentNullException("sessionOptions");
	}

	private static bool IsFallbackEligible(ExecutionDecisionReasonCode reasonCode)
	{
		if (reasonCode != ExecutionDecisionReasonCode.RecoveryRootUnavailable && reasonCode != ExecutionDecisionReasonCode.RecoveryQuotaExceeded)
		{
			return reasonCode == ExecutionDecisionReasonCode.RecoveryDiskSpaceInsufficient;
		}
		return true;
	}
}
