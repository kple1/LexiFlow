#!/usr/bin/env python3
"""Daily encrypted PostgreSQL backup; never restore over the production database.

The deployment host retains only a public recovery certificate. The private key
is kept off-host. OCI credentials are read from a mode-0600 file, never argv/logs.
"""
import argparse
import datetime as dt
import hashlib
import json
import os
from pathlib import Path
import re
import select
import shutil
import subprocess
import sys
import tarfile
import tempfile
import time
import urllib.error
import urllib.parse
import urllib.request
import uuid

ROOT = Path('/home/ubuntu/lexiflow-daily-backups')
CONFIG = Path('/home/ubuntu/.config/lexiflow-backup/config.json')
DB_CONTAINER = 'server-db-1'
MAX_BYTES = 8 * 1024 * 1024
RETENTION_DAYS = 7
STALE_HOURS = 30
NAME = re.compile(r'worddb-(\d{4}-\d{2}-\d{2})\.tar\.cms\Z')


def now():
    return dt.datetime.now(dt.timezone.utc)


def stamp(value):
    return value.isoformat()


def sha(path):
    with path.open('rb') as source:
        return hashlib.file_digest(source, 'sha256').hexdigest()


def write_json(path, data):
    # All callers hold the one host lock. Never follow a preexisting temp link.
    temp = path.with_name(path.name + '.' + uuid.uuid4().hex + '.tmp')
    with temp.open('x') as output:
        json.dump(data, output, indent=2)
        output.flush()
        os.fsync(output.fileno())
    temp.chmod(0o600)
    temp.replace(path)


def run(args, **kwargs):
    return subprocess.run(args, check=True, timeout=600, stderr=subprocess.PIPE, **kwargs)


def psql(database):
    return ['docker', 'exec', '-i', DB_CONTAINER, 'psql', '-X', '-qAt',
            '-v', 'ON_ERROR_STOP=1', '-U', 'worddb', '-d', database]


def quote_identifier(value):
    return '"' + value.replace('"', '""') + '"'


def count_query(names):
    # Aggregate counts only, never print account rows or password/session hashes.
    pairs = []
    for name in names:
        pairs.extend(["'" + name.replace("'", "''") + "'",
                      '(SELECT COUNT(*) FROM public.' + quote_identifier(name) + ')'])
    return 'SELECT json_build_object(' + ','.join(pairs) + ');'


def safe_rehearsal(name):
    if not re.fullmatch(r'lexiflow_verify_[a-f0-9]{32}', name):
        raise ValueError('Refusing non-disposable restore target')
    return name


def sql(database, statement):
    return run(psql(database), input=statement, text=True, stdout=subprocess.PIPE).stdout.strip()


def snapshot_dump(destination):
    # Keep one read-only exported snapshot alive for both exact counts and pg_dump.
    with tempfile.TemporaryFile() as errors:
        process = subprocess.Popen(psql('worddb'), stdin=subprocess.PIPE,
                                   stdout=subprocess.PIPE, stderr=errors, text=True, bufsize=1)
        def query(statement):
            process.stdin.write(statement + '\n')
            process.stdin.flush()
            if not select.select([process.stdout], [], [], 60)[0]:
                raise TimeoutError('Snapshot query timed out')
            result = process.stdout.readline().strip()
            if not result:
                raise RuntimeError('Snapshot query failed')
            return result
        try:
            snapshot = query("BEGIN ISOLATION LEVEL REPEATABLE READ READ ONLY; "
                             "SET idle_in_transaction_session_timeout='10min'; SELECT pg_export_snapshot();")
            if not re.fullmatch(r'[0-9A-F]+-[0-9A-F]+-[0-9]+', snapshot):
                raise RuntimeError('Invalid exported snapshot')
            names = json.loads(query("SELECT json_agg(tablename ORDER BY tablename) FROM pg_tables WHERE schemaname='public';"))
            if not names or 'Users' not in names or '__EFMigrationsHistory' not in names:
                raise RuntimeError('Unexpected database schema')
            counts = json.loads(query(count_query(names)))
            # Bound even a failed/unexpectedly large dump before it fills the disk.
            import resource
            def cap_file_size():
                resource.setrlimit(resource.RLIMIT_FSIZE, (MAX_BYTES, MAX_BYTES))
            with destination.open('xb') as output:
                run(['docker', 'exec', DB_CONTAINER, 'pg_dump', '-U', 'worddb', '-d', 'worddb',
                     '--format=custom', '--no-owner', '--no-acl', '--lock-wait-timeout=30s',
                     '--snapshot=' + snapshot], stdout=output, preexec_fn=cap_file_size)
            return names, counts
        finally:
            process.stdin.close()
            try:
                process.wait(timeout=10)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait()
            process.stdout.close()


