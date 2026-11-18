import unittest
import io
import urllib.error
from health import probe


class HealthTests(unittest.TestCase):
    def test_matching_revision_does_not_hide_a_failed_api_health_check(self):
        response = io.BytesIO()
        def fetch(url):
            if "/deployment" in url:
                return {"sha": "a" * 40, "environment": "dev"}
            raise urllib.error.HTTPError(url, 404, "Propagation in progress", {}, response)
        self.assertFalse(probe("https://example.invalid", "a" * 40, "dev", fetch))
        self.assertTrue(response.closed)

    def test_rejects_the_other_environment_even_with_same_images(self):
        self.assertFalse(probe("https://example.invalid", "a" * 40, "dev",
            lambda url: {"sha": "a" * 40, "environment": "production"}))

    def test_accepts_only_matching_revision_and_healthy_api(self):
        def fetch(url):
            return {"sha": "a" * 40, "environment": "dev"} if "/deployment" in url else {"status": "ok", "database": "connected"}
        self.assertTrue(probe("https://example.invalid", "a" * 40, "dev", fetch))

    def test_requires_database_health(self):
        def fetch(url):
            return {"sha": "a" * 40, "environment": "dev"} if "/deployment" in url else {"status": "ok", "database": "unavailable"}
        self.assertFalse(probe("https://example.invalid", "a" * 40, "dev", fetch))
