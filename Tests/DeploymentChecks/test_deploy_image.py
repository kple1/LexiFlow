import importlib.util
from pathlib import Path
import tempfile
import unittest

spec = importlib.util.spec_from_file_location('deploy_image', Path(__file__).resolve().parents[2] / 'Server' / 'deploy-image.py')
deploy = importlib.util.module_from_spec(spec)
spec.loader.exec_module(deploy)


class DeploymentConfigurationChecks(unittest.TestCase):
    def test_preserves_secrets_and_domain(self):
        original = 'DB_PASSWORD=example-$-with-specials\nADMIN_TOKEN=example-only\nAPI_DOMAIN=example.invalid\nAPI_IMAGE=old\n'
        updated = deploy.set_value(original, 'API_IMAGE', 'new')
        self.assertEqual(updated, original.replace('API_IMAGE=old', 'API_IMAGE=new'))

    def test_adds_image_and_disables_implicit_migrations(self):
        original = 'DB_PASSWORD=example-only\nAPPLY_MIGRATIONS=true\n'
        updated = deploy.set_value(deploy.set_value(original, 'API_IMAGE', 'new'), 'APPLY_MIGRATIONS', 'false')
        self.assertIn('API_IMAGE=new\n', updated)
        self.assertIn('APPLY_MIGRATIONS=false\n', updated)
        self.assertNotIn('APPLY_MIGRATIONS=true', updated)

    def test_atomic_write_retains_complete_content(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / '.env'
            deploy.save_config(path, 'API_IMAGE=first\n')
            deploy.save_config(path, 'API_IMAGE=second\n')
            self.assertEqual(path.read_text(), 'API_IMAGE=second\n')
            self.assertFalse((path.parent / '.env.deploy-new').exists())

    def test_does_not_overwrite_unexpected_temporary_file(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / '.env'
            path.write_text('API_IMAGE=old\n')
            temporary = path.parent / '.env.deploy-new'
            temporary.write_text('unfinished')
            with self.assertRaises(FileExistsError):
                deploy.save_config(path, 'API_IMAGE=new\n')
            self.assertEqual(path.read_text(), 'API_IMAGE=old\n')


if __name__ == '__main__':
    unittest.main()
