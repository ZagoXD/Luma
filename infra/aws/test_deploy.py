import unittest
import json
import io
import tempfile
import urllib.error
from unittest.mock import patch
from pathlib import Path
from deploy import validate_manifest, render_environment, verification_key_path, runtime_file_mode, install


class DeploymentTests(unittest.TestCase):
    def test_production_rejects_simulated_phone_verification(self):
        source = {"environment": "production", "api": {"Twilio__VerifyDevelopmentMode": "true",
            "Privacy__EncryptionKey": "key", "Privacy__LookupPepper": "pepper", "Luma__JwtSigningKey": "jwt"},
            "web": {}, "postgres": {"POSTGRES_PASSWORD": "test-only"}}
        with self.assertRaises(ValueError):
            render_environment(source, "a" * 40)

    def test_channel_checks_bypass_stale_redirects_and_close_missing_responses(self):
        response = io.BytesIO()
        error = urllib.error.HTTPError("https://example.invalid", 404, "Not published", {}, response)
        with tempfile.TemporaryDirectory() as directory, patch("deploy.ROOT", Path(directory)):
            with patch("deploy.fetch", side_effect=error) as fetch:
                install("dev")
                self.assertIn("?check=", fetch.call_args.args[0])
                self.assertTrue(response.closed)

    def test_redis_config_is_readable_inside_container_but_other_secret_files_stay_private(self):
        self.assertEqual(runtime_file_mode("redis.conf"), 0o644)
        for name in ("api.env", "web.env", "postgres.env", ".env"):
            self.assertEqual(runtime_file_mode(name), 0o600)

    def test_api_waits_for_authenticated_redis_health_check(self):
        compose = json.loads(Path(__file__).with_name("compose.json").read_text())
        self.assertIn("healthcheck", compose["services"]["redis"])
        self.assertEqual(compose["services"]["api"]["depends_on"]["redis"]["condition"], "service_healthy")

    def test_verification_key_matches_bootstrap_layout(self):
        self.assertEqual(verification_key_path("dev").as_posix(), "/opt/luma/infra/aws/keys/dev.pem")

    def manifest(self):
        sha = "a" * 40
        return {"schema": 1, "environment": "dev", "sha": sha, "assets": {
            name: {"file": name + ".tar.gz", "sha256": "b" * 64, "image": "luma-" + name + ":" + sha}
            for name in ("api", "web")}}

    def test_accepts_expected_artifacts(self):
        validate_manifest(self.manifest(), "dev")

    def test_rejects_environment_confusion(self):
        with self.assertRaises(ValueError):
            validate_manifest(self.manifest(), "production")

    def test_rejects_shell_injection_and_path_traversal(self):
        for field, value in (("sha", "$(touch /tmp/unsafe)"), ("file", "../../etc/passwd"), ("image", "attacker/image:latest")):
            data = self.manifest()
            if field == "sha":
                data[field] = value
            else:
                data["assets"]["api"][field] = value
            with self.assertRaises(ValueError):
                validate_manifest(data, "dev")

    def test_refuses_live_credentials_in_dev(self):
        with self.assertRaises(ValueError):
            render_environment({"environment": "dev", "api": {"MercadoPago__TestMode": "false"}, "web": {}, "postgres": {}}, "a" * 40)

    def test_requires_private_data_and_auth_keys(self):
        with self.assertRaises(ValueError):
            render_environment({"environment": "production", "api": {}, "web": {}, "postgres": {}}, "a" * 40)

    def test_renders_separate_environment_files(self):
        source = {"environment": "dev", "api": {"MercadoPago__AccessToken": "test_example", "MercadoPago__TestMode": "true",
            "Privacy__EncryptionKey": "key", "Privacy__LookupPepper": "pepper", "Luma__JwtSigningKey": "jwt"},
            "web": {}, "postgres": {"POSTGRES_PASSWORD": "dbpassword"}}
        files = render_environment(source, "a" * 40)
        self.assertIn("test_example", files["api.env"])
        self.assertNotIn("test_example", files["web.env"])
        self.assertNotIn("dbpassword", files["web.env"])
        self.assertIn("IMAGE_TAG", files[".env"])


if __name__ == "__main__":
    unittest.main()
