"""Regression tests for exclusions and stale report detection (standard library only)."""

import json
import os
from pathlib import Path
import tempfile
import unittest

from modules import coverage_policy


class CoveragePolicyTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        (self.root / "src").mkdir()
        (self.root / "eng/coverage").mkdir(parents=True)
        (self.root / "test").mkdir()
        self.source = self.root / "src/Guard.cs"
        self.source.write_text("class Guard {\n[ExcludeFromCodeCoverage]\nprivate void Check() { Work(); }\n}\n", encoding="utf-8")
        self.settings = self.root / "test/testconfig.json"
        self.settings.write_text(json.dumps({"commandLineOptions": {"report-trx": True}, "platformOptions": {"resultDirectory": "__RESULTS_DIRECTORY__", "Coverlet": {
            "format": "cobertura,json", "singleHit": False, "useSourceLink": False,
            "include": "[Fixture]*", "excludeByAttribute": "GeneratedCodeAttribute,ExcludeFromCodeCoverageAttribute",
            "excludeByFile": "**/*.g.cs", "includeTestAssembly": False, "skipAutoProps": True
        }}}), encoding="utf-8")
        self.approve()

    def approve(self):
        allowed = {key: {"sha256": digest, "reason": "Native platform guard"}
                   for key, digest in coverage_policy.exclusions(self.root).items()}
        (self.root / "test/coverage-exclusions.json").write_text(json.dumps(allowed), encoding="utf-8")

    def test_approved_guard_passes(self):
        coverage_policy.check_policy(self.root, {"Fixture.dll"})

    def test_adding_an_unapproved_exclusion_fails(self):
        self.source.write_text(self.source.read_text(encoding="utf-8") + "\n[GeneratedCode(\"tool\", \"1\")]\nvoid Hidden() { Work(); }\n", encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "source exclusions changed"):
            coverage_policy.check_policy(self.root, {"Fixture.dll"})

    def test_expanding_an_approved_guard_fails(self):
        self.source.write_text(self.source.read_text(encoding="utf-8").replace("Work();", "Work(); RegisterEverything();"), encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "source exclusions changed"):
            coverage_policy.check_policy(self.root, {"Fixture.dll"})

    def test_a_comment_or_a_new_layout_keeps_an_approved_guard(self):
        self.source.write_text("class Guard {\n    [ExcludeFromCodeCoverage]\n    private void Check()\n    {\n"
                               "        // ReSharper disable once SomeInspection — the reason.\n"
                               "        Work( ); /* trailing */\n    }\n}\n", encoding="utf-8")
        coverage_policy.check_policy(self.root, {"Fixture.dll"})

    def test_a_changed_literal_in_an_approved_guard_fails(self):
        self.source.write_text(self.source.read_text(encoding="utf-8").replace("Work();", 'Work("a b");'), encoding="utf-8")
        self.approve()
        self.source.write_text(self.source.read_text(encoding="utf-8").replace('"a b"', '"a  b"'), encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "source exclusions changed"):
            coverage_policy.check_policy(self.root, {"Fixture.dll"})

    def test_joining_two_words_in_an_approved_guard_fails(self):
        self.source.write_text(self.source.read_text(encoding="utf-8").replace("private void", "privatevoid"), encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "source exclusions changed"):
            coverage_policy.check_policy(self.root, {"Fixture.dll"})

    def test_expanding_the_file_filter_fails(self):
        self.settings.write_text(self.settings.read_text(encoding="utf-8").replace("**/*.g.cs", "**/*.cs"), encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "excludeByFile"):
            coverage_policy.check_policy(self.root, {"Fixture.dll"})

    def test_a_generated_filename_cannot_hide_hand_maintained_source(self):
        self.source.rename(self.source.with_suffix(".g.cs"))
        with self.assertRaisesRegex(ValueError, "filename"):
            coverage_policy.check_policy(self.root, {"Fixture.dll"})

    def test_duplicate_local_reports_fail(self):
        reports = [self.root / "Fixture.Tests/old/coverage.json", self.root / "Fixture.Tests/new/coverage.json"]
        with self.assertRaisesRegex(ValueError, "multiple reports"):
            coverage_policy.check_report_layout(self.root, reports, {"Fixture.Tests"})

    def test_separate_ci_matrix_artifacts_can_merge(self):
        reports = [self.root / "matrix-a/Fixture.Tests/id/coverage.json", self.root / "matrix-b/Fixture.Tests/id/coverage.json"]
        coverage_policy.check_report_layout(self.root, reports, {"Fixture.Tests"})

    def test_an_old_report_in_the_legacy_directory_fails(self):
        with self.assertRaisesRegex(ValueError, "outside a known"):
            coverage_policy.check_report_layout(self.root, [self.root / "old-guid/coverage.json"], {"Fixture.Tests"})

    def test_missing_test_project_reports_fail(self):
        with self.assertRaisesRegex(ValueError, "missing test project"):
            coverage_policy.check_report_layout(self.root, [], {"Fixture.Tests"})

    def test_a_plain_test_rerun_invalidates_older_coverage_even_after_artifact_download(self):
        project = self.root / "Fixture.Tests"
        project.mkdir()
        report = project / "coverage.101126120000000.json"
        report.write_text("{}", encoding="utf-8")
        (project / "run.trx").write_text('<TestRun><Times start="2026-11-10T12:01:00Z" /><ResultSummary><Counters total="1" failed="0" /></ResultSummary></TestRun>', encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "predates the latest test run"):
            coverage_policy.check_report_freshness(report, project)

    def test_current_coverage_matches_its_trx_receipt(self):
        project = self.root / "Fixture.Tests"
        project.mkdir()
        report = project / "coverage.101126120100000.json"
        report.write_text("{}", encoding="utf-8")
        (project / "run.trx").write_text('<TestRun><Times start="2026-11-10T12:00:00Z" /><ResultSummary><Counters total="1" failed="0" /></ResultSummary></TestRun>', encoding="utf-8")
        coverage_policy.check_report_freshness(report, project)

    def test_a_failed_latest_test_run_cannot_pass_the_coverage_gate(self):
        project = self.root / "Fixture.Tests"
        project.mkdir()
        report = project / "coverage.101126120100000.json"
        report.write_text("{}", encoding="utf-8")
        (project / "run.trx").write_text('<TestRun><Times start="2026-11-10T12:00:00Z" /><ResultSummary><Counters total="1" failed="1" /></ResultSummary></TestRun>', encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "latest test run failed"):
            coverage_policy.check_report_freshness(report, project)

    def test_a_filter_matching_no_tests_cannot_reuse_a_previous_coverage_report(self):
        project = self.root / "Fixture.Tests"
        project.mkdir()
        report = project / "coverage.101126120100000.json"
        report.write_text("{}", encoding="utf-8")
        (project / "run.trx").write_text('<TestRun><Times start="2026-11-10T12:00:00Z" /><ResultSummary><Counters total="0" failed="0" /></ResultSummary></TestRun>', encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "no test results"):
            coverage_policy.check_report_freshness(report, project)

    def test_command_line_config_cannot_override_the_reviewed_exclusions(self):
        document = json.loads(self.settings.read_text(encoding="utf-8"))
        document["commandLineOptions"]["coverlet-exclude"] = "[Fixture]*"
        self.settings.write_text(json.dumps(document), encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "command-line configuration overrides"):
            coverage_policy.check_policy(self.root, {"Fixture.dll"})


