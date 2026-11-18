"""Test analyzer report admission, never substitute these fixtures for Clang results."""
import importlib.util
import json
import pathlib
import plistlib
import sys
import tempfile
import types
import unittest
from unittest.mock import patch

ROOT=pathlib.Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT/'scripts'))
spec=importlib.util.spec_from_file_location('analyzer',ROOT/'scripts/analyze-ios.py')
analyzer=importlib.util.module_from_spec(spec);spec.loader.exec_module(analyzer)


class AnalyzerReportTests(unittest.TestCase):
    def run_case(self,compiler):
        with tempfile.TemporaryDirectory(dir=ROOT/'artifacts') as temporary:
            root=pathlib.Path(temporary);out=root/'artifacts/ios-analysis';out.mkdir(parents=True)
            (out/'results.json').write_text(json.dumps(dict(complete=True,passed=True,runId='old')))
            with patch.object(analyzer,'ROOT',root),patch.object(analyzer,'SOURCES',['one.c','two.c']),patch.object(analyzer,'write_audit_header'),patch.object(analyzer.subprocess,'run',side_effect=compiler),patch.object(sys,'argv',['analyze-ios.py','--zig','compiler.exe','--sdk','sdk']):
                try:analyzer.main()
                except SystemExit as error:self.assertIn(error.code,(0,1))
                except OSError:pass
            return json.loads((out/'results.json').read_text())

    @staticmethod
    def clean(command,**kwargs):
        pathlib.Path(command[command.index('-o')+1]).write_bytes(plistlib.dumps(dict(diagnostics=[])))
        return types.SimpleNamespace(returncode=0)

    def test_complete_current_reports_pass(self):
        report=self.run_case(self.clean)
        self.assertTrue(report['complete'] and report['passed']);self.assertEqual(2,len(report['results']))
        self.assertNotEqual('old',report['runId'])

    def test_missing_report_is_not_clean(self):
        report=self.run_case(lambda *a,**k:types.SimpleNamespace(returncode=0))
        self.assertTrue(report['complete']);self.assertFalse(report['passed'])

    def test_crash_replaces_old_clean_report_with_incomplete(self):
        calls=0
        def compiler(command,**kwargs):
            nonlocal calls
            calls+=1
            if calls==2:raise OSError('injected compiler startup failure')
            return self.clean(command,**kwargs)
        report=self.run_case(compiler)
        self.assertFalse(report['complete'] or report['passed']);self.assertEqual(1,len(report['results']))
        self.assertEqual('two.c',report['currentSource']);self.assertIn('OSError',report['error'])

    def test_diagnostics_fail_even_when_compiler_exits_zero(self):
        def compiler(command,**kwargs):
            pathlib.Path(command[command.index('-o')+1]).write_bytes(plistlib.dumps(dict(diagnostics=[dict(description='test diagnostic')])))
            return types.SimpleNamespace(returncode=0)
        report=self.run_case(compiler)
        self.assertTrue(report['complete']);self.assertFalse(report['passed'])


if __name__=='__main__':unittest.main(verbosity=2)
