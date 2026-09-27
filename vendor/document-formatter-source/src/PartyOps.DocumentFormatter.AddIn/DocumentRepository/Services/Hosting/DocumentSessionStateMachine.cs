namespace DocumentRepository.Services.Hosting;

public sealed class DocumentSessionStateMachine
{
	public DocumentSessionState State { get; private set; }

	public DocumentSessionStateMachine()
	{
		State = DocumentSessionState.Created;
	}

	public bool CanTransition(DocumentSessionState next)
	{
		switch (State)
		{
		case DocumentSessionState.Mutating:
			if (next == DocumentSessionState.Mutating || next == DocumentSessionState.Verified || next == DocumentSessionState.Committed || next == DocumentSessionState.RollingBack)
			{
				return true;
			}
			return next == DocumentSessionState.Disposed;
		case DocumentSessionState.RolledBack:
			return next == DocumentSessionState.Disposed;
		case DocumentSessionState.Prepared:
			if (next == DocumentSessionState.Mutating || next == DocumentSessionState.Committed || next == DocumentSessionState.RollingBack)
			{
				return true;
			}
			return next == DocumentSessionState.Disposed;
		case DocumentSessionState.Verified:
			if (next == DocumentSessionState.Verified || next == DocumentSessionState.Committed || next == DocumentSessionState.RollingBack)
			{
				return true;
			}
			return next == DocumentSessionState.Disposed;
		case DocumentSessionState.Created:
			if (next != DocumentSessionState.Prepared)
			{
				return next == DocumentSessionState.Disposed;
			}
			return true;
		case DocumentSessionState.RecoveryRequired:
			return next == DocumentSessionState.Disposed;
		case DocumentSessionState.RollingBack:
			if (next != DocumentSessionState.RolledBack)
			{
				return next == DocumentSessionState.RecoveryRequired;
			}
			return true;
		case DocumentSessionState.Committed:
			return next == DocumentSessionState.Disposed;
		default:
			return false;
		}
	}

	public void Transition(DocumentSessionState next)
	{
		if (!CanTransition(next))
		{
			throw new InvalidSessionTransitionException(State, next);
		}
		State = next;
	}
}
