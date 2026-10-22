"""Publish the verified MacroStudio release using existing GitHub Git credentials."""
import argparse
import hashlib
import http.client
import json
import os
from pathlib import Path
import subprocess
import urllib.error
import urllib.parse
import urllib.request

ROOT = Path(__file__).resolve().parents[2]
REPO = 'StephenTheCoder/freemacrorecorder'
TAG = 'v1.0.0'
COMMIT = '990ded25ffc480df3b3ede52d070655716db718d'
API = 'https://api.github.com'
HERE = Path(__file__).resolve().parent
ASSETS = [ROOT / 'artifacts/MacroStudio-1.0-win-x64.zip', ROOT / 'artifacts/MacroStudio-1.0-source.zip']


def auth():
    token = os.environ.get('GH_TOKEN') or os.environ.get('GITHUB_TOKEN')
    if not token:
        environment = dict(os.environ, GIT_TERMINAL_PROMPT='0', GCM_INTERACTIVE='never')
        credential = subprocess.run(['git', 'credential', 'fill'], input='protocol=https\nhost=github.com\n\n',
                                    text=True, capture_output=True, env=environment, timeout=30, cwd=ROOT)
        fields = dict(line.split('=', 1) for line in credential.stdout.splitlines() if '=' in line)
        token = fields.get('password')
    if not token:
        raise RuntimeError('No existing noninteractive GitHub credential is available.')
    return {'Authorization': 'Bearer ' + token, 'Accept': 'application/vnd.github+json',
            'X-GitHub-Api-Version': '2022-11-28', 'User-Agent': 'MacroStudio-release'}


def request(headers, path, method='GET', data=None, allow404=False):
    body = None if data is None else json.dumps(data).encode()
    req = urllib.request.Request(API + path, data=body, headers={**headers, 'Content-Type': 'application/json'}, method=method)
    try:
        with urllib.request.urlopen(req, timeout=60) as response:
            return json.load(response)
    except urllib.error.HTTPError as error:
        if error.code == 404 and allow404:
            return None
        payload = json.loads(error.read().decode())
        raise RuntimeError(f'GitHub {method} {path}: HTTP {error.code}: {payload.get("message", "request failed")}') from None


def sha(path):
    result = hashlib.sha256()
    with path.open('rb') as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b''):
            result.update(chunk)
    return result.hexdigest()


