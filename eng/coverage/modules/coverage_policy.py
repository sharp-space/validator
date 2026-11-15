"""Enforce the reviewed measurement scope before collecting or verifying coverage."""

from __future__ import annotations

from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import re
import xml.etree.ElementTree as ET


EXCLUDING_ATTRIBUTES = {"ExcludeFromCodeCoverage", "GeneratedCode", "CompilerGenerated"}
EXCLUDING_NAME = re.compile(r"\b(?:" + "|".join(sorted(EXCLUDING_ATTRIBUTES)) + r")(?:Attribute)?\b")
ATTRIBUTE_ITEM = re.compile(r"\s*(?:(\w+)\s*:(?!:)\s*)?(?:global\s*::\s*)?(?:\w+\s*\.\s*)*(\w+)")
TYPE_KEYWORD = re.compile(r"\b(?:class|struct|interface|enum|record|delegate|namespace)\b")
METHOD_NAME = re.compile(r"(\w+)\s*(?:<[^<>()]*>)?\s*\(")


def _char_end(text: str, start: int) -> int:
    if text.startswith("\\", start + 1):
        end = text.find("'", start + 3)
        return len(text) if end < 0 else end + 1
    return start + 3 if text.startswith("'", start + 2) else start + 1


def _hole_end(text: str, index: int) -> int:
    depth = 1
    while index < len(text):
        char = text[index]
        if char == "{":
            depth += 1
        elif char == "}":
            depth -= 1
            if depth == 0:
                return index + 1
        elif char in "$@\"":
            end = _string_end(text, index)
            if end is not None:
                index = end
                continue
        elif char == "'":
            index = _char_end(text, index)
            continue
        index += 1
    return len(text)


def _string_end(text: str, start: int) -> int | None:
    """Return the end of the string literal beginning at `start`, or None when no literal begins there."""
    index = start
    interpolated = verbatim = False
    while index < len(text) and text[index] in "$@":
        interpolated |= text[index] == "$"
        verbatim |= text[index] == "@"
        index += 1
    if index >= len(text) or text[index] != '"':
        return None
    quotes = 0
    while index + quotes < len(text) and text[index + quotes] == '"':
        quotes += 1
    if quotes == 2:
        return index + 2
    if quotes >= 3:
        end = text.find('"' * quotes, index + quotes)
        return len(text) if end < 0 else end + quotes
    index += 1
    while index < len(text):
        char = text[index]
        if char == '"':
            if verbatim and text.startswith('""', index):
                index += 2
                continue
            return index + 1
        if char == "\\" and not verbatim:
            index += 2
            continue
        if interpolated and char == "{":
            if text.startswith("{{", index):
                index += 2
                continue
            index = _hole_end(text, index + 1)
            continue
        index += 1
    return len(text)


def _mask(text: str) -> str:
    """Blank comments and string or character literals, keeping offsets and newlines, so scans see only code."""
    masked = list(text)
    index = 0
    while index < len(text):
        char = text[index]
        if text.startswith("//", index):
            end = text.find("\n", index)
            end = len(text) if end < 0 else end
        elif text.startswith("/*", index):
            end = text.find("*/", index + 2)
            end = len(text) if end < 0 else end + 2
        elif char == "'":
            end = _char_end(text, index)
        elif char in "$@\"":
            end = _string_end(text, index)
            if end is None:
                index += 1
                continue
        else:
            index += 1
            continue
        for position in range(index, end):
            if masked[position] not in "\r\n":
                masked[position] = " "
        index = end
    return "".join(masked)


def _code_digest(text: str) -> str:
    """Hash a guard's code, not its spelling: comments are dropped and layout is ignored, literals are kept as written.

    A space survives only between two word characters, where it separates tokens, so reformatting a guard or adding
    a comment to it leaves the digest alone, and any change to its tokens or literals does not.
    """
    pieces: list[str] = []
    pending_space = False
    index = 0
    while index < len(text):
        char = text[index]
        end = None
        if text.startswith("//", index):
            end = text.find("\n", index)
            index = len(text) if end < 0 else end
            pending_space = True
            continue
        if text.startswith("/*", index):
            end = text.find("*/", index + 2)
            index = len(text) if end < 0 else end + 2
            pending_space = True
            continue
        if char.isspace():
            index += 1
            pending_space = True
            continue
        if char == "'":
            end = _char_end(text, index)
        elif char in "$@\"":
            end = _string_end(text, index)
        token = text[index:end] if end is not None else char
        if pending_space and pieces and _is_word(pieces[-1][-1]) and _is_word(token[0]):
            pieces.append(" ")
        pieces.append(token)
        pending_space = False
        index = end if end is not None else index + 1
    return hashlib.sha256("".join(pieces).encode()).hexdigest()


