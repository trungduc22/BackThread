"""Compile/link/sign the existing Agent + UIKit app. Does not install or contact a device."""
import argparse
import hashlib
import json
import os
import pathlib
import plistlib
import shutil
import subprocess
from ios_build_support import debian, macho, stable_uuid, write_debian, write_ipa

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
    'src/iOS/AudioReceiver/BTAudioSessionCoordinator.m',
    'src/iOS/AudioReceiver/BTAudioReceiver.m',
    'src/iOS/AudioReceiver/BTPcmRing.c',
    'src/Shared/Protocol/bt_wire.c',
]
SOURCES = UI_SOURCES + AGENT_SOURCES
FRAMEWORKS = ['UIKit', 'Foundation', 'AVFoundation', 'CoreMedia', 'AudioToolbox', 'MediaPlayer', 'WebKit', 'Security', 'ImageIO', 'CoreGraphics', 'QuartzCore', 'SystemConfiguration']


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
    parser.add_argument('--linker',required=True,type=pathlib.Path,help='Existing LLVM LLD 23.1.5; the device-validated Mach-O link path')
    parser.add_argument('--out', default='artifacts/ios-build', type=pathlib.Path)
    args = parser.parse_args()
    zig, sdk, signer, linker = args.zig.resolve(), args.sdk.resolve(), args.codesign.resolve(), args.linker.resolve()
    out = (ROOT / args.out).resolve()
    if not out.is_relative_to(ROOT / 'artifacts') or out == ROOT / 'artifacts':
        raise ValueError('Build output must be a subdirectory of this repository artifacts directory')
    for path in (zig, signer, linker, sdk / 'SDKSettings.plist'):
        if not path.is_file():
            raise FileNotFoundError(path)
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
    linker_version=run([linker,'-flavor','darwin','--version'],logs/'linker.txt',env)
    if not (linker_version.startswith('LLD 23.1.') and len(linker_version)>9 and linker_version[9].isdigit()):
        raise ValueError('Validated cross-build requires LLD 23.1.x; record and validate another version separately')
    base = [zig, 'cc', '-target', 'aarch64-ios.12.0', '-isysroot', sdk, '-iframework', sdk / 'System/Library/Frameworks', '-isystem', sdk / 'usr/include', '-O2', '-g0', '-fno-sanitize=all', '-Wall', '-Wextra', '-Werror', '-Werror=unguarded-availability-new', f'-ffile-prefix-map={ROOT}=.']
    ui_compiled = []
    agent_compiled = []
    for source in UI_SOURCES + AGENT_SOURCES:
        print('Compiling ' + source, flush=True)
        target = objects / (pathlib.Path(source).stem + '.o')
        flags = ['-fobjc-arc', '-fblocks'] if source.endswith('.m') else []
        run(base + flags + ['-c', source, '-o', target], logs / (target.stem + '.txt'), env)
        if macho(target)['fileType'] != 1:
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
    # Use original multi-document SDK stubs so LLD can follow umbrella reexports.
    command=[linker,'-flavor','darwin','-arch','arm64','-platform_version','ios','12.0','12.4','-syslibroot',sdk,'-L',sdk/'usr/lib','-F',sdk/'System/Library/Frameworks','-F',sdk/'System/Library/PrivateFrameworks','-F',sdk/'System/Library/Frameworks/AVFoundation.framework/Frameworks','-no_fixup_chains','-headerpad','0x1000',*ui_compiled,library,'-lSystem','-lobjc']
    for framework in FRAMEWORKS:
        command += ['-framework', framework]
    run(command+['-o',executable],logs/'link.txt',env)
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
    # rcodesign 0.29's verifier incorrectly treats absent CMS in ad-hoc signatures as an error.
    # Verify every actual CodeDirectory page and resource hash independently below.
    from ios_build_support import verify_adhoc
    verify_adhoc(executable, app)
    agent_main = objects / 'AgentMain.o'
    print('Compiling and signing standalone Agent', flush=True)
    run(base + ['-fobjc-arc', '-fblocks', '-c', 'src/iOS/Agent/main.m', '-o', agent_main], logs / 'AgentMain.txt', env)
    agent = out / 'BackThread.Agent'
    run([linker,'-flavor','darwin','-arch','arm64','-platform_version','ios','12.0','12.4','-syslibroot',sdk,'-L',sdk/'usr/lib','-F',sdk/'System/Library/Frameworks','-F',sdk/'System/Library/PrivateFrameworks','-F',sdk/'System/Library/Frameworks/AVFoundation.framework/Frameworks','-no_fixup_chains','-headerpad','0x1000',agent_main,library,'-lSystem','-lobjc','-framework','Foundation','-framework','AVFoundation','-framework','AudioToolbox','-framework','Security','-o',agent],logs/'agent-link.txt',env)
    stable_uuid(agent)
    run([signer, 'sign', '--config-file', empty_config, '--timestamp-url', 'none', '--binary-identifier', 'app.backthread.agent', agent], logs / 'agent-sign.txt', env)
    verify_adhoc(agent)
    image = macho(executable)
    if not image['signed']:
        raise ValueError('Missing ad-hoc code signature')
    plist = plistlib.loads((app / 'Info.plist').read_bytes())
    epoch = int(os.environ.get('SOURCE_DATE_EPOCH', '0'))
    package = out / f'app.backthread.companion_{plist["CFBundleShortVersionString"]}_iphoneos-arm.deb'
    debian(package, (ROOT / 'src/iOS/control').read_bytes(), app, epoch)
    ipa = out / 'BackThread.ipa'
    write_ipa(ipa, app, epoch)
    agent_package=out/f'app.backthread.agent_{plist["CFBundleShortVersionString"]}_iphoneos-arm.deb'
    write_debian(agent_package,[('./control',(ROOT/'src/iOS/Agent/control').read_bytes(),0o644)],[('./var/mobile/BackThread/bin/BackThread.Agent',agent.read_bytes(),0o755)],epoch,data_directories=[('./var/mobile/BackThread',0o700,501,501),('./var/mobile/BackThread/bin',0o755,501,501)])
    manifest = dict(compiler=f'Zig {version} / Clang 18.1.6', linker=linker_version, tools={name:dict(executable=path.name,sha256=hashlib.sha256(path.read_bytes()).hexdigest()) for name,path in [('compiler',zig),('linker',linker),('signer',signer)]}, sdk=plistlib.loads((sdk / 'SDKSettings.plist').read_bytes()).get('Version'), deploymentTarget='12.0', translationUnits=len(compiled)+1, warningsAsErrors=True, validationScope='build only; device results are separate reports', nativeRuntimeTested=False, installedOnDevice=False, speakerVerified=False, components=['BackThread.Agent (embedded and standalone)', 'BackThread.UI'], image=image, agentImage=macho(agent), sdkStubFormat='original multi-document TBDs', artifacts={p.name:dict(bytes=p.stat().st_size, sha256=hashlib.sha256(p.read_bytes()).hexdigest()) for p in (executable, agent, library, package, agent_package, ipa)})
    (out / 'build-manifest.json').write_text(json.dumps(manifest, indent=2))
    print(json.dumps(manifest, indent=2))


if __name__ == '__main__':
    main()
