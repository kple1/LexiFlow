import importlib.util
import json
from pathlib import Path
import sys
import unittest
from unittest.mock import patch, MagicMock

BACKUP_DIR = Path(__file__).resolve().parents[2] / 'Server/backup'
sys.path.insert(0, str(BACKUP_DIR))
spec = importlib.util.spec_from_file_location('backup_monitor', BACKUP_DIR / 'monitor.py')
monitor = importlib.util.module_from_spec(spec)
spec.loader.exec_module(monitor)


class MonitorChecks(unittest.TestCase):
    def test_secret_exception_is_redacted(self):
        with patch.object(monitor.backup, 'load_config', side_effect=ValueError('SECRET_PAR')):
            issues = monitor.collect_health()
        self.assertEqual(issues, ['HEALTH_CHECK_FAILED_ValueError'])
        self.assertNotIn('SECRET_PAR', str(issues))

    def test_stopped_timer_and_failed_service_reported(self):
        state = {'last_success': {'file': 'safe.cms', 'sha256': 'digest'}}
        root = MagicMock()
        (root / 'status.json').read_text.return_value = json.dumps(state)
        with patch.object(monitor.backup, 'ROOT', root), \
             patch.object(monitor.backup, 'load_config', return_value={}), \
             patch.object(monitor.backup, 'health_issues', return_value=[]), \
             patch.object(monitor.backup, 'verify_cloud') as verify, \
             patch.object(monitor.subprocess, 'run', side_effect=[MagicMock(returncode=3), MagicMock(stdout='timeout\n')]):
            self.assertEqual(monitor.collect_health(), ['BACKUP_TIMER_INACTIVE', 'BACKUP_SERVICE_FAILED'])
            verify.assert_called_once_with({}, 'safe.cms', 'digest')

    def test_healthy_publishes_boolean_only(self):
        with patch.object(monitor, 'collect_health', return_value=[]), \
             patch.object(monitor, 'publish') as publish, \
             patch.object(monitor.backup, 'write_json'), patch('builtins.print'):
            self.assertEqual(monitor.main(), 0)
            publish.assert_called_once_with(True)

    def test_publication_failure_is_nonzero_and_redacted(self):
        with patch.object(monitor, 'collect_health', return_value=['STALE']), \
             patch.object(monitor, 'publish', side_effect=ValueError('SECRET')), \
             patch('builtins.print') as output:
            self.assertEqual(monitor.main(), 1)
            self.assertEqual(output.call_args.args[0], 'Metric publication failed: ValueError')


if __name__ == '__main__':
    unittest.main()
