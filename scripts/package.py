#!/usr/bin/env python3
"""Build the plugin for each Jellyfin line, zip it, and add the zips to manifest.json.

    python3 scripts/package.py v0.1.0 [--repo owner/name]

Writes dist/lilin_<version>.zip per Jellyfin line and puts their entries at the top of
manifest.json (the plugin repository Jellyfin reads). The fourth version number is the Jellyfin
major version the zip is for (12 or 10), so a server always picks the newest build it can run.
"""
import argparse
import datetime
import hashlib
import json
import os
import re
import shutil
import subprocess
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
PROJECT = ROOT / 'Jellyfin.Plugin.Lilin' / 'Jellyfin.Plugin.Lilin.csproj'
# (target framework, targetAbi, fourth version number)
LINES = [('net10.0', '12.0.0.0', 12), ('net9.0', '10.11.0.0', 10)]


def repo_slug():
    if os.environ.get('GITHUB_REPOSITORY'):
        return os.environ['GITHUB_REPOSITORY']
    url = subprocess.run(['git', 'remote', 'get-url', 'origin'], cwd=ROOT, capture_output=True, text=True).stdout.strip()
    m = re.search(r'github\.com[:/](.+?)(?:\.git)?$', url)
    if not m:
        raise SystemExit('Pass --repo owner/name (no GitHub origin found).')
    return m.group(1)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('tag')
    ap.add_argument('--repo')
    ap.add_argument('--dotnet', default=os.environ.get('DOTNET', 'dotnet'))
    a = ap.parse_args()
    m = re.fullmatch(r'v?(\d+)\.(\d+)\.(\d+)', a.tag)
    if not m:
        raise SystemExit('Tag must look like v1.2.3')
    base = '.'.join(m.groups())
    repo = a.repo or repo_slug()
    manifest_path = ROOT / 'manifest.json'
    manifest = json.loads(manifest_path.read_text())
    plugin = manifest[0]
    now = datetime.datetime.now(datetime.timezone.utc).strftime('%Y-%m-%dT%H:%M:%SZ')
    dist = ROOT / 'dist'
    shutil.rmtree(dist, ignore_errors=True)
    dist.mkdir()
    entries = []
    for tfm, abi, rev in LINES:
        version = f'{base}.{rev}'
        out = ROOT / 'out' / tfm
        shutil.rmtree(out, ignore_errors=True)
        subprocess.run([a.dotnet, 'build', str(PROJECT), '-c', 'Release', '-f', tfm, '-o', str(out),
                        f'-p:Version={version}', f'-p:AssemblyVersion={version}', f'-p:FileVersion={version}'], check=True)
        changelog = f'https://github.com/{repo}/releases/tag/{a.tag}'
        meta = {
            'category': plugin['category'], 'changelog': changelog, 'description': plugin['description'],
            'guid': plugin['guid'], 'name': plugin['name'], 'overview': plugin['overview'], 'owner': plugin['owner'],
            'targetAbi': abi, 'timestamp': now, 'version': version, 'status': 'Active', 'autoUpdate': True,
            'imagePath': '', 'assemblies': ['Jellyfin.Plugin.Lilin.dll'],
        }
        zip_path = dist / f'lilin_{version}.zip'
        with zipfile.ZipFile(zip_path, 'w', zipfile.ZIP_DEFLATED) as z:
            z.write(out / 'Jellyfin.Plugin.Lilin.dll', 'Jellyfin.Plugin.Lilin.dll')
            z.writestr('meta.json', json.dumps(meta, indent=2))
        entries.append({
            'version': version, 'changelog': changelog, 'targetAbi': abi,
            'sourceUrl': f'https://github.com/{repo}/releases/download/{a.tag}/{zip_path.name}',
            'checksum': hashlib.md5(zip_path.read_bytes()).hexdigest(), 'timestamp': now,
        })
        print(f'{zip_path.name}: Jellyfin {abi}+')
    versions = {e['version'] for e in entries}
    plugin['versions'] = entries + [v for v in plugin.get('versions', []) if v['version'] not in versions]
    manifest_path.write_text(json.dumps(manifest, indent=2) + '\n')


if __name__ == '__main__':
    main()
