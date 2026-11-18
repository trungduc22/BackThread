import importlib.util
import contextlib
import io
import json
import pathlib
import sys
import tempfile
import unittest
from unittest.mock import patch

ROOT = pathlib.Path(__file__).resolve().parents[1]


def module(name):
    spec = importlib.util.spec_from_file_location(name.replace('-', '_'), ROOT/'scripts'/(name+'.py'))
    result = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(result)
    return result


control = module('phone-control')
resolver = module('resolve-youtube')
native_test = module('test-native-direct')


class PhoneControlTests(unittest.TestCase):
    def test_ssh_only_targets_own_container(self):
        identifier = '01234567-1234-4567-89ab-0123456789ab'
        own = '/var/mobile/Containers/Data/Application/'+identifier
        command = control.remote_command(own, 'root', identifier)
        self.assertIn(own+'/Library/BackThread/Control', command)
        self.assertIn('chown 501:501', command)
        for container in ('/var/mobile', own+'/../other', own+"'; reboot #", own+'/Library'):
            with self.assertRaises(ValueError): control.remote_command(container, 'root', identifier)
        with self.assertRaises(ValueError): control.remote_command(own, 'other;sh', identifier)

    def test_urls_are_payload_not_shell(self):
        url = 'https://r1.googlevideo.com/videoplayback?sig=a%26b&expire=1234'
        request = control.validate_request('open', url)
        self.assertEqual(url, request['url'])
        command = control.remote_command('/var/mobile/Containers/Data/Application/'+request['id'], 'mobile', request['id'])
        self.assertNotIn(url, command)
        self.assertNotIn('sig=', command)

    def test_invalid_control_and_seek(self):
        for url in ('http://example.com/x.mp4', 'https://user:pass@example.com/x.mp4', 'https://example.com:8443/x.mp4'):
            with self.assertRaises(ValueError): control.validate_request('open', url)
        for seconds in (-1, float('nan'), float('inf'), True, '3'):
            with self.assertRaises(ValueError): control.validate_request('seek', seconds=seconds)
        with self.assertRaises(ValueError): control.validate_request('shell')
        with self.assertRaises(ValueError): control.validate_request('status', url='https://example.com/x.mp4')
        self.assertEqual(12, control.validate_request('seek', seconds=12)['seconds'])

    def test_single_video_links_and_lookalikes(self):
        expected = 'https://www.youtube.com/watch?v=M7lc1UVf-VE'
        for url in (expected, 'https://youtu.be/M7lc1UVf-VE', 'https://m.youtube.com/live/M7lc1UVf-VE'):
            self.assertEqual(expected, resolver.video_url(url))
        for url in ('https://youtube.com.evil/watch?v=M7lc1UVf-VE', 'https://youtube.com/playlist?list=x', 'https://youtube.com/watch?v=M7lc1UVf-VE&v=other', 'https://user@youtube.com/watch?v=M7lc1UVf-VE'):
            with self.assertRaises(ValueError): resolver.video_url(url)

    def test_only_single_compatible_stream_not_split_or_software_formats(self):
        base = dict(url='https://r1.googlevideo.com/videoplayback?sig=test', vcodec='avc1.42001E', acodec='mp4a.40.2', height=360, fps=30, tbr=700)
        variations = [dict(base, vcodec='vp9'), dict(base, vcodec='av01.0'), dict(base, acodec='none'), dict(base, height=1080), dict(base, fps=60), dict(base, has_drm=True), dict(base, url='https://googlevideo.com.evil/videoplayback?x=1')]
        for candidate in variations:
            with self.assertRaises(ValueError): resolver.select_stream(dict(formats=[candidate]))
        result = resolver.select_stream(dict(formats=variations+[base]))
        self.assertEqual(base['url'], result['url'])
        self.assertFalse(result['videoRelay'])
        self.assertFalse(result['transcoding'])

    def test_live_metadata_and_quality_cap(self):
        base = dict(url='https://manifest.googlevideo.com/x/index.m3u8', vcodec='avc1', acodec='aac', height=360, fps=25, tbr=800)
        result = resolver.select_stream(dict(is_live=True, formats=[base, dict(base, height=720)]))
        self.assertTrue(result['isLive'])
        self.assertEqual(360, result['height'])

    def run_native_guard(self, old_client):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            (root/'key').write_text('12'*32)
            (root/'config.json').write_text(json.dumps(dict(keyFile=str(root/'key'), apiPort=12345)))
            secret_url = 'https://r1.googlevideo.com/videoplayback?sig=PRIVATE_TEST_VALUE'
            (root/'stream.json').write_text(json.dumps(dict(url=secret_url)))
            commands = []
            def response(request, timeout):
                if request.full_url.endswith('/status'):
                    result = dict(state='connected', simulated=False, transport='usb')
                else:
                    body = json.loads(request.data)
                    if body.get('phoneControl'):
                        commands.append(body['phoneControl']['command'])
                        if commands[-1] == 'open': raise OSError(secret_url)
                        result = dict(media=dict(owner='pc'))
                    else:
                        result = dict(media=dict(owner='pc'))
                        if not old_client: result['media']['timeAdvanced'] = False
                return io.BytesIO(json.dumps(result).encode())
            argv = ['test-native-direct.py', '--config', str(root/'config.json'), '--request-file', str(root/'stream.json'), '--output', str(root/'result.json')]
            output = io.StringIO()
            with patch.object(sys, 'argv', argv), patch.object(native_test.urllib.request, 'urlopen', response), contextlib.redirect_stdout(output):
                self.assertEqual(1, native_test.main())
            self.assertNotIn('PRIVATE_TEST_VALUE', output.getvalue())
            return commands, json.loads((root/'result.json').read_text())

    def test_physical_check_refuses_old_build_without_playing(self):
        commands, result = self.run_native_guard(True)
        self.assertEqual([], commands)
        self.assertFalse(result['passed'])

    def test_physical_check_restores_even_when_open_response_is_lost(self):
        commands, result = self.run_native_guard(False)
        self.assertEqual(['open', 'return-pc'], commands)
        self.assertTrue(result['pcOwnershipRestored'])
        self.assertFalse(result['passed'])


if __name__ == '__main__': unittest.main()
