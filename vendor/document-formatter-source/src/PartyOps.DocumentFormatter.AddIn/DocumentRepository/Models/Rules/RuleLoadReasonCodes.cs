namespace DocumentRepository.Models.Rules;

public static class RuleLoadReasonCodes
{
	public const string Loaded = "rule-store-loaded";

	public const string CreatedDefault = "rule-store-created-default";

	public const string RecoveredDefault = "rule-store-recovered-default";

	public const string InMemoryDefault = "rule-store-in-memory-default";

	public const string Corrupt = "rule-store-corrupt";

	public const string NormalizeFailed = "rule-store-normalize-failed";

	public const string ReadFailed = "rule-store-read-failed";

	public const string CreateDefaultFailed = "rule-store-create-default-failed";

	public const string BackupFailed = "rule-store-backup-failed";

	public const string ResetFailed = "rule-store-reset-failed";
}
