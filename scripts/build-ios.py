"""Compile/link/sign the existing Agent + UIKit app. Does not install or contact a device."""
import argparse
import hashlib
import json
import os
import pathlib
import plistlib
import shutil
import subprocess
from ios_build_support import debian, debian_rootless, macho, stable_uuid, write_debian, write_ipa

ROOT = pathlib.Path(__file__).resolve().parents[1]
UI_SOURCES = [
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
]
AGENT_SOURCES = [
    'src/iOS/Agent/BTAgent.m',
    'src/iOS/Transport/BTWifiClient.m',
    'src/iOS/Worker/BTWorkerEngine.m',
    'src/iOS/AudioReceiver/BTAudioSessionCoordinator.m',
    'src/iOS/AudioReceiver/BTAudioReceiver.m',
    'src/iOS/AudioReceiver/BTPcmRing.c',
    'src/Shared/Protocol/bt_wire.c',
]
SOURCES = UI_SOURCES + AGENT_SOURCES
FRAMEWORKS = ['UIKit', 'Foundation', 'AVFoundation', 'AVKit', 'CoreMedia', 'AudioToolbox', 'MediaPlayer', 'WebKit', 'Security', 'Network', 'ImageIO', 'CoreGraphics', 'QuartzCore', 'SystemConfiguration']
WEAK_FRAMEWORKS = ['PDFKit', 'Vision', 'Speech']