def _is_word(char: str) -> bool:
    return char.isalnum() or char in "_@"


def _matching(masked: str, index: int) -> int:
    opening = masked[index]
    closing = {"[": "]", "(": ")", "{": "}"}[opening]
    depth = 0
    for position in range(index, len(masked)):
        if masked[position] == opening:
            depth += 1
        elif masked[position] == closing:
            depth -= 1
            if depth == 0:
                return position
    raise ValueError("unbalanced brackets")


def _items(masked: str, start: int, end: int) -> list[str]:
    items, depth, current = [], 0, start
    for position in range(start, end):
        char = masked[position]
        if char in "([{":
            depth += 1
        elif char in ")]}":
            depth -= 1
        elif char == "," and depth == 0:
            items.append(masked[current:position])
            current = position + 1
    items.append(masked[current:end])
    return items


def _skip_space(masked: str, index: int) -> int:
    while index < len(masked) and masked[index].isspace():
        index += 1
    return index


def exclusions(root: Path) -> dict[str, str]:
    """Map every hand-written coverage exclusion to the hash of the guard it covers.

    A guard is the whole excluded method, from its excluding attribute list to the end of its body. Attribute lists
    are found in code only (comments and literals are masked), so combined lists, same-line attributes and aliases
    cannot hide a method, and assembly or module targets are rejected outright.
    """
    result = {}
    for source in sorted((root / "src").rglob("*.cs")):
        if {"obj", "bin"}.intersection(source.parts):
            continue
        relative = source.relative_to(root).as_posix()
        if source.name.endswith(".g.cs"):
            raise ValueError(f"hand-maintained source would bypass coverage by filename: {relative}")
        text = source.read_text(encoding="utf-8")
        masked = _mask(text)
        try:
            result.update(_source_exclusions(relative, text, masked, result))
        except ValueError as error:
            raise ValueError(f"{error}: {relative}") from None
    return result


def _source_exclusions(relative: str, text: str, masked: str, known: dict[str, str]) -> dict[str, str]:
    found: dict[str, str] = {}
    spans = []
    declarations = set()
    for opening in (match.start() for match in re.finditer(r"\[", masked)):
        closing = _matching(masked, opening)
        excluding = []
        for item in _items(masked, opening + 1, closing):
            parsed = ATTRIBUTE_ITEM.match(item)
            if parsed and parsed.group(2).removesuffix("Attribute") in EXCLUDING_ATTRIBUTES:
                excluding.append(parsed.group(1))
        if not excluding:
            continue
        spans.append((opening, closing))
        if {"assembly", "module"}.intersection(excluding):
            raise ValueError("assembly or module-level exclusions are not allowed")
        declaration = _skip_space(masked, closing + 1)
        while masked.startswith("[", declaration):
            declaration = _skip_space(masked, _matching(masked, declaration) + 1)
        if declaration in declarations:
            continue
        declarations.add(declaration)
        position, depth = declaration, 0
        while position < len(masked):
            char = masked[position]
            if char == "(":
                depth += 1
            elif char == ")":
                depth -= 1
            elif depth == 0 and (char in "{;" or masked.startswith("=>", position)):
                break
            position += 1
        else:
            raise ValueError("unterminated excluded declaration")
        header = masked[declaration:position]
        name = METHOD_NAME.search(header)
        if masked[position] == ";" or TYPE_KEYWORD.search(header) or name is None:
            raise ValueError("only reviewed method-level exclusions with a body are allowed")
        if masked[position] == "{":
            end = _matching(masked, position) + 1
        else:
            end, depth = position + 2, 0
            while end < len(masked) and not (masked[end] == ";" and depth == 0):
                depth += masked[end] in "([{"
                depth -= masked[end] in ")]}"
                end += 1
            end += 1
        # The allowlist covers the entire annotated guard, so extending a guard to hide registration logic requires
        # an explicit policy update. Comments and layout don't count: a cleanup pass or a suppression comment changes
        # no code, and an update it forced would teach reviewers to approve hashes unread.
        key = f"{relative}:{name.group(1)}"
        if key in found or key in known:
            raise ValueError(f"ambiguous excluded method {key}")
        found[key] = _code_digest(text[opening:end])
    for match in EXCLUDING_NAME.finditer(masked):
        if not any(opening < match.start() < closing for opening, closing in spans):
            raise ValueError("a coverage-exclusion attribute is referenced outside an attribute list")
    return found


