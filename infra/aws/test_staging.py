import unittest
from pathlib import Path


class PausedDeploymentTests(unittest.TestCase):
    def test_staging_is_opt_in_and_does_not_claim_runtime_health(self):
        workflow = (Path(__file__).resolve().parents[2] / ".github/workflows/deploy-aws.yml").read_text()
        self.assertIn("stage_only:", workflow)
        self.assertIn("STAGE_ONLY: ${{ inputs.stage_only || 'false' }}", workflow)
        self.assertIn("openssl pkeyutl -verify -pubin", workflow)
        self.assertIn('validate_manifest(data, "dev")', workflow)
        self.assertIn("if: inputs.stage_only != true", workflow)
        self.assertIn("if: inputs.stage_only == true", workflow)
        self.assertIn("not installed or health-validated", workflow)


if __name__ == "__main__":
    unittest.main()
