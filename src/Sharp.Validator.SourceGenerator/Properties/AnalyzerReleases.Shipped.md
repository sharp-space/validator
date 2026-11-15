; Shipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

## Release 1.0.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
SVL001 | Sharp.Validator | Warning | Validator is private, protected, or file-local (or nested in one), so AddValidators skips it.
SVL010 | Sharp.Validator | Info | Literal Regex pattern runs interpreted under NativeAOT; convert it to a [GeneratedRegex] method. Reported when PublishAot is true.
SVL011 | Sharp.Validator | Warning | Cross-property target is not a member chain, so it is compiled at runtime. Reported when EnableTrimAnalyzer is true.
