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
