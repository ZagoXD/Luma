import unittest
from deploy import validate_manifest, render_environment


class DeploymentTests(unittest.TestCase):
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
            render_environment({"environment": "dev", "api": {"Stripe__SecretKey": "rk_live_no"}, "web": {}, "postgres": {}}, "a" * 40)

    def test_requires_private_data_and_auth_keys(self):
        with self.assertRaises(ValueError):
            render_environment({"environment": "production", "api": {}, "web": {}, "postgres": {}}, "a" * 40)

    def test_renders_separate_environment_files(self):
        source = {"environment": "dev", "api": {"Stripe__SecretKey": "rk_test_example",
            "Privacy__EncryptionKey": "key", "Privacy__LookupPepper": "pepper", "Luma__JwtSigningKey": "jwt"},
            "web": {}, "postgres": {"POSTGRES_PASSWORD": "dbpassword"}}
        files = render_environment(source, "a" * 40)
        self.assertIn("rk_test_example", files["api.env"])
        self.assertNotIn("rk_test_example", files["web.env"])
        self.assertNotIn("dbpassword", files["web.env"])
        self.assertIn("IMAGE_TAG", files[".env"])


if __name__ == "__main__":
    unittest.main()
