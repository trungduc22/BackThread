"""Run Clang's path-sensitive analyzer against the actual iOS SDK (no device access)."""
import argparse
import json
import os
import pathlib
import plistlib
import subprocess

ROOT = pathlib.Path(__file__).resolve().parents[1]
SOURCES = ['src/iOS/UI/main.m', 'src/iOS/UI/BTViewController.m', 'src/iOS/Agent/BTAgent.m',
           'src/iOS/Agent/main.m', 'src/iOS/AudioReceiver/BTAudioReceiver.m',
           'src/iOS/AudioReceiver/BTPcmRing.c', 'src/Shared/Protocol/bt_wire.c']


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--zig', required=True, type=pathlib.Path)
    parser.add_argument('--sdk', required=True, type=pathlib.Path)
    args = parser.parse_args()
    zig, sdk = args.zig.resolve(), args.sdk.resolve()
    out = ROOT / 'artifacts/ios-analysis'
    out.mkdir(parents=True, exist_ok=True)
    results = []
    for index, source in enumerate(SOURCES):
        print('Analyzing ' + source, flush=True)
        plist = out / f'{index}.plist'
        plist.unlink(missing_ok=True)
        command = [str(zig), 'clang', '--analyze', '-target', 'arm64-apple-ios12.0',
                   '-isysroot', str(sdk), '-iframework', str(sdk / 'System/Library/Frameworks'),
                   '-isystem', str(sdk / 'usr/include'), '-isystem', str(zig.parent / 'lib/include'),
                   '-Wall', '-Wextra', '-Werror', '-Werror=unguarded-availability-new',
                   '-Xanalyzer', '-analyzer-output=plist', '-o', str(plist), source]
        if source.endswith('.m'):
            command += ['-fobjc-arc', '-fblocks']
        with (out / f'{index}.log').open('wb') as log:
            process = subprocess.run(command, cwd=ROOT, stdout=log, stderr=subprocess.STDOUT,
                                     env=dict(os.environ, ZIG_GLOBAL_CACHE_DIR=str(out / 'zig-cache')))
        diagnostics = plistlib.loads(plist.read_bytes()).get('diagnostics', []) if plist.is_file() else None
        results.append(dict(source=source, exitCode=process.returncode,
                            diagnostics=diagnostics, reportProduced=plist.is_file()))
        # Save partial results too: absence of later reports must never count as a pass.
        (out / 'results.json').write_text(json.dumps(results, indent=2))
    passed = all(r['exitCode'] == 0 and r['diagnostics'] == [] and r['reportProduced'] for r in results)
    print(f'{len(results)} translation units analyzed; clean={passed}')
    raise SystemExit(0 if passed else 1)


if __name__ == '__main__':
    main()
