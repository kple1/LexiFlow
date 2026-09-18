#!/usr/bin/env python3
"""Deploy a verified commit image while preserving protected server configuration."""
import datetime
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import time
import urllib.error
import urllib.request


def run(args, **kwargs):
    return subprocess.run(args, check=True, **kwargs)


def set_value(config, key, value):
    pattern = r'^' + re.escape(key) + r'=.*$'
    if re.search(pattern, config, re.MULTILINE):
        return re.sub(pattern, lambda _: key + '=' + value, config, flags=re.MULTILINE)
    return config.rstrip() + '\n' + key + '=' + value + '\n'


def save_config(path, config):
    temporary = path.with_name('.env.deploy-new')
    with temporary.open('x') as output:
        output.write(config)
    temporary.chmod(0o600)
    temporary.replace(path)


def main():
    os.umask(0o077)
    image = os.environ.get('DEPLOY_IMAGE', '')
    if not re.fullmatch(r'ghcr\.io/kple1/lexiflow/wordapp:[a-f0-9]{40}', image):
        raise SystemExit('A full commit SHA image tag is required.')
    incoming = Path(__file__).resolve().parent
    server = Path('/home/ubuntu/LexiFlow/Server')
    if incoming != server / 'incoming':
        raise SystemExit('Run the staged deployment from Server/incoming.')
    os.chdir(server)
    config_file = server / '.env'
    original_config = config_file.read_text()
    # The protected host file remains authoritative; never reset DB/admin secrets from CI.
    for key in ('DB_PASSWORD', 'ADMIN_TOKEN'):
        if not re.search(r'^' + key + r'=.+$', original_config, re.MULTILINE):
            raise SystemExit('Required protected server configuration is missing.')
    old_image = run(['docker', 'inspect', 'server-api-1', '--format', '{{.Image}}'], text=True, capture_output=True).stdout.strip()
    run(['docker', 'pull', image])
    stamp = datetime.datetime.now(datetime.timezone.utc).strftime('%Y%m%dT%H%M%SZ')
    backup = Path('/home/ubuntu/lexiflow-security-backups') / ('deploy-' + stamp)
    backup.mkdir(mode=0o700, parents=True, exist_ok=False)
    for name in ('.env', 'docker-compose.yml', 'Caddyfile'):
        shutil.copyfile(server / name, backup / name)
    with (backup / 'worddb.dump').open('xb') as output:
        run(['docker', 'exec', 'server-db-1', 'pg_dump', '-U', 'worddb', '-d', 'worddb',
             '--format=custom', '--no-owner', '--no-acl'], stdout=output)
    assert (backup / 'worddb.dump').stat().st_size > 0
    with (backup / 'worddb.dump').open('rb') as source:
        run(['docker', 'exec', '-i', 'server-db-1', 'pg_restore', '--list'], stdin=source, stdout=subprocess.DEVNULL)
    config = set_value(set_value(original_config, 'API_IMAGE', image), 'APPLY_MIGRATIONS', 'false')
    changed = False
    try:
        save_config(config_file, config)
        changed = True
        for name in ('docker-compose.yml', 'Caddyfile'):
            shutil.copyfile(incoming / name, server / name)
        run(['docker', 'compose', 'config', '--quiet'])
        # Do not upgrade or recreate the database during an application deployment.
        run(['docker', 'compose', 'up', '-d', '--no-deps', 'api', 'proxy'])
        expected_id = run(['docker', 'image', 'inspect', image, '--format', '{{.Id}}'], text=True, capture_output=True).stdout.strip()
        actual_id = run(['docker', 'inspect', 'server-api-1', '--format', '{{.Image}}'], text=True, capture_output=True).stdout.strip()
        assert actual_id == expected_id
        healthy = False
        for _ in range(20):
            try:
                with urllib.request.urlopen('https://lexiflow.duckdns.org/health', timeout=3) as response:
                    healthy = response.status == 200
                if healthy:
                    break
            except (urllib.error.URLError, TimeoutError):
                pass
            time.sleep(2)
        if not healthy:
            raise RuntimeError('HTTPS health check failed.')
        try:
            urllib.request.urlopen('https://lexiflow.duckdns.org/users/me', timeout=10)
            raise RuntimeError('Anonymous account access was not denied.')
        except urllib.error.HTTPError as error:
            if error.code != 401:
                raise
        report = {'image': image, 'image_id': actual_id, 'backup': str(backup), 'health': 200, 'anonymous_me': 401}
        with (backup / 'deployment.json').open('x') as output:
            json.dump(report, output, indent=2)
        print(json.dumps(report, indent=2))
    except Exception:
        if changed:
            # No schema migrations were allowed. Restore only application/configuration, never DB data.
            restore = set_value(set_value(original_config, 'API_IMAGE', old_image), 'APPLY_MIGRATIONS', 'false')
            save_config(config_file, restore)
            for name in ('docker-compose.yml', 'Caddyfile'):
                shutil.copyfile(backup / name, server / name)
            run(['docker', 'compose', 'up', '-d', '--no-deps', 'api', 'proxy'])
            print('Deployment failed; previous application image restored. Database was not overwritten.')
        raise


if __name__ == '__main__':
    main()
