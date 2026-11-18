"""Validate real ARM64 artifacts, package scope, determinism and corruption detection."""
import hashlib
import io
import json
import os
import pathlib
import plistlib
import shutil
import struct
import sys
import tarfile
import tempfile
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'scripts'))
from ios_build_support import macho, verify_adhoc, write_debian

BUILD = (ROOT / os.environ.get('BACKTHREAD_IOS_BUILD_DIR', 'artifacts/ios-build')).resolve()
if not BUILD.is_relative_to(ROOT / 'artifacts') or BUILD == ROOT / 'artifacts':
    raise ValueError('Select a build directory under this repository artifacts directory')
APP = BUILD / 'stage/BackThread.app'


def members(path):
    data = path.read_bytes()
    if data[:8] != b'!<arch>\n':
        raise ValueError('Not a Debian ar archive')
    result, offset = {}, 8
    while offset < len(data):
        header = data[offset:offset + 60]
        if len(header) != 60 or header[-2:] != b'`\n':
            raise ValueError('Invalid ar member header')
        size = int(header[48:58])
        name = header[:16].decode().strip().rstrip('/')
        if name in result or offset + 60 + size > len(data):
            raise ValueError('Duplicate or truncated ar member')
        result[name] = data[offset + 60:offset + 60 + size]
        offset += 60 + size + size % 2
    return result


