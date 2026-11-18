import importlib.util
import pathlib
import sys
import unittest

ROOT=pathlib.Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT/'scripts'))
from ios_log_support import BackThreadLogs
spec=importlib.util.spec_from_file_location('installer',ROOT/'scripts/install-ios.py')
installer=importlib.util.module_from_spec(spec);spec.loader.exec_module(installer)


class DeviceToolsTests(unittest.TestCase):
    def test_existing_data_selects_upgrade(self):
        for identifier in installer.IDENTIFIERS.values():
            self.assertEqual('Upgrade',installer.install_command(identifier,{'Container':'existing-app-data'}))
            self.assertEqual('Install',installer.install_command(identifier,{}))

    def test_unrelated_apps_are_rejected_before_a_device_call(self):
        with self.assertRaises(ValueError):installer.install_command('com.apple.Preferences',{})
        with self.assertRaises(ValueError):installer.lookup_app(None,'com.apple.Preferences')

    def test_nul_and_newline_records(self):
        logs=BackThreadLogs();logs.feed(b'noise\0BackThread first\0app.backthread.probe second\n');logs.finish()
        self.assertEqual(['BackThread first','app.backthread.probe second'],list(logs.lines))
        self.assertEqual(2,logs.matches)

    def test_fragmented_marker_and_unterminated_last_record(self):
        logs=BackThreadLogs();logs.feed(b'BackThr');logs.feed(b'ead.Probe UIKit-ready');logs.finish()
        self.assertEqual(['BackThread.Probe UIKit-ready'],list(logs.entry_markers))

    def test_bounded_retention_keeps_markers_separately(self):
        logs=BackThreadLogs(capacity=2);logs.feed(b'BackThread.Probe main-entered\0')
        for i in range(5):logs.feed(f'BackThread state {i}\0'.encode())
        self.assertEqual(2,len(logs.lines));self.assertEqual(4,logs.report()['truncatedMatches'])
        self.assertEqual(1,len(logs.entry_markers))

    def test_private_uuid_is_redacted(self):
        logs=BackThreadLogs();logs.feed(b'BackThread container AABBCCDD-1234-5678-90AB-CDEF12345678\n')
        self.assertEqual(['BackThread container <uuid>'],list(logs.lines))

    def test_partial_and_output_line_bounds(self):
        logs=BackThreadLogs();logs.feed(b'x'*100000)
        self.assertLessEqual(len(logs.pending),8192)
        logs.feed(b'\nBackThread '+b'x'*10000+b'\n')
        self.assertEqual(2048,len(logs.lines[-1]))

    def test_invalid_utf8_does_not_break_collection(self):
        logs=BackThreadLogs();logs.feed(b'BackThread \xff\0');logs.finish()
        self.assertIn('\ufffd',logs.lines[-1]);self.assertFalse(logs.report()['unrelatedLogContentStored'])

    def test_native_nul_records_preserve_multiline_exit_context(self):
        logs=BackThreadLogs(nul_framed=True)
        logs.feed(b'SpringBoard BackThreadProbe exit {\n code=1;\n')
        self.assertEqual(0,len(logs.lines))
        logs.feed(b'}\0unrelated private event\0')
        self.assertEqual(['SpringBoard BackThreadProbe exit {\n code=1;\n}'],list(logs.lines))


if __name__=='__main__':unittest.main(verbosity=2)