def repository_path(source_path: str) -> str:
    """Key a coverage source by its repository-relative path, so reports from other operating systems merge.

    Coverlet records absolute build paths. Reports from Windows, macOS and Linux runners name the same file
    differently, and tests that run on one operating system only must still cover that file for the whole suite.
    """
    parts = source_path.replace("\\", "/").split("/")
    for index in range(len(parts) - 3, -1, -1):
        if parts[index] == "src" and parts[index + 1].startswith("Sharp."):
            return "/".join(parts[index:])
    return os.path.normcase(os.path.realpath(source_path))


def check_policy(root: Path, expected_modules: set[str]) -> None:
    document = json.loads((root / "test/testconfig.json").read_text(encoding="utf-8"))
    if set(document) != {"platformOptions", "commandLineOptions"} or document["commandLineOptions"] != {"report-trx": True}:
        raise ValueError("command-line configuration overrides require coverage policy review")
    platform = document["platformOptions"]
    if set(platform) != {"Coverlet", "resultDirectory"} or platform["resultDirectory"] != "__RESULTS_DIRECTORY__":
        raise ValueError("MTP configuration differs from the shared result/report policy")
    configuration = platform["Coverlet"]
    expected = {
        "format": "cobertura,json",
        "singleHit": False,
        "useSourceLink": False,
        "excludeByAttribute": "GeneratedCodeAttribute,ExcludeFromCodeCoverageAttribute",
        "excludeByFile": "**/*.g.cs",
        "includeTestAssembly": False,
        "skipAutoProps": True,
    }
    for key, value in expected.items():
        if configuration.get(key) != value:
            raise ValueError(f"unreviewed measurement policy: {key}")
    if set(configuration) != set(expected) | {"include"}:
        raise ValueError("additional or missing coverage configuration requires review")
    includes = {f"[{module.removesuffix('.dll')}]*" for module in expected_modules}
    if set((configuration.get("include") or "").split(",")) != includes:
        raise ValueError("assembly include filters differ from the required shipped modules")
    allowed = json.loads((root / "test/coverage-exclusions.json").read_text(encoding="utf-8"))
    if any(not item.get("reason", "").strip() for item in allowed.values()):
        raise ValueError("every approved exclusion needs a reason")
    actual = exclusions(root)
    if actual != {key: item["sha256"] for key, item in allowed.items()}:
        raise ValueError("source exclusions changed; review test/coverage-exclusions.json instead of hiding uncovered code")


def check_report_layout(root: Path, reports: list[Path], expected_projects: set[str]) -> None:
    seen = set()
    projects = set()
    for report in reports:
        relative = report.relative_to(root)
        matching = [i for i, part in enumerate(relative.parts) if part in expected_projects]
        if len(matching) != 1:
            raise ValueError(f"report outside a known test project's results directory: {relative}")
        index = matching[0]
        project = relative.parts[index]
        scope = relative.parts[:index + 1]
        if scope in seen:
            raise ValueError(f"multiple reports for the same project/run directory: {relative}; rerun the complete suite")
        seen.add(scope)
        projects.add(project)
    missing = expected_projects - projects
    if missing:
        raise ValueError(f"missing test project reports: {', '.join(sorted(missing))}")


def check_report_freshness(report: Path, project_directory: Path) -> None:
    # Artifact downloads rewrite mtimes, so compare the UTC run times stored in filenames and TRX instead.
    stamp = re.search(r"\.(\d{15})\.json$", report.name)
    if stamp is None:
        raise ValueError(f"coverage report has no MTP UTC timestamp: {report}")
    created = datetime.strptime(stamp.group(1), "%d%m%y%H%M%S%f").replace(tzinfo=timezone.utc)
    runs = []
    for trx in project_directory.rglob("*.trx"):
        receipt = ET.parse(trx)
        times = receipt.find(".//{*}Times")
        if times is not None and times.get("start"):
            runs.append((datetime.fromisoformat(times.get("start").replace("Z", "+00:00")), receipt))
    if not runs:
        raise ValueError(f"no TRX run receipt for coverage report: {report}")
    latest_start, latest_receipt = max(runs, key=lambda run: run[0])
    counters = latest_receipt.find(".//{*}Counters")
    if counters is None or int(counters.get("total", "0")) == 0:
        raise ValueError(f"latest test run has no test results: {project_directory}")
    if any(int(counters.get(key, "0")) > 0 for key in ["failed", "error", "timeout", "aborted"]):
        raise ValueError(f"latest test run failed: {project_directory}")
    if created < latest_start:
        raise ValueError(f"coverage predates the latest test run: {report}; collect coverage again")