class NativeArtifactTests(unittest.TestCase):
    def test_arm64_ios12_and_public_linkage(self):
        for binary in (APP / 'BackThread', BUILD / 'BackThread.Agent'):
            image = macho(binary)
            self.assertEqual(('arm64', 2, '12.0.0', True),
                             (image['architecture'], image['platform'], image['minimumOS'], image['signed']))
            self.assertTrue(all('/PrivateFrameworks/' not in p for p in image['linkedLibraries']))

    def test_rejects_arm64e_and_wrong_deployment(self):
        data = bytearray((BUILD / 'BackThread.Agent').read_bytes())
        with tempfile.TemporaryDirectory(dir=ROOT / 'artifacts') as temporary:
            path = pathlib.Path(temporary) / 'invalid'
            struct.pack_into('<I', data, 8, 2)
            path.write_bytes(data)
            with self.assertRaises(ValueError):
                macho(path)
            struct.pack_into('<I', data, 8, 0)
            offset = 32
            for _ in range(struct.unpack_from('<I', data, 16)[0]):
                command, length = struct.unpack_from('<II', data, offset)
                if command == 0x32:
                    struct.pack_into('<I', data, offset + 12, 13 << 16)
                offset += length
            path.write_bytes(data)
            with self.assertRaises(ValueError):
                macho(path)

    def test_code_and_resources_signature_hashes(self):
        self.assertGreater(verify_adhoc(APP / 'BackThread', APP)['codePages'], 0)
        self.assertGreater(verify_adhoc(BUILD / 'BackThread.Agent')['codePages'], 0)

    def test_rejects_newer_required_dyld_load_commands(self):
        data=bytearray((BUILD/'BackThread.Agent').read_bytes())
        offset=32
        for _ in range(struct.unpack_from('<I',data,16)[0]):
            command,length=struct.unpack_from('<II',data,offset)
            if command==0x26:
                struct.pack_into('<I',data,offset,0x80000034)
                break
            offset+=length
        with tempfile.TemporaryDirectory(dir=ROOT/'artifacts') as temporary:
            path=pathlib.Path(temporary)/'newer-dyld';path.write_bytes(data)
            with self.assertRaisesRegex(ValueError,'legacy dyld'):macho(path)

    def test_rejects_corrupted_code(self):
        with tempfile.TemporaryDirectory(dir=ROOT / 'artifacts') as temporary:
            path = pathlib.Path(temporary) / 'corrupt'
            data = bytearray((BUILD / 'BackThread.Agent').read_bytes())
            data[8192] ^= 1
            path.write_bytes(data)
            with self.assertRaisesRegex(ValueError, 'page hash'):
                verify_adhoc(path)

    def test_rejects_corrupted_plist_and_launch_image(self):
        with tempfile.TemporaryDirectory(dir=ROOT / 'artifacts') as temporary:
            bundle = pathlib.Path(temporary) / 'BackThread.app'
            shutil.copytree(APP, bundle)
            for resource in ('Info.plist', 'LaunchLandscape@2x.png'):
                path = bundle / resource
                original = path.read_bytes()
                path.write_bytes(original + b'corrupt')
                with self.assertRaisesRegex(ValueError, 'hash mismatch'):
                    verify_adhoc(bundle / 'BackThread', bundle)
                path.write_bytes(original)

    def test_iphone6_bundle_resources(self):
        info = plistlib.loads((APP / 'Info.plist').read_bytes())
        self.assertEqual('12.0', info['MinimumOSVersion'])
        self.assertEqual(['arm64'], info['UIRequiredDeviceCapabilities'])
        self.assertEqual(['audio'], info['UIBackgroundModes'])
        png = (APP / 'LaunchLandscape@2x.png').read_bytes()
        self.assertEqual((1334, 750), struct.unpack_from('>II', png, 16))
        self.assertEqual(png, (ROOT / 'src/iOS/Resources/LaunchLandscape@2x.png').read_bytes())

    def test_debian_scope_permissions_and_embedded_executables(self):
        for name, prefix, binary, target in (
            ('app.backthread.companion', './Applications/BackThread.app/', APP / 'BackThread', 'BackThread'),
            ('app.backthread.agent', './var/mobile/BackThread/bin/', BUILD / 'BackThread.Agent', 'BackThread.Agent')):
            version=plistlib.loads((APP/'Info.plist').read_bytes())['CFBundleShortVersionString']
            archive = members(BUILD / f'{name}_{version}_iphoneos-arm.deb')
            self.assertEqual({'debian-binary', 'control.tar.gz', 'data.tar.gz'}, set(archive))
            self.assertEqual(b'2.0\n', archive['debian-binary'])
            with tarfile.open(fileobj=io.BytesIO(archive['data.tar.gz'])) as tar:
                entries = tar.getmembers()
                self.assertTrue(entries)
                directories={e.name.rstrip('/') for e in entries if e.isdir()}
                self.assertNotIn('./Applications',directories)
                self.assertNotIn('./var/mobile',directories)
                for entry in entries:
                    if entry.isdir():
                        own_root='./Applications/BackThread.app' if name.endswith('.companion') else './var/mobile/BackThread'
                        self.assertTrue(entry.name.rstrip('/')==own_root or entry.name.startswith(own_root+'/'))
                        self.assertEqual((0,0) if name.endswith('.companion') else (501,501),(entry.uid,entry.gid))
                        if entry.name.rstrip('/')=='./var/mobile/BackThread':self.assertEqual(0o700,entry.mode)
                        continue
                    self.assertTrue(entry.isfile())
                    self.assertTrue(entry.name.startswith(prefix))
                    self.assertNotIn('..', pathlib.PurePosixPath(entry.name).parts)
                    self.assertFalse(entry.name.endswith(('.key', '.pem')))
                    self.assertEqual(0o755 if entry.name == prefix + target else 0o644, entry.mode)
                    parent='./'+pathlib.PurePosixPath(entry.name).parent.as_posix()
                    self.assertIn(parent,directories,'dpkg requires an explicit app-owned parent directory')
                self.assertEqual(binary.read_bytes(), tar.extractfile(prefix + target).read())
            with tarfile.open(fileobj=io.BytesIO(archive['control.tar.gz'])) as tar:
                self.assertIn('Architecture: iphoneos-arm', tar.extractfile('./control').read().decode())
                self.assertEqual({'./control'} if name.endswith('.agent') else {'./control', './postinst', './prerm'}, set(tar.getnames()))
                if name.endswith('.companion'):
                    for script in ('./postinst', './prerm'):
                        self.assertIn('/Applications/BackThread.app', tar.extractfile(script).read().decode())

    def test_debian_determinism_and_manifest(self):
        with tempfile.TemporaryDirectory(dir=ROOT / 'artifacts') as temporary:
            one, two = (pathlib.Path(temporary) / p for p in ('a.deb', 'b.deb'))
            for path in (one, two):
                write_debian(path, [('./control', b'example', 0o644)], [('./own/data', b'value', 0o600)])
            self.assertEqual(one.read_bytes(), two.read_bytes())
        manifest = json.loads((BUILD / 'build-manifest.json').read_text())
        for name, expected in manifest['artifacts'].items():
            path = APP / name if name == 'BackThread' else BUILD / name
            self.assertEqual(expected['sha256'], hashlib.sha256(path.read_bytes()).hexdigest())
            self.assertEqual(expected['bytes'], path.stat().st_size)
        self.assertFalse(manifest['speakerVerified'])
        self.assertFalse(manifest['nativeRuntimeTested'])


if __name__ == '__main__':
    unittest.main(verbosity=2)
