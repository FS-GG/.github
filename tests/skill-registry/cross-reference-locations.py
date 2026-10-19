#!/usr/bin/env python3
"""Remote source navigation is not a receiver-local materialization dependency."""
import copy
import pathlib
import runpy
import tempfile
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[2]
CHECKER = ROOT / 'scripts/fsgg-skill-registry-check'
module = runpy.run_path(str(CHECKER))


class CrossReferenceLocations(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix='skill-reference-locations-')
        self.root = pathlib.Path(self.temp.name)
        self.skill = self.root / '.github/.claude/skills/driver'
        self.skill.mkdir(parents=True)
        (self.skill / 'references').mkdir()
        self.rows = [
            {'id': 'driver', 'source': '.github/.claude/skills/driver/SKILL.md', 'materializes-when': 'always'},
            {'id': 'operator', 'materializes-when': 'false'},
        ]

    def tearDown(self):
        self.temp.cleanup()

    def check(self, text, *, checker=module):
        (self.skill / 'SKILL.md').write_text('# Driver\n')
        (self.skill / 'references/route.md').write_text(text)
        return checker['cross_references'](self.rows, str(self.root))

    def test_remote_markdown_autolink_and_bare_urls_need_no_local_payload(self):
        for root in ('.agents', '.claude'):
            for scheme in ('https', 'http', 'HTTPS'):
                url = f'{scheme}://github.com/FS-GG/.github/blob/main/{root}/skills/operator/SKILL.md'
                for text in (f'[operator]({url})', f'<{url}>', url):
                    with self.subTest(text=text):
                        self.assertEqual([], self.check(text))

    def test_remote_unregistered_navigation_is_not_a_missing_local_row(self):
        self.assertEqual([], self.check('https://example.test/.agents/skills/not-shipped/SKILL.md'))

    def test_local_materialization_dependency_still_refuses(self):
        for root in ('.agents', '.claude'):
            findings = self.check(f'Run `{root}/skills/operator/script.py`.')
            self.assertEqual(['operator'], [x['declared'] for x in findings])
            self.assertIn('does not imply', findings[0]['detail'])

    def test_url_on_same_line_does_not_hide_a_local_missing_dependency(self):
        text = ('[source](https://example.test/.agents/skills/operator/SKILL.md) '
                'then run `.claude/skills/not-shipped/script.py`.')
        findings = self.check(text)
        self.assertEqual(['not-shipped'], [x['declared'] for x in findings])
        self.assertIn('no registry row', findings[0]['detail'])

    def test_causal_old_classifier_reports_the_false_materialization_dependency(self):
        # Restore exactly the old scanner in memory. The same real cross-reference check
        # must reject this fixture, proving the positive test reaches the changed branch.
        old = copy.copy(module)
        check = module['cross_references']
        original = check.__globals__['sibling_path_references']
        check.__globals__['sibling_path_references'] = lambda text: set(module['_SIBLING_PATH_RE'].findall(text))
        try:
            findings = self.check('[source](https://example.test/.agents/skills/operator/SKILL.md)', checker=old)
            self.assertEqual(['operator'], [x['declared'] for x in findings])
        finally:
            check.__globals__['sibling_path_references'] = original


if __name__ == '__main__':
    unittest.main()
