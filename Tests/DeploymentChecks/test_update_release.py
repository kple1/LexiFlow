import base64
import contextlib
import hashlib
import importlib.util
import io
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch


SPEC = importlib.util.spec_from_file_location('update_deploy', Path(__file__).resolve().parents[2] / 'Tools' / 'UpdateRelease' / 'deploy.py')
DEPLOY = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(DEPLOY)


class UpdateDeploymentChecks(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix='lexiflow-update-deploy-test-')
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.server = self.root / 'Server'; self.server.mkdir()
        self.staging = self.root / 'staging'; self.staging.mkdir()
        self.stage = self.staging / 'release-isolated-test'; self.stage.mkdir()
        self.compose = b'services:\n  proxy:\n    volumes:\n      - ./Caddyfile:/etc/caddy/Caddyfile:ro\n'
        self.caddy = b'original API proxy configuration\n'
        (self.server / 'docker-compose.yml').write_bytes(self.compose)
        (self.server / 'Caddyfile').write_bytes(self.caddy)
        (self.stage / 'Caddyfile').write_bytes(b'candidate static route and API proxy\n')
        self.binary = b'MZ isolated signed artifact placeholder'
        self.hash = hashlib.sha256(self.binary).hexdigest().upper()
        (self.stage / 'LexiFlow.exe').write_bytes(self.binary)
        self.manifest = {'Schema': 1, 'Product': 'LexiFlow', 'Platform': 'windows-x64', 'Version': '1.6.1', 'Sha256': self.hash,
                         'Length': len(self.binary), 'Url': DEPLOY.ORIGIN + '/updates/windows-x64/1.6.1/LexiFlow.exe'}
        self.feed = json.dumps({'Payload': base64.b64encode(json.dumps(self.manifest).encode()).decode(), 'Signature': 'local signature validation is a separate required step'}).encode()
        (self.stage / 'latest.json').write_bytes(self.feed)
        self.calls = []

    def execute(self, unhealthy=False, changed_backend=False):
        identities = {}

        def command(*args):
            self.calls.append(args)
            if args[:2] == ('docker', 'inspect'):
                name = args[-1]; identities[name] = identities.get(name, 0) + 1
                return name + ('-changed' if changed_backend and identities[name] > 1 else '-original')
            return ''

        def response(path, method='GET'):
            if path == '/health': return (503 if unhealthy else 200), {}, b''
            if path == '/ranking': return 401, {}, b''
            if path.endswith('latest.json'): return 200, {}, self.feed
            return 200, {'Content-Length': str(len(self.binary))}, b''

        with patch.object(DEPLOY, 'SERVER', self.server), patch.object(DEPLOY, 'STAGING', self.staging), patch.object(DEPLOY, '__file__', str(self.stage / 'deploy.py')), \
                patch.object(DEPLOY.sys, 'argv', ['deploy.py', '1.6.1', self.hash]), patch.object(DEPLOY, 'run', side_effect=command), \
                patch.object(DEPLOY, 'status', side_effect=response), patch.object(DEPLOY.time, 'sleep'), contextlib.redirect_stdout(io.StringIO()):
            DEPLOY.main()

    def test_promotes_only_verified_public_binary_and_proxy(self):
        self.execute()
        self.assertEqual((self.server / 'public-updates/windows-x64/1.6.1/LexiFlow.exe').read_bytes(), self.binary)
        self.assertEqual((self.server / 'public-updates/windows-x64/latest.json').read_bytes(), self.feed)
        self.assertIn(('docker', 'compose', 'up', '-d', '--no-deps', 'proxy'), self.calls)
        self.assertFalse(any('api' in command or 'db' in command for command in self.calls if command[:3] == ('docker', 'compose', 'up')))
        self.assertEqual((self.stage / 'original-config/docker-compose.yml').read_bytes(), self.compose)

    def test_bad_health_restores_configuration_and_removes_only_own_feed(self):
        with self.assertRaises(RuntimeError): self.execute(unhealthy=True)
        self.assertEqual((self.server / 'docker-compose.yml').read_bytes(), self.compose)
        self.assertEqual((self.server / 'Caddyfile').read_bytes(), self.caddy)
        self.assertFalse((self.server / 'public-updates/windows-x64/latest.json').exists())
        self.assertTrue((self.server / 'public-updates/windows-x64/1.6.1/LexiFlow.exe').exists())

    def test_backend_identity_change_is_not_accepted(self):
        with self.assertRaises(RuntimeError): self.execute(changed_backend=True)
        self.assertEqual((self.server / 'docker-compose.yml').read_bytes(), self.compose)

    def test_tampered_artifact_cannot_touch_proxy(self):
        (self.stage / 'LexiFlow.exe').write_bytes(b'tampered')
        with self.assertRaises(ValueError): self.execute()
        self.assertEqual(self.calls, [])
        self.assertEqual((self.server / 'Caddyfile').read_bytes(), self.caddy)

    def test_same_version_feed_cannot_be_replaced(self):
        public = self.server / 'public-updates/windows-x64'; public.mkdir(parents=True)
        (public / 'latest.json').write_bytes(self.feed)
        with self.assertRaises(ValueError): self.execute()
        self.assertEqual(self.calls, [])
        self.assertEqual((public / 'latest.json').read_bytes(), self.feed)

    def test_existing_different_version_artifact_is_preserved(self):
        release = self.server / 'public-updates/windows-x64/1.6.1'; release.mkdir(parents=True)
        (release / 'LexiFlow.exe').write_bytes(b'never overwrite')
        with self.assertRaises(ValueError): self.execute()
        self.assertEqual((release / 'LexiFlow.exe').read_bytes(), b'never overwrite')
        self.assertEqual(self.calls, [])

    def test_unexpected_proxy_layout_stops_before_mutation(self):
        (self.server / 'docker-compose.yml').write_bytes(b'unexpected configuration')
        with self.assertRaises(ValueError): self.execute()
        self.assertEqual(self.calls, [])
        self.assertEqual((self.server / 'Caddyfile').read_bytes(), self.caddy)


if __name__ == '__main__':
    unittest.main()