class ExclusionScannerTests(unittest.TestCase):
    """The scanner reads code, not lines: every spelling of an exclusion must reach the reviewed allowlist."""

    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        (self.root / "src").mkdir()

    def scan(self, text):
        (self.root / "src/Unit.cs").write_text(text, encoding="utf-8")
        return coverage_policy.exclusions(self.root)

    def test_an_attribute_list_shared_with_other_attributes_is_found(self):
        found = self.scan('class A {\n[Obsolete("x"), System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]\n'
                          "void Hidden() { Work(); }\n}\n")
        self.assertEqual(["src/Unit.cs:Hidden"], list(found))

    def test_a_second_attribute_on_the_declaration_line_is_found(self):
        found = self.scan("class A {\n[Obsolete] [ExcludeFromCodeCoverage] void Hidden() { Work(); }\n}\n")
        self.assertEqual(["src/Unit.cs:Hidden"], list(found))

    def test_stacked_exclusions_on_one_method_are_one_guard(self):
        found = self.scan('class A {\n[GeneratedCode("t", "1")]\n[ExcludeFromCodeCoverage]\nvoid Guard() { }\n}\n')
        self.assertEqual(["src/Unit.cs:Guard"], list(found))

    def test_a_generic_method_is_named_without_its_type_arguments(self):
        found = self.scan("class A {\n[ExcludeFromCodeCoverage]\nvoid Guard<T>(T value) { }\n}\n")
        self.assertEqual(["src/Unit.cs:Guard"], list(found))

    def test_assembly_and_module_targets_are_rejected(self):
        for target in ("assembly", "module"):
            with self.subTest(target), self.assertRaisesRegex(ValueError, "assembly or module"):
                self.scan(f"[{target}: ExcludeFromCodeCoverage]\nclass A {{ void M() {{ }} }}\n")

    def test_an_alias_cannot_hide_the_attribute(self):
        with self.assertRaisesRegex(ValueError, "outside an attribute list"):
            self.scan("using Skip = System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute;\n"
                      "class A {\n[Skip]\nvoid Hidden() { }\n}\n")

    def test_type_property_and_bodyless_exclusions_are_rejected(self):
        sources = [
            "[ExcludeFromCodeCoverage]\nclass A { void M() { } }\n",
            "class A {\n[ExcludeFromCodeCoverage]\nint P { get; }\n}\n",
            "abstract class A {\n[ExcludeFromCodeCoverage]\nabstract void M();\n}\n",
        ]
        for source in sources:
            with self.subTest(source), self.assertRaisesRegex(ValueError, "method-level"):
                self.scan(source)

    def test_comments_and_literals_are_not_code(self):
        found = self.scan('''class A {
    // [ExcludeFromCodeCoverage]
    /* [GeneratedCode("t", "1")] */
    const string One = "[ExcludeFromCodeCoverage]";
    const string Two = @"[GeneratedCode] ""quoted"" ";
    string Three(int x) => $"{x} [CompilerGenerated] {(x > 0 ? "[GeneratedCode]" : "}")}";
    const string Four = """
        [ExcludeFromCodeCoverage]
        """;
    const char Quote = '"';
    const string Five = "[ExcludeFromCodeCoverage]";
}
''')
        self.assertEqual({}, found)

    def test_brackets_inside_a_justification_do_not_end_the_attribute_list(self):
        found = self.scan('class A {\n[ExcludeFromCodeCoverage(Justification = "see [x]")]\nvoid Hidden() { Work(); }\n}\n')
        self.assertEqual(["src/Unit.cs:Hidden"], list(found))

    def test_braces_inside_strings_do_not_end_the_guard(self):
        guard = 'class A {\n[ExcludeFromCodeCoverage]\nvoid Guard() { var s = "}"; Work(); }\n}\n'
        original = self.scan(guard)
        extended = self.scan(guard.replace("Work(); }", "Work(); Register(); }"))
        self.assertNotEqual(original, extended)

    def test_an_expression_bodied_guard_ends_at_its_semicolon(self):
        source = "class A {\n[ExcludeFromCodeCoverage]\nvoid Guard() => Work();\nvoid Next() { Other(); }\n}\n"
        original = self.scan(source)
        self.assertEqual(["src/Unit.cs:Guard"], list(original))
        self.assertEqual(original, self.scan(source.replace("Other()", "Another()")))
        self.assertNotEqual(original, self.scan(source.replace("=> Work();", "=> Work() && Register();")))


class RepositoryPathTests(unittest.TestCase):
    def test_runners_on_different_operating_systems_name_one_file(self):
        paths = [
            "/home/runner/work/Sharp.Messenger/Sharp.Messenger/src/Sharp.Messenger/Consume/Pump.cs",
            "/Users/runner/work/Sharp.Messenger/Sharp.Messenger/src/Sharp.Messenger/Consume/Pump.cs",
            "D:\\a\\Sharp.Messenger\\Sharp.Messenger\\src\\Sharp.Messenger\\Consume\\Pump.cs",
        ]
        self.assertEqual({"src/Sharp.Messenger/Consume/Pump.cs"}, {coverage_policy.repository_path(path) for path in paths})

    def test_the_innermost_source_root_wins(self):
        path = "/home/me/src/Sharp.Messenger/src/Sharp.Messenger/Pump.cs"
        self.assertEqual("src/Sharp.Messenger/Pump.cs", coverage_policy.repository_path(path))

    def test_files_outside_a_project_keep_their_resolved_path(self):
        path = "/tmp/elsewhere/Pump.cs"
        self.assertEqual(os.path.normcase(os.path.realpath(path)), coverage_policy.repository_path(path))


if __name__ == "__main__":
    unittest.main()
