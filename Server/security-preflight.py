#!/usr/bin/env python3
"""Back up the existing deployment and rehearse a restore without changing worddb.

Run on the deployment host. Prints counts and paths only, never credentials or rows.
The protected backup and isolated rehearsal database are deliberately retained.
"""
import datetime
import json
import os
from pathlib import Path
import subprocess

os.umask(0o077)
server = Path('/home/ubuntu/LexiFlow/Server')
if not server.is_dir():
    raise SystemExit('Expected deployment directory is missing.')
os.chdir(server)
container = 'server-db-1'

def run(args, **kwargs):
    return subprocess.run(args, check=True, **kwargs)

def sql(database, statement):
    result = run(['docker', 'exec', '-i', container, 'psql', '-X', '-v', 'ON_ERROR_STOP=1',
                  '-U', 'worddb', '-d', database, '-At'], input=statement, text=True, capture_output=True)
    return result.stdout.strip()

def counts(database):
    names = sql(database, "SELECT tablename FROM pg_tables WHERE schemaname='public' ORDER BY tablename;").splitlines()
    return {name: int(sql(database, 'SELECT COUNT(*) FROM "' + name.replace('"', '""') + '";')) for name in names}

duplicate_count = int(sql('worddb', 'SELECT COUNT(*) FROM (SELECT "UserId" FROM "Users" GROUP BY "UserId" HAVING COUNT(*) > 1) duplicates;'))
orphan_counts = {}
for table in ('WordProgresses', 'GrammarProgresses', 'IdiomProgresses'):
    orphan_counts[table] = int(sql('worddb', f'SELECT COUNT(*) FROM "{table}" p LEFT JOIN "Users" u ON u."UserId"=p."UserId" WHERE u."Id" IS NULL;'))
print(json.dumps({'duplicate_ids': duplicate_count, 'orphan_counts': orphan_counts}), flush=True)

stamp = datetime.datetime.now(datetime.timezone.utc).strftime('%Y%m%dT%H%M%SZ')
backup_dir = Path('/home/ubuntu/lexiflow-security-backups') / stamp
backup_dir.mkdir(mode=0o700, parents=True, exist_ok=False)
backup = backup_dir / 'worddb.dump'
before_counts = counts('worddb')
with backup.open('xb') as output:
    run(['docker', 'exec', container, 'pg_dump', '-U', 'worddb', '-d', 'worddb',
         '--format=custom', '--no-owner', '--no-acl'], stdout=output)
if backup.stat().st_size == 0:
    raise SystemExit('Empty backup. Stop deployment.')
with backup.open('rb') as source:
    run(['docker', 'exec', '-i', container, 'pg_restore', '--list'], stdin=source, stdout=subprocess.DEVNULL)

# Retain a restricted copy of configuration for recovery, without printing secrets.
for name in ('.env', 'docker-compose.yml', 'Caddyfile'):
    path = server / name
    if path.is_file():
        with (backup_dir / name).open('xb') as output:
            output.write(path.read_bytes())
image = run(['docker', 'inspect', 'server-api-1', '--format', '{{.Image}}'], text=True, capture_output=True).stdout.strip()
rehearsal = 'security_rehearsal_' + stamp.lower()
run(['docker', 'exec', container, 'createdb', '-U', 'worddb', rehearsal])
with backup.open('rb') as source:
    run(['docker', 'exec', '-i', container, 'pg_restore', '-U', 'worddb', '-d', rehearsal,
         '--exit-on-error', '--no-owner', '--no-acl'], stdin=source)
restored_counts = counts(rehearsal)
after_counts = counts('worddb')
result = {'backup': str(backup), 'backup_bytes': backup.stat().st_size, 'previous_image': image,
          'rehearsal_db': rehearsal, 'before_counts': before_counts, 'restored_counts': restored_counts,
          'after_counts': after_counts, 'duplicate_ids': duplicate_count, 'orphan_counts': orphan_counts}
with (backup_dir / 'preflight.json').open('x') as output:
    json.dump(result, output, indent=2)
print(json.dumps(result, indent=2), flush=True)
if before_counts != restored_counts or restored_counts != after_counts:
    raise SystemExit('Counts changed during backup or do not match. Review before deployment.')
if duplicate_count or any(orphan_counts.values()):
    raise SystemExit('Existing identity conflicts require a data ownership decision. Stop deployment.')
print('PASS: backup restores and table counts match; production database was not modified.')
