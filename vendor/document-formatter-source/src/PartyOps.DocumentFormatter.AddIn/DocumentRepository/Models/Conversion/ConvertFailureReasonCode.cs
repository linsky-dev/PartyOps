namespace DocumentRepository.Models.Conversion;

public enum ConvertFailureReasonCode
{
	NoActiveDocument,
	DocumentNeedsSave,
	DocumentAnalysisUnavailable,
	TaskContextUnavailable,
	ConfigurationUnavailable,
	OutputFolderInvalid,
	PageCountUnavailable,
	PageRangeFormatInvalid,
	PageRangeOrderInvalid,
	PageSelectionEmpty,
	PageSelectionNotNumeric,
	PageOutsideDocument,
	UnsupportedFormat,
	ImageResourceBudgetExceeded,
	ImagePageReadFailed,
	OutputWriteFailed,
	HostExportFailed,
	DocxReplacementFailed,
	OutputValidationFailed,
	UnexpectedFailure,
	ScannedPdfUnsupported,
	PdfEncryptedOrProtected
}
