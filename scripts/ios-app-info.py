"""Read Installation Proxy metadata only for BackThread's UI or diagnostic probe."""
import argparse
import importlib.util
import json
import pathlib
import plistlib
import struct

ROOT=pathlib.Path(__file__).resolve().parents[1]
spec=importlib.util.spec_from_file_location('installer',ROOT/'scripts/install-ios.py')
installer=importlib.util.module_from_spec(spec);spec.loader.exec_module(installer)

def main():
    parser=argparse.ArgumentParser();parser.add_argument('--component',choices=('ui','probe'),default='ui');args=parser.parse_args()
    identifier='app.backthread.companion' if args.component=='ui' else 'app.backthread.probe'
    with installer.PairedDevice() as device:
        record=installer.lookup_app(device,identifier)
        private=ROOT/'artifacts/ios-private';private.mkdir(parents=True,exist_ok=True)
        (private/(args.component+'-app.json')).write_text(json.dumps(record,indent=2))
        print(json.dumps(dict(identifier=identifier,installed=bool(record),version=record.get('CFBundleShortVersionString'),hasPath=bool(record.get('Path')),hasContainer=bool(record.get('Container')))))

if __name__=='__main__':main()
