## Summary

<!-- What changes and why, in a sentence or two. Link the issue it resolves, for example "Fixes #123".
     The title of this pull request appears as written in the release notes. -->

## Checklist

- [ ] The title reads as a release note
- [ ] `dotnet build Sharp.Validator.slnx -c Release -warnaserror --no-incremental` passes
- [ ] `dotnet test --solution Sharp.Validator.slnx -c Release --no-build --coverlet` passes, and every behavior change
  comes with a test
- [ ] `python3 eng/coverage/verify-coverage.py test/artifacts/coverage` reports 100% line and branch coverage
- [ ] A change to the source generator, the accessors, or registration keeps the Native AOT canary,
  `test/Sharp.Validator.AotTests`, publishing and running
- [ ] Public API changes have XML docs, and `README.md` describes the new behavior
- [ ] A new diagnostic is recorded in `AnalyzerReleases.Unshipped.md` and has a page in `doc/diagnostics/`
- [ ] No breaking change, or one agreed in an issue for the next major version
