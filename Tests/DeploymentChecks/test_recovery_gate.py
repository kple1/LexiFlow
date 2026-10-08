from pathlib import Path
import unittest


class RecoveryDeploymentGateChecks(unittest.TestCase):
    def test_recovery_requires_explicit_rollout_gate(self):
        workflow = (Path(__file__).resolve().parents[2] / '.github/workflows/ci.yml').read_text(encoding='utf-8')
        deploy_condition = next(line.strip() for line in workflow.splitlines() if line.strip().startswith('if: vars.DEPLOY_ENABLED'))
        self.assertEqual(deploy_condition, "if: vars.DEPLOY_ENABLED == 'true' && vars.SECURITY_DEPLOY_READY == 'true' && vars.ACCOUNT_RECOVERY_DEPLOY_READY == 'true'")

    def test_postgres_checks_are_in_build_gate(self):
        workflow = (Path(__file__).resolve().parents[2] / '.github/workflows/ci.yml').read_text(encoding='utf-8')
        build = workflow.split('  push-image:')[0]
        self.assertIn('image: postgres:17', build)
        self.assertIn('LEXIFLOW_TEST_POSTGRES: Host=127.0.0.1;Port=55439;Database=postgres;Username=lexiflow_test;', build)
        self.assertIn('PostgreSQL migration and concurrent account recovery checks', build)


if __name__ == '__main__':
    unittest.main()
