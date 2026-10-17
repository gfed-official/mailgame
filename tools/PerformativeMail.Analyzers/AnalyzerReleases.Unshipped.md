; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
PMSIM001 | Determinism | Error | NoFloatingPointAnalyzer: float, double, decimal and System.Half are banned in the Sim (ADR-0006 rule 1)
