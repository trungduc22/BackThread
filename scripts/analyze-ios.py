"""Run Clang's path-sensitive analyzer against the actual iOS SDK (no device access)."""
import argparse
import json
import os
import pathlib
import plistlib
import subprocess
import datetime
import uuid
from ios_build_support import write_audit_header

ROOT = pathlib.Path(__file__).resolve().parents[1]
SOURCES = [
    'src/iOS/UI/main.m',
    'src/iOS/UI/BTTheme.m',
    'src/iOS/UI/BTVideoSurfaceView.m',
    'src/iOS/UI/BTNowPlayingView.m',
    'src/iOS/UI/BTIdleHubView.m',
    'src/iOS/UI/BTQuickActionsView.m',
    'src/iOS/UI/BTWorkerStatusView.m',
    'src/iOS/UI/BTSettingsView.m',
    'src/iOS/UI/BTNotificationOverlayView.m',
    'src/iOS/UI/BTViewController.m',
    'src/iOS/Media/BTBufferedMediaEngine.m',
    'src/iOS/Media/BTDirectMediaPlayer.m',
    'src/iOS/Media/BTYouTubeEmbedController.m',
    'src/iOS/Agent/BTAgent.m',
    'src/iOS/Agent/main.m',
    'src/iOS/AudioReceiver/BTAudioSessionCoordinator.m',
    'src/iOS/AudioReceiver/BTAudioReceiver.m',
    'src/iOS/AudioReceiver/BTPcmRing.c',
    'src/Shared/Protocol/bt_wire.c',
    'src/iOS/Probe/main.m',
    'src/iOS/Probe/entry.c',
    'src/iOS/Probe/loader.c',
]


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--zig', required=True, type=pathlib.Path)
    parser.add_argument('--sdk', required=True, type=pathlib.Path)
    args = parser.parse_args()
    zig, sdk = args.zig.resolve(), args.sdk.resolve()
    out = ROOT / 'artifacts/ios-analysis'
    out.mkdir(parents=True, exist_ok=True)
    report=dict(runId=uuid.uuid4().hex,startedUtc=datetime.datetime.now(datetime.timezone.utc).isoformat(),expectedTranslationUnits=len(SOURCES),complete=False,passed=False,currentSource=None,results=[])
    def save():
        temporary=out/'results.json.tmp'
        temporary.write_text(json.dumps(report,indent=2),encoding='utf-8');temporary.replace(out/'results.json')
    # Invalidate any old clean report before invoking a compiler or reading build inputs.
    save()
    write_audit_header(ROOT/'artifacts/ios-build/stage/BackThread.app/BackThread',out/'audit-symbols.h')
    results = report['results']
    for index, source in enumerate(SOURCES):
        print('Analyzing ' + source, flush=True)
        report['currentSource']=source;save()
        plist = out / f'{index}.plist'
        plist.unlink(missing_ok=True)
        command = [str(zig), 'clang', '--analyze', '-target', 'arm64-apple-ios12.0',
                   '-isysroot', str(sdk), '-iframework', str(sdk / 'System/Library/Frameworks'),
                   '-isystem', str(sdk / 'usr/include'), '-isystem', str(zig.parent / 'lib/include'),
                   '-I', str(out),
                   '-Wall', '-Wextra', '-Werror', '-Werror=unguarded-availability-new',
                   '-Xanalyzer', '-analyzer-output=plist', '-o', str(plist), source]
        if source.endswith('.m'):
            command += ['-fobjc-arc', '-fblocks']
        if source=='src/iOS/Probe/main.m':command+=['-DBT_PROBE_LAUNCH_UI=1']
        with (out / f'{index}.log').open('wb') as log:
            try:
                process = subprocess.run(command, cwd=ROOT, stdin=subprocess.DEVNULL,stdout=log, stderr=subprocess.STDOUT,timeout=120,
                                         env=dict(os.environ, ZIG_GLOBAL_CACHE_DIR=str(out / 'zig-cache')))
            except (OSError,subprocess.TimeoutExpired) as error:
                report['error']=type(error).__name__+': '+str(error);save();raise
        try:diagnostics = plistlib.loads(plist.read_bytes()).get('diagnostics', []) if plist.is_file() else None
        except (ValueError,plistlib.InvalidFileException) as error:
            report['error']='Invalid analyzer report: '+str(error);save();raise
        results.append(dict(source=source, exitCode=process.returncode,
                            diagnostics=diagnostics, reportProduced=plist.is_file()))
        # Save partial results too: absence of later reports must never count as a pass.
        save()
    passed = len(results)==len(SOURCES) and all(r['exitCode'] == 0 and r['diagnostics'] == [] and r['reportProduced'] for r in results)
    report.update(complete=True,passed=passed,currentSource=None,finishedUtc=datetime.datetime.now(datetime.timezone.utc).isoformat());save()
    print(f'{len(results)} translation units analyzed; clean={passed}')
    raise SystemExit(0 if passed else 1)


if __name__ == '__main__':
    main()
