# 源码模块索引

主插件命名空间以 `DocumentRepository` 为主。以下是恢复后的高层模块。

## Commands（8 个类型文件）

`CommandExecutor`, `ConvertCommand`, `FormatCommand`, `ICommand`, `PdfToWordCommand`, `RedHeaderCommand`, `RenameCommand`, `ReplaceCommand`

## Pipelines（9 个类型文件）

`CompilationFormattingPipeline`, `ConvertPipeline`, `FormatPipeline`, `IPipelineStep`, `PdfToWordPipeline`, `PipelineRunner`, `RedHeaderPipeline`, `RenamePipeline`, `ReplacePipeline`

## Services（400 个类型文件）

`AddInStartupCoordinator`, `ApplicationDataPaths`, `AtomicFileService`, `AtomicFileWriteResult`, `AttachmentDetector`, `AttachmentFormattingPlan`, `AttachmentFormattingPlanBuilder`, `AttachmentListNormalizationService`, `AuthorizationDecisionAdapter`, `CellVerticalAlignment`, `ChineseContextPunctuationNormalizer`, `ChinesePunctuationNormalizationResult`, `ChinesePunctuationNormalizer`, `CleanupStagePrecheck`, `ColumnReadingSegment`, `ColumnRegionSorter`, `ComObjectRelease`, `ComOwnershipAttribute`, `ComOwnershipKind`, `CompilationArticleListBuilder`, `CompilationArticleTitleResolution`, `CompilationArticleTitleResolver`, `CompilationBoundaryService`, `CompilationDocumentScanResult`, `CompilationDocumentScanner`, `CompilationFailurePresentation`, `CompilationFormatEntryDispatcher`, `CompilationFormatRouter`, `CompilationManifestService`, `CompilationMarkerParser`, `CompilationMutationWarmupService`, `CompilationOperationVerificationService`, `CompilationRouteResolution`, `CompilationSeparatorService`, `CompilationTocFieldFact`, `CompilationTocOptionsValidator`, `CompilationTocOwnedRegion`, `CompilationTocOwnershipAnalyzer`, `CompilationTocOwnershipService`, `CompilationTocOwnershipSnapshot`, `CompilationTocRangeClassifier`, `CompilationTocService`, `CompilationTocUpdateException`, `CompilationTocUpdateExecutor`, `CompilationTocUpdateService`, `ConfigurationExportResult`, `ConfigurationFileSnapshot`, `ConfigurationImportInspection`, `ConfigurationImportResult`, `ConfigurationMigrationResult`, `ConfigurationMigrationService`, `ConfigurationMigrationTransaction`, `ConfigurationPackageCodec`, `ConfigurationPackageEntry`, `ConfigurationPackageManifest`, `ConfigurationPackagePayload`, `ConfigurationTransferService`, `ConvertDocumentAnalyzer`, `ConvertExecutor`, `ConvertFailureClassifier`, `ConvertFailurePresentation`, `ConvertPlanBuilder`, `ConvertSettingsService`, `ConvertVerifier`, `CrossPageTableConservationReport`, `CrossPageTableContinuationEvidence`, `CrossPageTableContinuationOptions`, `CrossPageTableContinuationPlan`, `CrossPageTableContinuationPlanner`, `CrossPageTableContinuationResult`, `CrossPageTableContinuationVerifier`, `CrossPageTableId`, `CrossPageTableRowSignature`, `CrossPageTableRowSource`, `DecorationDetectionResult`, `DecorationOccurrence`, `DecorationRegionDetector`, `Detector`, `DocumentAnalysisResult`, `DocumentAnalysisService`

## Models（184 个类型文件）

