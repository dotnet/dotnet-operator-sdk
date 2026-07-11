; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------------------------------------------------------------------------
KOG001  | KubeOps.Generator | Error | Controller declares both [LabelSelector] and [FieldSelector]
KOG002  | KubeOps.Generator | Warning | Conflicting generated registration class names; the conflicting referenced registration is skipped. Set the KubeOpsGeneratorNamespace MSBuild property to disambiguate.