def restore_check(dump, names, expected):
    target = safe_rehearsal('lexiflow_verify_' + uuid.uuid4().hex)
    created = False
    try:
        run(['docker', 'exec', DB_CONTAINER, 'createdb', '-U', 'worddb', '-T', 'template0', target], stdout=subprocess.DEVNULL)
        created = True
        with dump.open('rb') as source:
            run(['docker', 'exec', '-i', DB_CONTAINER, 'pg_restore', '-U', 'worddb', '-d', target,
                 '--exit-on-error', '--single-transaction', '--no-owner', '--no-acl'], stdin=source, stdout=subprocess.DEVNULL)
        restored = json.loads(sql(target, count_query(names)))
        if restored != expected:
            raise RuntimeError('Restored counts do not match exported snapshot')
        if sql(target, "SELECT COUNT(*) FROM pg_constraint WHERE connamespace='public'::regnamespace AND NOT convalidated;") != '0':
            raise RuntimeError('Restored constraints are not valid')
        return restored
    finally:
        if created:
            # Only the unpredictable name created in THIS invocation can be dropped.
            run(['docker', 'exec', DB_CONTAINER, 'dropdb', '-U', 'worddb', safe_rehearsal(target)], stdout=subprocess.DEVNULL)


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        raise RuntimeError('Cloud redirect denied')


def load_config(path=CONFIG):
    if path.is_symlink() or path.stat().st_mode & 0o077:
        raise ValueError('Backup configuration must be a private regular file')
    config = json.loads(path.read_text())
    parsed = urllib.parse.urlsplit(config['par_url'])
    if (parsed.scheme != 'https' or parsed.hostname != 'objectstorage.ap-tokyo-1.oraclecloud.com'
            or parsed.port not in (None, 443) or parsed.username or parsed.query or parsed.fragment
            or not re.fullmatch(r'/p/[^/]+/n/nrddt9lcbdzc/b/lexiflow-encrypted-backups/o/', parsed.path)):
        raise ValueError('Unexpected cloud backup destination')
    expires = dt.datetime.fromisoformat(config['par_expires_at'])
    if expires.tzinfo is None or expires <= now():
        raise ValueError('Cloud backup access expired')
    cert = Path(config['certificate'])
    if cert.is_symlink() or cert.resolve().parent != CONFIG.parent.resolve():
        raise ValueError('Unexpected recovery certificate path')
    config['certificate_sha256'] = sha(cert)
    return config


def object_url(config, name):
    if not NAME.fullmatch(name):
        raise ValueError('Invalid backup object name')
    return config['par_url'] + 'daily/' + name


def cloud_request(config, name, method='GET', data=None):
    headers = {'Content-Type': 'application/octet-stream'}
    if method == 'PUT':
        headers['If-None-Match'] = '*'
    request = urllib.request.Request(object_url(config, name), data=data, headers=headers, method=method)
    return urllib.request.build_opener(NoRedirect()).open(request, timeout=60)


def verify_cloud(config, name, expected_hash):
    with cloud_request(config, name) as response:
        data = response.read(MAX_BYTES + 1)
    if len(data) > MAX_BYTES or hashlib.sha256(data).hexdigest() != expected_hash:
        raise RuntimeError('Cloud round-trip checksum mismatch')


def expired_files(directory, today):
    # Only our exact daily artifacts; existing migration/deployment backups excluded.
    candidates = []
    for path in directory.iterdir():
        match = NAME.fullmatch(path.name)
        if match and path.is_file() and not path.is_symlink() and path.resolve().parent == directory.resolve():
            if (today - dt.date.fromisoformat(match[1])).days >= RETENTION_DAYS:
                candidates.append(path)
    return candidates


def health_issues(state, config, current):
    issues = []
    success = state.get('last_success')
    if not success or current - dt.datetime.fromisoformat(success['completed_at']) > dt.timedelta(hours=STALE_HOURS):
        issues.append('BACKUP_STALE_OR_MISSING')
    if state.get('last_result') == 'failed':
        issues.append('LATEST_RUN_FAILED')
    if dt.datetime.fromisoformat(config['par_expires_at']) - current < dt.timedelta(days=14):
        issues.append('CLOUD_ACCESS_EXPIRES_SOON')
    if not state.get('recovery_key_verified_at'):
        issues.append('OFF_HOST_RECOVERY_KEY_NOT_VERIFIED')
    elif config.get('certificate_sha256') and config['certificate_sha256'] != state.get('recovery_certificate_sha256'):
        issues.append('RECOVERY_CERTIFICATE_CHANGED_WITHOUT_TEST')
    return issues