def upload(headers, url, path):
    parsed = urllib.parse.urlsplit(url)
    if parsed.scheme != 'https' or parsed.hostname != 'uploads.github.com':
        raise RuntimeError('Unexpected GitHub upload host.')
    target = parsed.path + '?' + urllib.parse.urlencode({'name': path.name})
    connection = http.client.HTTPSConnection(parsed.hostname, timeout=300)
    try:
        connection.putrequest('POST', target)
        for name, value in {**headers, 'Content-Type': 'application/zip' if path.suffix == '.zip' else 'text/plain',
                            'Content-Length': str(path.stat().st_size)}.items():
            connection.putheader(name, value)
        connection.endheaders()
        with path.open('rb') as stream:
            for chunk in iter(lambda: stream.read(1024 * 1024), b''):
                connection.send(chunk)
        response = connection.getresponse()
        data = json.loads(response.read())
        if response.status not in (200, 201):
            raise RuntimeError(f'Asset upload failed: HTTP {response.status}: {data.get("message", "request failed")}')
        return data
    finally:
        connection.close()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--publish', action='store_true')
    args = parser.parse_args()
    headers = auth()
    repo = request(headers, '/repos/' + REPO)
    releases = request(headers, '/repos/' + REPO + '/releases?per_page=20')
    branch = request(headers, '/repos/' + REPO + '/git/ref/heads/main')
    if branch['object']['sha'] != COMMIT:
        raise RuntimeError('Remote main changed; inspect it before publishing.')
    evidence = json.loads((ROOT / 'work/package-verification.json').read_text())
    expected = [evidence['zip_sha256'], evidence['source']['sha256']]
    for path, expected_hash in zip(ASSETS, expected):
        if sha(path) != expected_hash:
            raise RuntimeError('Verified asset hash changed: ' + path.name)
    release = next((item for item in releases if item['tag_name'] == TAG), None)
    print(json.dumps({'repository': repo['full_name'], 'can_push': repo.get('permissions', {}).get('push'),
                      'commit': COMMIT, 'existing_release': None if release is None else release['html_url'],
                      'assets': [{'name': p.name, 'bytes': p.stat().st_size, 'sha256': sha(p)} for p in ASSETS]}, indent=2), flush=True)
    if not args.publish:
        return
    notes = '''MacroStudio 1.0 is a local Windows desktop macro recorder with system-wide recording and playback.

### Download and run
Download **MacroStudio-1.0-win-x64.zip**, extract the entire folder, then run **MacroStudio.exe**. Windows 11 x64; the .NET runtime is included. The bundled Interaction Lab opens from the toolbar.

### Included
- Mouse paths, clicks, drags, scrolling, keyboard shortcuts and Unicode text; an editable action sequence with undo/redo and local versioned JSON files.
- Configurable global record/play/pause/stop shortcuts, adjustable speed and repeats, selection playback, window-relative coordinates, window restoration, and light/dark themes.
- Emergency cancellation with held-key/button cleanup, source code, samples, and reproducible build/verification scripts.

### Verification
17 core tests, 7 editor checks, and 9 native integration checks passed. The packaged application was launched and successfully replayed a reopened recording in the bundled lab. Every release archive member was verified against the local build.

Mixed-DPI multi-monitor hardware, alternate IMEs/layouts, and elevated/secure-desktop targets were not hardware-tested. See [setup and use](https://github.com/StephenTheCoder/freemacrorecorder/blob/v1.0.0/README.md) and [verification details](https://github.com/StephenTheCoder/freemacrorecorder/blob/v1.0.0/docs/VERIFICATION.md).

**Default shortcuts:** F8 record, F9 play, F10 pause/resume, Escape stop.
'''
    if release and not release['draft']:
        raise RuntimeError('A published release already exists; refusing to alter it.')
    if not release:
        release = request(headers, '/repos/' + REPO + '/releases', 'POST', {
            'tag_name': TAG, 'target_commitish': COMMIT, 'name': 'MacroStudio 1.0.0', 'body': notes,
            'draft': True, 'prerelease': False, 'generate_release_notes': False})
    checksums = HERE / 'SHA256SUMS.txt'
    checksums.write_text(''.join(f'{sha(path)}  {path.name}\n' for path in ASSETS), encoding='utf-8')
    release_id = release['id']
    remote_assets = request(headers, f'/repos/{REPO}/releases/{release_id}/assets?per_page=100')
    for path in [*ASSETS, checksums]:
        existing = next((a for a in remote_assets if a['name'] == path.name), None)
        if existing:
            if existing['size'] != path.stat().st_size or existing.get('digest') != 'sha256:' + sha(path):
                raise RuntimeError('Existing draft asset differs: ' + path.name)
        else:
            print('Uploading ' + path.name, flush=True)
            result = upload(headers, release['upload_url'].split('{')[0], path)
            if result['size'] != path.stat().st_size or result.get('digest') != 'sha256:' + sha(path):
                raise RuntimeError('Remote asset verification failed: ' + path.name)
    tag = request(headers, f'/repos/{REPO}/git/ref/tags/{TAG}', allow404=True)
    if tag is None:
        request(headers, f'/repos/{REPO}/git/refs', 'POST', {'ref': 'refs/tags/' + TAG, 'sha': COMMIT})
    elif tag['object']['sha'] != COMMIT:
        raise RuntimeError('The release tag points to another object; inspect it before publishing.')
    published = request(headers, f'/repos/{REPO}/releases/{release_id}', 'PATCH', {'draft': False, 'make_latest': 'true'})
    verified = request(headers, f'/repos/{REPO}/releases/tags/{TAG}')
    if verified['draft'] or verified['id'] != release_id or len(verified['assets']) != 3:
        raise RuntimeError('Published release verification failed.')
    summary = {'url': verified['html_url'], 'tag': TAG, 'commit': COMMIT, 'draft': verified['draft'],
               'assets': [{k: a.get(k) for k in ('name', 'size', 'digest', 'browser_download_url')} for a in verified['assets']]}
    (HERE / 'published.json').write_text(json.dumps(summary, indent=2), encoding='utf-8')
    print(json.dumps(summary, indent=2), flush=True)


if __name__ == '__main__':
    try:
        main()
    except Exception as error:
        print(f'Release operation stopped: {error}', flush=True)
        raise SystemExit(1)
