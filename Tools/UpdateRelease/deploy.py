"""Publish public, locally signature-verified update artifacts; never deploy API/schema.

Upload this script, Caddyfile, latest.json and LexiFlow.exe to a fresh directory in
/home/ubuntu/lexiflow-update-deploy, then run: python3 deploy.py VERSION SHA256.
Signature verification happens on the signing PC and independently in every client.
"""
import base64
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import time
import urllib.error
import urllib.request

SERVER = Path('/home/ubuntu/LexiFlow/Server')
STAGING = Path('/home/ubuntu/lexiflow-update-deploy')
ORIGIN = 'https://lexiflow.duckdns.org'


def run(*args):
    return subprocess.check_output(args, cwd=SERVER, text=True).strip()


def digest(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest().upper()


def status(path, method='GET'):
    request = urllib.request.Request(ORIGIN + path, method=method)
    try:
        with urllib.request.urlopen(request, timeout=20) as response:
            return response.status, response.headers, response.read(16385) if method == 'GET' else b''
    except urllib.error.HTTPError as error:
        return error.code, error.headers, b''


def main():
    if len(sys.argv) != 3 or not re.fullmatch(r'(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)', sys.argv[1]) or not re.fullmatch(r'[0-9A-Fa-f]{64}', sys.argv[2]):
        raise ValueError('Expected canonical VERSION and SHA256.')
    version, expected = sys.argv[1], sys.argv[2].upper()
    stage = Path(__file__).resolve().parent
    if stage.parent != STAGING or not stage.name.startswith('release-') or not SERVER.is_dir():
        raise ValueError('Use a fresh, explicit deployment staging directory.')
    for path in (stage, SERVER, stage / 'LexiFlow.exe', stage / 'latest.json', stage / 'Caddyfile', SERVER / 'docker-compose.yml', SERVER / 'Caddyfile'):
        if path.is_symlink():
            raise ValueError('Symlink deployment targets are not supported.')
    feed = (stage / 'latest.json').read_bytes()
    if len(feed) > 16384:
        raise ValueError('Oversized feed.')
    envelope = json.loads(feed)
    manifest = json.loads(base64.b64decode(envelope['Payload'], validate=True))
    source = stage / 'LexiFlow.exe'
    if (manifest['Schema'], manifest['Product'], manifest['Platform'], manifest['Version'], manifest['Sha256'].upper(), manifest['Length'], manifest['Url']) != (
            1, 'LexiFlow', 'windows-x64', version, expected, source.stat().st_size, ORIGIN + '/updates/windows-x64/' + version + '/LexiFlow.exe') or digest(source) != expected:
        raise ValueError('Public artifact/manifest mismatch.')
    public = SERVER / 'public-updates' / 'windows-x64'
    public.mkdir(parents=True, exist_ok=True)
    if public.resolve() != public or public.is_symlink():
        raise ValueError('Unsafe public directory.')
    latest = public / 'latest.json'
    previous_feed = latest.read_bytes() if latest.exists() else None
    if previous_feed is not None:
        previous = json.loads(base64.b64decode(json.loads(previous_feed)['Payload'], validate=True))
        if tuple(map(int, version.split('.'))) <= tuple(map(int, previous['Version'].split('.'))):
            raise ValueError('Release must be strictly newer; never replace a published version.')
    release = public / version
    release.mkdir(exist_ok=True)
    if release.resolve() != release or release.is_symlink():
        raise ValueError('Unsafe release directory.')
    target = release / 'LexiFlow.exe'
    if target.exists():
        if target.is_symlink() or digest(target) != expected:
            raise ValueError('Never overwrite an existing different release.')
    else:
        incoming = release / ('incoming-' + stage.name)
        with source.open('rb') as source_stream, incoming.open('xb') as target_stream:
            shutil.copyfileobj(source_stream, target_stream)
            target_stream.flush()
            os.fsync(target_stream.fileno())
        if digest(incoming) != expected:
            raise ValueError('Copy verification failed.')
        os.chmod(incoming, 0o644)
        os.replace(incoming, target)
    compose_path, caddy_path = SERVER / 'docker-compose.yml', SERVER / 'Caddyfile'
    compose, caddy = compose_path.read_bytes(), caddy_path.read_bytes()
    new_caddy = (stage / 'Caddyfile').read_bytes()
    needle = b'      - ./Caddyfile:/etc/caddy/Caddyfile:ro\n'
    mount = b'      - ./public-updates:/srv/lexiflow-updates:ro\n'
    if compose.count(needle) != 1 or compose.count(mount) > 1:
        raise ValueError('Unexpected proxy configuration; stop for manual review.')
    new_compose = compose if mount in compose else compose.replace(needle, needle + mount, 1)
    backend_ids = {name: run('docker', 'inspect', '--format', '{{.Id}}', 'server-' + name + '-1') for name in ('api', 'db')}
    backup = stage / 'original-config'
    backup.mkdir(mode=0o700)
    shutil.copy2(compose_path, backup / 'docker-compose.yml')
    shutil.copy2(caddy_path, backup / 'Caddyfile')
    candidate = '/tmp/lexiflow-update-candidate-' + stage.name
    run('docker', 'cp', str(stage / 'Caddyfile'), 'server-proxy-1:' + candidate)
    run('docker', 'exec', 'server-proxy-1', 'caddy', 'validate', '--config', candidate, '--adapter', 'caddyfile')
    changed_proxy = new_compose != compose or new_caddy != caddy
    promoted = False
    try:
        if changed_proxy:
            compose_path.write_bytes(new_compose)
            caddy_path.write_bytes(new_caddy)
            run('docker', 'compose', 'config', '-q')
            run('docker', 'compose', 'up', '-d', '--no-deps', 'proxy')
        feed_temp = public / ('feed-' + stage.name + '.json')
        with feed_temp.open('xb') as stream:
            stream.write(feed); stream.flush(); os.fsync(stream.fileno())
        os.chmod(feed_temp, 0o644)
        os.replace(feed_temp, latest); promoted = True
        for attempt in range(12):
            try:
                health, _, _ = status('/health')
                ranking, _, _ = status('/ranking')
                feed_code, _, received = status('/updates/windows-x64/latest.json')
                binary_code, headers, _ = status('/updates/windows-x64/' + version + '/LexiFlow.exe', 'HEAD')
                if (health, ranking, feed_code, binary_code) == (200, 401, 200, 200) and received == feed and int(headers.get('Content-Length', '0')) == manifest['Length']:
                    break
            except (OSError, ValueError):
                pass
            time.sleep(2)
        else:
            raise RuntimeError('HTTPS health/authentication/update verification failed.')
        if any(run('docker', 'inspect', '--format', '{{.Id}}', 'server-' + name + '-1') != identity for name, identity in backend_ids.items()):
            raise RuntimeError('Backend container identity unexpectedly changed.')
        report = {'Version': version, 'Length': manifest['Length'], 'Sha256': expected, 'Health': health, 'AnonymousRanking': ranking,
                  'Feed': feed_code, 'Executable': binary_code, 'BackendContainersUnchanged': True, 'ProxyRecreated': changed_proxy}
        (stage / 'deployment-result.json').write_text(json.dumps(report, indent=2))
        print(json.dumps(report))
    except Exception:
        if promoted:
            if previous_feed is None:
                if latest.read_bytes() == feed:
                    latest.unlink()  # Only this invocation's feed; retain binary for inspection.
            else:
                latest.write_bytes(previous_feed)
        if changed_proxy:
            compose_path.write_bytes(compose); caddy_path.write_bytes(caddy)
            run('docker', 'compose', 'up', '-d', '--no-deps', 'proxy')
        raise


if __name__ == '__main__':
    main()