def backup(config, state):
    current = now()
    name = 'worddb-' + current.date().isoformat() + '.tar.cms'
    final = ROOT / name
    # Idempotent retry: never overwrite a successful daily cloud restore point.
    previous = state.get('last_success', {})
    if previous.get('file') == name:
        verify_cloud(config, name, previous['sha256'])
        return previous
    pending = ROOT / 'pending.json'
    if final.exists():
        record = json.loads(pending.read_text())
        if record.get('file') != name or sha(final) != record.get('sha256'):
            raise RuntimeError('Uncommitted artifact mismatch; inspect before retry')
        return finish_upload(config, final, record, current)
    if shutil.disk_usage(ROOT).free < 256 * 1024 * 1024:
        raise RuntimeError('Insufficient backup disk space')
    with tempfile.TemporaryDirectory(prefix='.work-', dir=ROOT) as temp:
        work = Path(temp)
        dump = work / 'worddb.dump'
        names, counts = snapshot_dump(dump)
        with dump.open('rb') as source:
            header = source.read(5)
        if not 5 < dump.stat().st_size < MAX_BYTES or header != b'PGDMP':
            raise RuntimeError('Invalid or oversized dump')
        restore_check(dump, names, counts)
        manifest = {'created_at': stamp(current), 'database': 'worddb', 'counts': counts,
                    'dump_sha256': sha(dump), 'restore_verified': True,
                    'recovery_certificate_sha256': sha(Path(config['certificate']))}
        write_json(work / 'manifest.json', manifest)
        bundle = work / 'backup.tar.gz'
        with tarfile.open(bundle, 'w:gz') as archive:
            for filename in ('worddb.dump', 'manifest.json'):
                archive.add(work / filename, arcname=filename)
        encrypted = work / name
        run(['openssl', 'cms', '-encrypt', '-aes-256-gcm', '-binary', '-outform', 'DER',
             '-in', str(bundle), '-out', str(encrypted), '-recip', config['certificate'],
             '-keyopt', 'rsa_padding_mode:oaep', '-keyopt', 'rsa_oaep_md:sha256',
             '-keyopt', 'rsa_mgf1_md:sha256'], stdout=subprocess.DEVNULL)
        if encrypted.stat().st_size >= MAX_BYTES:
            raise RuntimeError('Encrypted backup exceeds 8 MiB safety cap')
        digest = sha(encrypted)
        encrypted.replace(final)
        record = {'file': name, 'sha256': digest, 'bytes': final.stat().st_size,
                  'counts': counts, 'restore_verified': True}
        write_json(pending, record)
    return finish_upload(config, final, record, current)


def finish_upload(config, final, record, current):
    # Retain encrypted pending data on transient network errors. Retries upload
    # the identical bytes and verify a preexisting object rather than overwrite it.
    try:
        with cloud_request(config, final.name, 'PUT', final.read_bytes()):
            pass
    except urllib.error.HTTPError as error:
        if error.code != 412:
            raise
    verify_cloud(config, final.name, record['sha256'])
    record.update(completed_at=stamp(now()), cloud_roundtrip_verified=True)
    # Only prune after today's independently verified remote copy exists.
    for path in expired_files(ROOT, current.date()):
        path.unlink()
    return record


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--check', action='store_true', help='Read-only health and cloud checksum check')
    args = parser.parse_args()
    os.umask(0o077)
    state_file = ROOT / 'status.json'
    state = json.loads(state_file.read_text()) if state_file.exists() else {}
    if args.check:
        try:
            config = load_config()
            issues = health_issues(state, config, now())
            success = state.get('last_success')
            if success:
                verify_cloud(config, success['file'], success['sha256'])
            result = {'healthy': not issues, 'issues': issues, 'last_success': success,
                      'checked_at': stamp(now()), 'par_expires_at': config['par_expires_at']}
        except Exception as error:
            result = {'healthy': False, 'issues': ['CHECK_FAILED_' + type(error).__name__]}
        print(json.dumps(result, indent=2))
        return 0 if result['healthy'] else 1
    ROOT.mkdir(mode=0o700, exist_ok=True)
    import fcntl
    with (ROOT / '.lock').open('a') as lock:
        try:
            fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        except BlockingIOError:
            print('Backup already running.')
            return 0
        state = json.loads(state_file.read_text()) if state_file.exists() else {}
        try:
            config = load_config()
            state['last_success'] = backup(config, state)
            state.update(last_result='success', last_attempt=stamp(now()))
            state.pop('error_type', None)
            write_json(state_file, state)
            print(json.dumps(state, indent=2))
            return 0
        except Exception as error:
            # Exceptions can contain secret PAR URLs or PostgreSQL rows. Never log str(error).
            state.update(last_result='failed', last_attempt=stamp(now()), error_type=type(error).__name__)
            write_json(state_file, state)
            print('Backup failed: ' + type(error).__name__ + '. Prior backups retained.', file=sys.stderr)
            return 1


if __name__ == '__main__':
    sys.exit(main())
