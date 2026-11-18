"""Compile/link/sign the existing Agent + UIKit app. Does not install or contact a device."""
import argparse
import hashlib
import json
import os
import pathlib
import plistlib
import shutil
import subprocess
from ios_build_support import debian, macho, normalize_tbds, stable_uuid, write_debian

ROOT = pathlib.Path(__file__).resolve().parents[1]
SOURCES = ['src/iOS/UI/main.m', 'src/iOS/UI/BTViewController.m', 'src/iOS/Agent/BTAgent.m', 'src/iOS/AudioReceiver/BTAudioReceiver.m', 'src/iOS/AudioReceiver/BTPcmRing.c', 'src/Shared/Protocol/bt_wire.c']
FRAMEWORKS = ['UIKit', 'Foundation', 'AVFoundation', 'AudioToolbox', 'MediaPlayer', 'Security', 'ImageIO', 'CoreGraphics', 'QuartzCore']


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
    parser.add_argument('--out', default='artifacts/ios-build', type=pathlib.Path)
    args = parser.parse_args()
    zig, sdk, signer = args.zig.resolve(), args.sdk.resolve(), args.codesign.resolve()
    out = (ROOT / args.out).resolve()
    if not out.is_relative_to(ROOT / 'artifacts') or out == ROOT / 'artifacts':
        raise ValueError('Build output must be a subdirectory of this repository artifacts directory')
    for path in (zig, signer, sdk / 'SDKSettings.plist'):
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
    base = [zig, 'cc', '-target', 'aarch64-ios.12.0', '-isysroot', sdk, '-iframework', sdk / 'System/Library/Frameworks', '-isystem', sdk / 'usr/include', '-O2', '-g0', '-fno-sanitize=all', '-Wall', '-Wextra', '-Werror', '-Werror=unguarded-availability-new', f'-ffile-prefix-map={ROOT}=.']
    compiled = []
    for source in SOURCES:
        print('Compiling ' + source, flush=True)
        target = objects / (pathlib.Path(source).stem + '.o')
        flags = ['-fobjc-arc', '-fblocks'] if source.endswith('.m') else []
        run(base + flags + ['-c', source, '-o', target], logs / (target.stem + '.txt'), env)
        if macho(target)['fileType'] != 1:
            raise ValueError('Compiler did not emit a relocatable ARM64 object')
        compiled.append(target)
    library = out / 'libBackThread.Agent.a'
    # Recreate this BackThread-owned archive; ar otherwise retains stale object members.
    library.unlink(missing_ok=True)
    run([zig, 'ar', 'rcs', library, *compiled[2:]], logs / 'archive.txt', env)
    link_sdk = out / 'link-sdk'
    normalized = normalize_tbds(sdk, link_sdk)
    print('Linking UIKit application', flush=True)
    executable = app / 'BackThread'
    command = [zig, 'cc', '-target', 'aarch64-ios.12.0', '-isysroot', link_sdk, '-L', link_sdk / 'usr/lib', '-L', link_sdk / 'usr/lib/system', '-F', link_sdk / 'System/Library/Frameworks', '-F', link_sdk / 'System/Library/PrivateFrameworks', '-F', link_sdk / 'System/Library/Frameworks/AVFoundation.framework/Frameworks', '-O2', '-fno-sanitize=all', *compiled[:2], library, '-lobjc']
    for framework in FRAMEWORKS:
        command += ['-framework', framework]
    run(command + ['-Wl,-headerpad,0x1000', '-o', executable], logs / 'link.txt', env)
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
    run([zig, 'cc', '-target', 'aarch64-ios.12.0', '-isysroot', link_sdk, '-L', link_sdk / 'usr/lib', '-L', link_sdk / 'usr/lib/system', '-F', link_sdk / 'System/Library/Frameworks', '-F', link_sdk / 'System/Library/Frameworks/AVFoundation.framework/Frameworks', '-F', link_sdk / 'System/Library/PrivateFrameworks', '-O2', '-fno-sanitize=all', agent_main, library, '-lobjc', '-framework', 'Foundation', '-framework', 'AVFoundation', '-framework', 'AudioToolbox', '-framework', 'Security', '-Wl,-headerpad,0x1000', '-o', agent], logs / 'agent-link.txt', env)
    stable_uuid(agent)
    run([signer, 'sign', '--config-file', empty_config, '--timestamp-url', 'none', '--binary-identifier', 'app.backthread.agent', agent], logs / 'agent-sign.txt', env)
    verify_adhoc(agent)
    image = macho(executable)
    if not image['signed']:
        raise ValueError('Missing ad-hoc code signature')
    plist = plistlib.loads((app / 'Info.plist').read_bytes())
    package = out / f'app.backthread.companion_{plist["CFBundleShortVersionString"]}_iphoneos-arm.deb'
    debian(package, (ROOT / 'src/iOS/control').read_bytes(), app, int(os.environ.get('SOURCE_DATE_EPOCH', '0')))
    agent_package=out/'app.backthread.agent_0.1.1_iphoneos-arm.deb'
    write_debian(agent_package,[('./control',(ROOT/'src/iOS/Agent/control').read_bytes(),0o644)],[('./var/mobile/BackThread/bin/BackThread.Agent',agent.read_bytes(),0o755)],int(os.environ.get('SOURCE_DATE_EPOCH','0')))
    manifest = dict(compiler=f'Zig {version} / Clang 18.1.6', sdk=plistlib.loads((sdk / 'SDKSettings.plist').read_bytes()).get('Version'), deploymentTarget='12.0', translationUnits=len(compiled)+1, warningsAsErrors=True, nativeRuntimeTested=False, installedOnDevice=False, speakerVerified=False, components=['BackThread.Agent (embedded and standalone)', 'BackThread.UI'], image=image, agentImage=macho(agent), sdkStubDocuments=len(normalized), artifacts={p.name:dict(bytes=p.stat().st_size, sha256=hashlib.sha256(p.read_bytes()).hexdigest()) for p in (executable, agent, library, package, agent_package)})
    (out / 'build-manifest.json').write_text(json.dumps(manifest, indent=2))
    (out / 'tbd-document-manifest.json').write_text(json.dumps(normalized, indent=2))
    print(json.dumps(manifest, indent=2))


if __name__ == '__main__':
    main()
