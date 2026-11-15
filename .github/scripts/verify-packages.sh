#!/usr/bin/env bash
# Asserts the packed .nupkg files actually contain what they promise. The analyzer slot is the critical
# one: a package missing the generator still works under the JIT (reflection fallback) and only fails at
# a consumer's NativeAOT publish — the worst possible place to find out.
set -euo pipefail

packages_dir="${1:?usage: verify-packages.sh <packages-dir>}"

require() {
    local archive="$1" entry="$2"
    # Whole entry names (-Z1), matched exactly (-x): a substring match also accepts the entry nested one folder deeper.
    # No `grep -q`: its early exit would SIGPIPE unzip, which pipefail then reports as a failure.
    if ! unzip -Z1 "$archive" | grep -Fx "$entry" >/dev/null; then
        echo "FAIL: $(basename "$archive") is missing '$entry'" >&2
        exit 1
    fi
    echo "ok  : $(basename "$archive") contains $entry"
}

# NB: a bare *.nupkg glob would also match *.snupkg (the 's' is absorbed by the wildcard).
main_pkg=$(find "$packages_dir" -name 'Sharp.Validator.[0-9]*.nupkg' ! -name '*.snupkg')
result_pkg=$(find "$packages_dir" -name 'Sharp.Validator.Result.[0-9]*.nupkg' ! -name '*.snupkg')

require "$main_pkg" "lib/net10.0/Sharp.Validator.dll"
require "$main_pkg" "lib/net10.0/Sharp.Validator.xml"
require "$main_pkg" "analyzers/dotnet/cs/Sharp.Validator.SourceGenerator.dll"
require "$main_pkg" "analyzers/dotnet/cs/Sharp.Validator.CodeFixes.dll"
require "$main_pkg" "buildTransitive/Sharp.Validator.props"
require "$main_pkg" "README.md"

require "$result_pkg" "lib/net10.0/Sharp.Validator.Result.dll"
require "$result_pkg" "lib/net10.0/Sharp.Validator.Result.xml"
require "$result_pkg" "README.md"

# Symbol packages must exist for both.
ls "$packages_dir"/Sharp.Validator.[0-9]*.snupkg >/dev/null
ls "$packages_dir"/Sharp.Validator.Result.[0-9]*.snupkg >/dev/null
echo "ok  : symbol packages present"

echo "ALL PACKAGE CHECKS PASS"