def run(command, log, env):
    with log.open('wb') as output:
        result = subprocess.run([str(c) for c in command], cwd=ROOT, env=env, stdout=output, stderr=subprocess.STDOUT)
    output = log.read_text(errors='replace')
    if result.returncode:
        raise RuntimeError(f'Command failed ({result.returncode}); see {log.relative_to(ROOT)}: ' + output[-1500:])
    return output.strip()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--zig', required=True, type=pathlib.Path)
    parser.add_argument('--sdk', required=True, type=pathlib.Path)
    parser.add_argument('--codesign', required=True, type=pathlib.Path, help='rcodesign executable; ad-hoc signing only')
    parser.add_argument('--linker', required=True, type=pathlib.Path, help='Existing LLVM LLD 23.1.5; the device-validated Mach-O link path')
    parser.add_argument('--profile', choices=['ios12-universal', 'ios15'], default='ios12-universal', help='Target deployment profile')
    parser.add_argument('--out', default='artifacts/ios-build', type=pathlib.Path)
    args = parser.parse_args()
    profile = args.profile
    min_ios = '15.0' if profile == 'ios15' else '12.0'
    zig, sdk, signer, linker = args.zig.resolve(), args.sdk.resolve(), args.codesign.resolve(), args.linker.resolve()
    out = (ROOT / args.out).resolve()
    if not out.is_relative_to(ROOT / 'artifacts') or out == ROOT / 'artifacts':
        raise ValueError('Build output must be a subdirectory of this repository artifacts directory')
    for path in (zig, signer, linker, sdk / 'SDKSettings.plist'):
        if not path.is_file():
            raise FileNotFoundError(path)
    sdk_version = plistlib.loads((sdk / 'SDKSettings.plist').read_bytes()).get('Version', '12.4')
    out.mkdir(parents=True, exist_ok=True)
    stage = out / 'stage'
    if stage.exists():
        if not stage.resolve().is_relative_to(out) or stage.is_symlink():
            raise ValueError('Unsafe staging directory')
        shutil.rmtree(stage)
    app = stage / 'BackThread.app'
    app.mkdir(parents=True)
    objects = out / 'objects'
    logs = out / 'logs'
    objects.mkdir(exist_ok=True)
    logs.mkdir(exist_ok=True)
    env = dict(os.environ, ZIG_GLOBAL_CACHE_DIR=str(out / 'zig-cache'))
    version = run([zig, 'version'], logs / 'compiler.txt', env)
    if version != '0.13.0':
        raise ValueError('Validated cross-build uses Zig 0.13.0; use Theos/Xcode for other toolchains')
    linker_version = run([linker, '-flavor', 'darwin', '--version'], logs / 'linker.txt', env)
    if not (linker_version.startswith('LLD 23.1.') and len(linker_version) > 9 and linker_version[9].isdigit()):
        raise ValueError('Validated cross-build requires LLD 23.1.x; record and validate another version separately')
    base = [zig, 'cc', '-target', f'aarch64-ios.{min_ios}', '-isysroot', sdk, '-iframework', sdk / 'System/Library/Frameworks', '-isystem', sdk / 'usr/include', '-O2', '-g0', '-fno-sanitize=all', '-Wall', '-Wextra', '-Werror', '-Werror=unguarded-availability-new', f'-ffile-prefix-map={ROOT}=.']
    ui_compiled = []
    agent_compiled = []
    for source in UI_SOURCES + AGENT_SOURCES:
        print('Compiling ' + source, flush=True)
        target = objects / (pathlib.Path(source).stem + '.o')
        flags = ['-fobjc-arc', '-fblocks'] if source.endswith('.m') else []
        run(base + flags + ['-c', source, '-o', target], logs / (target.stem + '.txt'), env)
        if macho(target, profile=profile)['fileType'] != 1:
            raise ValueError('Compiler did not emit a relocatable ARM64 object')
        if source in UI_SOURCES:
            ui_compiled.append(target)
        else:
            agent_compiled.append(target)
    compiled = ui_compiled + agent_compiled
    library = out / 'libBackThread.Agent.a'
    # Recreate this BackThread-owned archive; ar otherwise retains stale object members.
    library.unlink(missing_ok=True)
    run([zig, 'ar', 'rcs', library, *agent_compiled], logs / 'archive.txt', env)
    print('Linking UIKit application', flush=True)
    executable = app / 'BackThread'
    fixup_flags = ['-no_fixup_chains'] if profile == 'ios12-universal' else []
    # Use original multi-document SDK stubs so LLD can follow umbrella reexports.
    command = [linker, '-flavor', 'darwin', '-arch', 'arm64', '-platform_version', 'ios', min_ios, sdk_version, '-syslibroot', sdk, '-L', sdk / 'usr/lib', '-F', sdk / 'System/Library/Frameworks', '-F', sdk / 'System/Library/PrivateFrameworks', '-F', sdk / 'System/Library/Frameworks/AVFoundation.framework/Frameworks', *fixup_flags, '-headerpad', '0x1000', *ui_compiled, library, '-lSystem', '-lobjc', '-lz']
    for framework in FRAMEWORKS:
        command += ['-framework', framework]
    for framework in WEAK_FRAMEWORKS:
        command += ['-weak_framework', framework]
    run(command + ['-o', executable], logs / 'link.txt', env)
    stable_uuid(executable)
    resources = ROOT / 'src/iOS/Resources'
    for resource in resources.iterdir():
        if resource.is_file():
            shutil.copyfile(resource, app / resource.name)
    # An explicit empty config prevents signing-tool user profiles from changing this build.
    empty_config = out / 'empty-codesign.toml'
    empty_config.write_text('')
    run([signer, 'sign', '--config-file', empty_config, '--timestamp-url', 'none', app], logs / 'sign.txt', env)
    print('Checking ad-hoc code and bundle hashes', flush=True)
    from ios_build_support import verify_adhoc
    verify_adhoc(executable, app)
    agent_main = objects / 'AgentMain.o'
    print('Compiling and signing standalone Agent', flush=True)
    run(base + ['-fobjc-arc', '-fblocks', '-c', 'src/iOS/Agent/main.m', '-o', agent_main], logs / 'AgentMain.txt', env)
    agent = out / 'BackThread.Agent'
    agent_cmd = [linker, '-flavor', 'darwin', '-arch', 'arm64', '-platform_version', 'ios', min_ios, sdk_version, '-syslibroot', sdk, '-L', sdk / 'usr/lib', '-F', sdk / 'System/Library/Frameworks', '-F', sdk / 'System/Library/PrivateFrameworks', '-F', sdk / 'System/Library/Frameworks/AVFoundation.framework/Frameworks', *fixup_flags, '-headerpad', '0x1000', agent_main, library, '-lSystem', '-lobjc', '-lz', '-framework', 'Foundation', '-framework', 'AVFoundation', '-framework', 'AudioToolbox', '-framework', 'Security', '-framework', 'Network', '-framework', 'ImageIO', '-framework', 'CoreGraphics']
    for framework in WEAK_FRAMEWORKS:
        agent_cmd += ['-weak_framework', framework]
    run(agent_cmd + ['-o', agent], logs / 'agent-link.txt', env)
    stable_uuid(agent)
    run([signer, 'sign', '--config-file', empty_config, '--timestamp-url', 'none', '--binary-identifier', 'app.backthread.agent', agent], logs / 'agent-sign.txt', env)
    verify_adhoc(agent)
    image = macho(executable, profile=profile)
    if not image['signed']:
        raise ValueError('Missing ad-hoc code signature')
    plist = plistlib.loads((app / 'Info.plist').read_bytes())
    epoch = int(os.environ.get('SOURCE_DATE_EPOCH', '0'))
    ver = plist['CFBundleShortVersionString']
    package = out / f'app.backthread.companion_{ver}_iphoneos-arm.deb'
    debian(package, (ROOT / 'src/iOS/control').read_bytes(), app, epoch)
    rootless_package = out / f'app.backthread.companion_{ver}_iphoneos-arm64-rootless.deb'
    debian_rootless(rootless_package, (ROOT / 'src/iOS/control').read_bytes(), app, epoch)
    ipa_name = 'BackThread-iOS15.ipa' if profile == 'ios15' else 'BackThread.ipa'
    ipa = out / ipa_name
    write_ipa(ipa, app, epoch)
    agent_package = out / f'app.backthread.agent_{ver}_iphoneos-arm.deb'
    write_debian(agent_package, [('./control', (ROOT / 'src/iOS/Agent/control').read_bytes(), 0o644)], [('./var/mobile/BackThread/bin/BackThread.Agent', agent.read_bytes(), 0o755)], epoch, data_directories=[('./var/mobile/BackThread', 0o700, 501, 501), ('./var/mobile/BackThread/bin', 0o755, 501, 501)])
    manifest = dict(profile=profile, compiler=f'Zig {version} / Clang 18.1.6', linker=linker_version, tools={name: dict(executable=path.name, sha256=hashlib.sha256(path.read_bytes()).hexdigest()) for name, path in [('compiler', zig), ('linker', linker), ('signer', signer)]}, sdk=sdk_version, deploymentTarget=min_ios, translationUnits=len(compiled) + 1, warningsAsErrors=True, validationScope='build only; device results are separate reports', nativeRuntimeTested=False, installedOnDevice=False, speakerVerified=False, components=['BackThread.Agent (embedded and standalone)', 'BackThread.UI'], image=image, agentImage=macho(agent, profile=profile), sdkStubFormat='original multi-document TBDs', artifacts={p.name: dict(bytes=p.stat().st_size, sha256=hashlib.sha256(p.read_bytes()).hexdigest()) for p in (executable, agent, library, package, rootless_package, agent_package, ipa)})
    (out / 'build-manifest.json').write_text(json.dumps(manifest, indent=2))
    print(json.dumps(manifest, indent=2))


if __name__ == '__main__':
    main()
