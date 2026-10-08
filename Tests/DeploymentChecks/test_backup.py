import datetime as dt
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch, MagicMock
import urllib.error

spec = importlib.util.spec_from_file_location('backup', Path(__file__).resolve().parents[2] / 'Server/backup/backup.py')
backup = importlib.util.module_from_spec(spec)
spec.loader.exec_module(backup)


class BackupSafetyChecks(unittest.TestCase):
    def test_daily_identity_matches_korea_timer_not_utc_calendar(self):
        bootstrap = dt.datetime(2026, 9, 18, 4, 14, tzinfo=dt.timezone.utc)
        scheduled = dt.datetime(2026, 9, 18, 18, 0, 18, tzinfo=dt.timezone.utc)
        self.assertEqual(backup.backup_date(bootstrap), dt.date(2026, 9, 18))
        self.assertEqual(backup.backup_date(scheduled), dt.date(2026, 9, 19))
        self.assertEqual(backup.backup_date(scheduled + dt.timedelta(days=1)), dt.date(2026, 9, 20))

    def test_korea_midnight_and_year_boundary(self):
        before = dt.datetime(2026, 12, 31, 14, 59, 59, tzinfo=dt.timezone.utc)
        self.assertEqual(backup.backup_date(before), dt.date(2026, 12, 31))
        self.assertEqual(backup.backup_date(before + dt.timedelta(seconds=1)), dt.date(2027, 1, 1))
        with self.assertRaises(ValueError):
            backup.backup_date(dt.datetime(2026, 9, 19))

    def test_first_scheduled_run_after_bootstrap_creates_new_restore_point(self):
        scheduled = dt.datetime(2026, 9, 18, 18, 0, 18, tzinfo=dt.timezone.utc)
        old = {'file': 'worddb-2026-09-18.tar.cms', 'sha256': 'old-hash',
               'completed_at': '2026-09-18T04:14:07+00:00'}
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            original = root / old['file']
            original.write_bytes(b'existing encrypted backup')
            cert = root / 'test-certificate.pem'
            cert.write_bytes(b'test certificate')
            def dump(path):
                path.write_bytes(b'PGDMP-synthetic-test-data')
                return ['Users'], {'Users': 4}
            def encrypt(args, **kwargs):
                Path(args[args.index('-out') + 1]).write_bytes(b'new encrypted test backup')
            def finish(config, final, record, current):
                return dict(record, completed_at=current.isoformat(), cloud_roundtrip_verified=True)
            with patch.object(backup, 'ROOT', root), patch.object(backup, 'now', return_value=scheduled), \
                 patch.object(backup, 'snapshot_dump', side_effect=dump) as snapshot, \
                 patch.object(backup, 'restore_check') as restore, patch.object(backup, 'run', side_effect=encrypt), \
                 patch.object(backup, 'finish_upload', side_effect=finish) as upload, patch.object(backup, 'verify_cloud') as verify:
                result = backup.backup({'certificate': str(cert)}, {'last_success': old})
                self.assertEqual(result['file'], 'worddb-2026-09-19.tar.cms')
                snapshot.assert_called_once()
                restore.assert_called_once()
                upload.assert_called_once()
                verify.assert_not_called()
                self.assertEqual(original.read_bytes(), b'existing encrypted backup')
                self.assertEqual(old['completed_at'], '2026-09-18T04:14:07+00:00')

    def test_same_korea_day_retry_verifies_without_redumping_or_refreshing_age(self):
        current = dt.datetime(2026, 9, 18, 18, 5, tzinfo=dt.timezone.utc)
        previous = {'file': 'worddb-2026-09-19.tar.cms', 'sha256': 'known',
                    'completed_at': '2026-09-18T18:00:18+00:00'}
        with patch.object(backup, 'now', return_value=current), patch.object(backup, 'verify_cloud') as verify, \
             patch.object(backup, 'snapshot_dump') as snapshot:
            self.assertIs(backup.backup({}, {'last_success': previous}), previous)
            verify.assert_called_once_with({}, previous['file'], 'known')
            snapshot.assert_not_called()
            self.assertEqual(previous['completed_at'], '2026-09-18T18:00:18+00:00')

    def test_retention_uses_same_korea_date_as_artifact_name(self):
        current = dt.datetime(2026, 9, 18, 18, tzinfo=dt.timezone.utc)
        with tempfile.TemporaryDirectory() as directory:
            final = Path(directory) / 'worddb-2026-09-19.tar.cms'
            final.write_bytes(b'encrypted')
            with patch.object(backup, 'cloud_request', return_value=MagicMock()), patch.object(backup, 'verify_cloud'), \
                 patch.object(backup, 'expired_files', return_value=[]) as expired:
                backup.finish_upload({}, final, {'sha256': 'expected'}, current)
                expired.assert_called_once_with(backup.ROOT, dt.date(2026, 9, 19))

    def test_production_restore_targets_rejected(self):
        for name in ('worddb', 'postgres', 'lexiflow_verify_worddb', 'lexiflow_verify_' + 'a'*32 + ';DROP DATABASE worddb'):
            with self.assertRaises(ValueError):
                backup.safe_rehearsal(name)
        self.assertEqual(backup.safe_rehearsal('lexiflow_verify_' + 'f'*32), 'lexiflow_verify_' + 'f'*32)

    def test_counts_quote_identifiers_and_literals(self):
        result = backup.count_query(['Users', 'a"b', "c'd"])
        self.assertIn('public."a""b"', result)
        self.assertIn("'c''d'", result)

    def test_retention_only_managed_old_files(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            for name in ('worddb-2026-09-10.tar.cms', 'worddb-2026-09-11.tar.cms', 'worddb-2026-09-12.tar.cms', 'worddb-2026-09-18.tar.cms', 'original.dump', 'status.json', 'worddb-2026-09-01.tar.cms.bak'):
                (root / name).touch()
            (root / 'worddb-2026-09-02.tar.cms').mkdir()
            self.assertEqual({p.name for p in backup.expired_files(root, dt.date(2026,9,18))},
                             {'worddb-2026-09-10.tar.cms', 'worddb-2026-09-11.tar.cms'})

    def test_unknown_names_cannot_be_uploaded(self):
        config = {'par_url': 'https://example.invalid/'}
        for name in ('../secrets', 'daily/../secret', '.env', 'worddb.dump'):
            with self.assertRaises(ValueError):
                backup.object_url(config, name)

    def test_cloud_redirects_denied(self):
        with self.assertRaises(RuntimeError):
            backup.NoRedirect().redirect_request(None, None, 302, '', {}, 'https://attacker.invalid/')

    def test_health_staleness_failure_expiry_and_key_verification(self):
        current = dt.datetime(2026,9,18,tzinfo=dt.timezone.utc)
        config = {'par_expires_at': (current + dt.timedelta(days=7)).isoformat()}
        state = {'last_success': {'completed_at': (current-dt.timedelta(hours=31)).isoformat()}, 'last_result':'failed'}
        self.assertEqual(set(backup.health_issues(state,config,current)),
                         {'BACKUP_STALE_OR_MISSING', 'LATEST_RUN_FAILED', 'CLOUD_ACCESS_EXPIRES_SOON', 'OFF_HOST_RECOVERY_KEY_NOT_VERIFIED'})

    def test_healthy_recent_backup(self):
        current = backup.now()
        state = {'last_success': {'completed_at': current.isoformat()}, 'last_result':'success', 'recovery_key_verified_at':current.isoformat()}
        self.assertEqual(backup.health_issues(state, {'par_expires_at':(current+dt.timedelta(days=90)).isoformat()}, current), [])

    def test_failed_cloud_verification_does_not_prune(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            final = root / 'worddb-2026-09-18.tar.cms'
            final.write_bytes(b'encrypted')
            old = root / 'worddb-2026-09-01.tar.cms'
            old.write_bytes(b'keep')
            with patch.object(backup,'ROOT',root), patch.object(backup,'cloud_request',return_value=MagicMock()), patch.object(backup,'verify_cloud',side_effect=RuntimeError('network')):
                with self.assertRaises(RuntimeError):
                    backup.finish_upload({},final,{'sha256':'expected'},backup.now())
            self.assertTrue(old.exists())
            self.assertTrue(final.exists())

    def test_existing_cloud_object_requires_matching_hash(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            final = root / ('worddb-' + backup.now().date().isoformat() + '.tar.cms')
            final.write_bytes(b'encrypted')
            error = urllib.error.HTTPError('redacted',412,'exists',{},None)
            with patch.object(backup,'ROOT',root), patch.object(backup,'cloud_request',side_effect=error), patch.object(backup,'verify_cloud') as verify:
                record = backup.finish_upload({}, final, {'sha256':'expected'}, backup.now())
                verify.assert_called_once_with({}, final.name, 'expected')
                self.assertTrue(record['cloud_roundtrip_verified'])

    def test_atomic_status_replacement(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory)/'status.json'
            backup.write_json(path, {'last_result':'failed'})
            backup.write_json(path, {'last_result':'success'})
            self.assertEqual(json.loads(path.read_text()), {'last_result':'success'})
            self.assertEqual(len(list(path.parent.iterdir())),1)


if __name__ == '__main__':
    unittest.main()