`CommandResult`, `CompilationArticleInfo`, `CompilationArticleList`, `CompilationConfirmationRequest`, `CompilationConfirmationResult`, `CompilationExistingTocPolicy`, `CompilationFormatFailure`, `CompilationFormatFailureReasonCode`, `CompilationFormatFailureStage`, `CompilationManifest`, `CompilationRouteDecision`, `CompilationRouteFacts`, `CompilationRouteKind`, `CompilationSeparatorRecord`, `CompilationTocEntry`, `CompilationTocLeaderStyle`, `CompilationTocOptions`, `CompilationTocOutcome`, `CompilationTocRangeRelation`, `CompilationTocTitleAlignment`, `ConvertConflictDecision`, `ConvertDocumentAnalysis`, `ConvertExecutionPlan`, `ConvertFailureReasonCode`, `ConvertFailureStage`, `ConvertFormat`, `ConvertOperationException`, `ConvertOptions`, `ConvertRequest`, `ConvertResult`, `ConvertSameNamePolicy`, `ConvertSaveLocation`, `CustomDocumentPropertySnapshot`, `DocumentElement`, `DocumentElementList`, `DocumentMutationKind`, `DocumentMutationPlan`, `DocumentRiskClassifier`, `DocumentRiskProfile`, `DocumentRiskState`, `DocumentSafetyDisposition`, `DocumentSnapshot`, `DocumentSnapshotMismatchException`, `DocumentSnapshotMismatchKind`, `DocxConvertMode`, `DocxReplaceCurrentResult`, `ElementType`, `ExecutionAdmissionResult`, `ExecutionDecision`, `ExecutionDecisionOutcome`, `ExecutionDecisionReasonCode`, `ExecutionDecisionSource`, `FeatureDescriptor`, `FeatureEntryFailureReasonCode`, `FeatureEntryOperationException`, `FeatureExecutionOptions`, `FeatureMessageKind`, `FeatureRegistryValidationResult`, `FeatureSmokeTestDescriptor`, `FormatContext`, `FormatExecutionPlan`, `FormatExecutionScope`, `FormatFailureReasonCode`, `FormatFailureStage`, `FormatMutationOperation`, `FormatMutationStage`, `FormatOperationException`, `FormatParameterField`, `FormatStageException`, `IDocumentMutation`, `IFeatureUiService`, `ITaskArtifactRegistry`, `ITaskProgressReporter`, `ImageAnalysisSnapshot`, `ImageConversionBudget`, `ImageExportMode`, `ImageFileFormat`, `ImageFormattingExecutionResult`, `ImageFormattingPlan`, `ImageFormattingTarget`

## UI（6 个类型文件）

`CompletionMessageForm`, `RecoveryAssistantForm`, `SimpleTaskProgressFormReporter`, `TimedMessageForm`, `WarningMessageForm`, `WinFormsFeatureUiService`

## 关键入口

- `DocumentRepository/ThisAddIn.cs`：VSTO 生命周期、Word/WPS 宿主接入、启动/关闭。
- `DocumentRepository/Ribbon1.cs`：Ribbon UI 与各功能入口。
- `DocumentRepository/Commands/*`：用户命令层。
- `DocumentRepository/Pipelines/*`：排版、替换、重命名、红头、转换等流水线。
- `DocumentRepository/Services/Formatting/*`：核心公文排版算法。
- `DocumentRepository/Services/Conversion/PdfToWord/*`：PDF 页面分析、表格/版式恢复和输出。
- `DocumentRepository/Services/Features/ProductCapabilityCatalog.cs`：25 条产品能力与真实源码责任类型的闭环契约。
- `DocumentRepository/Services/Auth/*` 与 `CloudAuthManager.cs`：账户/授权服务。

五大功能和 PDF 转 Word 的能力状态、自动化证据与宿主验收边界见 `FEATURE_PARITY_MATRIX.md`。


## NearSource 二次清理

本版本在初始 ILSpy 导出基础上进一步完成：Confuser 控制流 IL 还原、34 个数组常量内联、`-Module-.cs` 删除、反编译 goto 清零、ILSpy IL/override 诊断注释清零、保护模型死分支清理、功能/流水线/服务边界目录闭合，以及优先模块的结构化/语义化整理。最终量化结果见 `artifacts/source-quality-final.json`，历史反混淆基线仍保留在 `recovery` 与工作区上层附件中。
