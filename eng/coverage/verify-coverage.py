#!/usr/bin/env python3
"""Merge Coverlet JSON reports and require 100% line and branch coverage."""

from __future__ import annotations

import json
import sys
from pathlib import Path

from modules import coverage_policy


EXPECTED_MODULES = {
    "Sharp.Validator.dll",
    "Sharp.Validator.Result.dll",
    "Sharp.Validator.SourceGenerator.dll",
    "Sharp.Validator.CodeFixes.dll",
}


EXPECTED_TEST_PROJECTS = {'Sharp.Validator.CodeFixes.Tests', 'Sharp.Validator.SourceGenerator.Tests', 'Sharp.Validator.Tests', 'Sharp.Validator.Result.Tests'}


def fail(message: str) -> None:
    print(f"FAIL: {message}", file=sys.stderr)
    raise SystemExit(1)


def main() -> None:
    repository = Path(__file__).resolve().parents[2]
    try:
        coverage_policy.check_policy(repository, EXPECTED_MODULES)
    except ValueError as error:
        fail(str(error))
    if sys.argv[1:] == ["--check-policy"]:
        print("COVERAGE POLICY CHECK PASS")
        return
    if len(sys.argv) != 2:
        fail("usage: verify-coverage.py <coverage-results-directory>")

    root = Path(sys.argv[1])
    reports = sorted(root.rglob("coverage*.json"))
    if not reports:
        fail(f"no coverage JSON reports found under {root}")

    try:
        coverage_policy.check_report_layout(root, reports, EXPECTED_TEST_PROJECTS)
        for report in reports:
            project = next(parent for parent in report.parents if parent.name in EXPECTED_TEST_PROJECTS)
            coverage_policy.check_report_freshness(report, project)
    except ValueError as error:
        fail(str(error))

    modules: set[str] = set()
    lines: dict[tuple[str, str, int], int] = {}
    branches: dict[tuple[object, ...], int] = {}

    for report in reports:
        with report.open(encoding="utf-8") as stream:
            document = json.load(stream)

        for module, source_documents in document.items():
            modules.add(module)
            for source_path, classes in source_documents.items():
                normalized_source = coverage_policy.repository_path(source_path)
                for class_name, methods in classes.items():
                    for method_name, coverage in methods.items():
                        for line_number, hits in coverage["Lines"].items():
                            key = (module, normalized_source, int(line_number))
                            lines[key] = max(lines.get(key, 0), int(hits))

                        for branch in coverage["Branches"]:
                            key = (
                                module,
                                normalized_source,
                                class_name,
                                method_name,
                                int(branch["Line"]),
                                int(branch["Offset"]),
                                int(branch["EndOffset"]),
                                int(branch["Path"]),
                                int(branch["Ordinal"]),
                            )
                            branches[key] = max(branches.get(key, 0), int(branch["Hits"]))

    missing_modules = sorted(EXPECTED_MODULES - modules)
    if missing_modules:
        fail(f"missing coverage modules: {', '.join(missing_modules)}")

    empty_modules = sorted(module for module in EXPECTED_MODULES if not any(key[0] == module for key in lines))
    if empty_modules:
        fail(f"modules without instrumented lines: {', '.join(empty_modules)}")

    uncovered_lines = sorted(key for key, hits in lines.items() if hits == 0)
    uncovered_branches = sorted(key for key, hits in branches.items() if hits == 0)

    print(f"line coverage   : {len(lines) - len(uncovered_lines)}/{len(lines)}")
    print(f"branch coverage : {len(branches) - len(uncovered_branches)}/{len(branches)}")

    for module, source_path, line_number in uncovered_lines:
        print(f"UNCOVERED LINE: {module} {source_path}:{line_number}", file=sys.stderr)
    for branch in uncovered_branches:
        module, source_path, class_name, method_name, line_number, offset, end_offset, path, ordinal = branch
        print(
            "UNCOVERED BRANCH: "
            f"{module} {source_path}:{line_number} {class_name}.{method_name} "
            f"offset={offset}->{end_offset} path={path} ordinal={ordinal}",
            file=sys.stderr,
        )

    if uncovered_lines or uncovered_branches:
        fail("coverage must remain 100% for both lines and branches")

    print(f"ALL COVERAGE CHECKS PASS ({len(reports)} reports, {len(modules)} modules)")


if __name__ == "__main__":
    main()
