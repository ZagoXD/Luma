#!/usr/bin/env python3
"""Install only signed releases from the project's public GitHub repository."""
import contextlib
import datetime
try:
    import fcntl
except ImportError:
    fcntl = None
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import tempfile
import time
import urllib.error
import urllib.request

ROOT = Path(os.environ.get("LUMA_DEPLOY_ROOT", "/opt/luma"))
REPO = "ZagoXD/Luma"
REGION = "us-east-2"


def verification_key_path(environment):
    return ROOT / "infra/aws/keys" / (environment + ".pem")


def runtime_file_mode(name):
    # The Redis process is non-root; the enclosing host directory remains mode 0700.
    return 0o644 if name == "redis.conf" else 0o600


def validate_manifest(data, environment):
    if environment not in ("dev", "production") or data.get("environment") != environment or data.get("schema") != 1:
        raise ValueError("Invalid environment or manifest schema")
    sha = data.get("sha", "")
    if not re.fullmatch(r"[a-f0-9]{40}", sha):
        raise ValueError("Invalid commit SHA")
    if set(data.get("assets", {})) != {"api", "web"}:
        raise ValueError("Missing image assets")
    for name, asset in data["assets"].items():
        if asset.get("file") != name + ".tar.gz" or asset.get("image") != f"luma-{name}:{sha}":
            raise ValueError("Unexpected image or asset path")
        if not re.fullmatch(r"[a-f0-9]{64}", asset.get("sha256", "")):
            raise ValueError("Invalid asset digest")


def render_environment(config, sha):
    environment = config.get("environment")
    if environment not in ("dev", "production"):
        raise ValueError("Unknown environment")
    api = config["api"]
    if environment == "production" and str(api.get("Twilio__VerifyDevelopmentMode", "false")).lower() == "true":
        raise ValueError("Simulated phone verification forbidden in production")
    for key in ("Privacy__EncryptionKey", "Privacy__LookupPepper", "Luma__JwtSigningKey"):
        if not api.get(key):
            raise ValueError("Missing required security configuration")
    if not config["postgres"].get("POSTGRES_PASSWORD"):
        raise ValueError("Missing database password")
    if environment == "dev" and re.match(r"[sr]k_live_", api.get("Stripe__SecretKey", "")):
        raise ValueError("Live Stripe key forbidden in dev")
    if environment == "production" and re.match(r"[sr]k_test_", api.get("Stripe__SecretKey", "")):
        raise ValueError("Test Stripe key forbidden in production")

    def encode(values):
        return "".join(f"{key}={json.dumps(str(value), ensure_ascii=True)}\n" for key, value in values.items())

    return {"api.env": encode(api), "web.env": encode(config["web"]),
            "postgres.env": encode(config["postgres"]),
            ".env": encode({"IMAGE_TAG": sha, "ENVIRONMENT": environment}),
            "redis.conf": "appendonly yes\nmaxmemory 48mb\nmaxmemory-policy volatile-lru\nrequirepass " + config.get("redisPassword", "") + "\n"}


def run(args, **kwargs):
    return subprocess.run(args, check=True, **kwargs)


def fetch(url, path):
    with urllib.request.urlopen(url, timeout=90) as response, path.open("wb") as output:
        while chunk := response.read(1024 * 1024):
            output.write(chunk)


def aws_json(*args):
    response = run(["aws", *args, "--region", REGION, "--output", "json"], capture_output=True, text=True)
    return json.loads(response.stdout)


def load_secret(name):
    return json.loads(aws_json("secretsmanager", "get-secret-value", "--secret-id", name)["SecretString"])


def compose(environment, *args, **kwargs):
    runtime = ROOT / "runtime" / environment
    return run(["docker", "compose", "--project-name", "luma-" + environment,
                "--env-file", str(runtime / ".env"), "-f", str(ROOT / "infra/aws/compose.json"), *args], **kwargs)


def backup(environment):
    runtime = ROOT / "runtime" / environment
    if not (runtime / "seeded").exists():
        return
    with tempfile.TemporaryDirectory(prefix="luma-backup-") as directory:
        dump = Path(directory) / "database.dump"
        with dump.open("wb") as output:
            compose(environment, "exec", "-T", "postgres", "pg_dump", "-U", "luma", "-d", "luma", "-Fc", stdout=output)
        date = datetime.datetime.now(datetime.timezone.utc).strftime("%Y%m%dT%H%M%SZ")
        bucket = (ROOT / "bucket").read_text().strip()
        run(["aws", "s3", "cp", str(dump), f"s3://{bucket}/backups/{environment}/{date}.dump", "--sse", "AES256", "--only-show-errors", "--region", REGION])


def wait_ready(environment):
    for attempt in range(90):
        try:
            redis = run(["docker", "inspect", "--format", "{{.State.Health.Status}}", f"luma-{environment}-redis-1"], capture_output=True, text=True)
            if redis.stdout.strip() != "healthy":
                raise subprocess.CalledProcessError(1, "redis health check")
            run(["docker", "exec", "luma-gateway", "wget", "-qO", "/dev/null", f"http://{environment}-api:8080/health"], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
            run(["docker", "exec", "luma-gateway", "wget", "-qO", "/dev/null", f"http://{environment}-web:3000/"], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
            return
        except subprocess.CalledProcessError:
            time.sleep(2)
    raise RuntimeError("Application health checks failed")


def seed(environment):
    runtime = ROOT / "runtime" / environment
    if (runtime / "seeded").exists():
        return
    bucket = (ROOT / "bucket").read_text().strip()
    with tempfile.TemporaryDirectory(prefix="luma-seed-") as directory:
        dump = Path(directory) / "seed.dump"
        run(["aws", "s3", "cp", f"s3://{bucket}/seeds/{environment}.dump", str(dump), "--only-show-errors", "--region", REGION])
        compose(environment, "exec", "-T", "postgres", "psql", "-U", "luma", "-d", "luma", "-v", "ON_ERROR_STOP=1", "-c", "CREATE EXTENSION IF NOT EXISTS vector;")
        with dump.open("rb") as source:
            compose(environment, "exec", "-T", "postgres", "pg_restore", "-U", "luma", "-d", "luma", "--no-owner", "--no-acl", "--exit-on-error", stdin=source)
    (runtime / "seeded").touch(mode=0o600)


def install(environment):
    runtime = ROOT / "runtime" / environment
    runtime.mkdir(parents=True, exist_ok=True, mode=0o700)
    with tempfile.TemporaryDirectory(prefix="luma-release-") as directory:
        temporary = Path(directory)
        base = f"https://github.com/{REPO}/releases/download/aws-channel-{environment}"
        manifest, signature = temporary / "manifest.json", temporary / "manifest.sig"
        check = str(time.time_ns())
        try:
            fetch(base + "/manifest.json?check=" + check, manifest)
            fetch(base + "/manifest.sig?check=" + check, signature)
        except urllib.error.HTTPError as error:
            error.close()
            if error.code == 404:
                return
            raise
        run(["openssl", "pkeyutl", "-verify", "-pubin", "-inkey", str(verification_key_path(environment)),
             "-rawin", "-in", str(manifest), "-sigfile", str(signature)], stdout=subprocess.DEVNULL)
        release = json.loads(manifest.read_bytes())
        validate_manifest(release, environment)
        current_path = ROOT / "status" / (environment + ".json")
        previous = json.loads(current_path.read_text()) if current_path.exists() else None
        if previous and previous["sha"] == release["sha"]:
            return
        for asset in release["assets"].values():
            archive = temporary / asset["file"]
            fetch(f"https://github.com/{REPO}/releases/download/aws-{release['sha']}/{asset['file']}", archive)
            with archive.open("rb") as source:
                digest = hashlib.sha256()
                for chunk in iter(lambda: source.read(1024 * 1024), b""):
                    digest.update(chunk)
            if digest.hexdigest() != asset["sha256"]:
                raise ValueError("Image checksum mismatch")
            run(["docker", "load", "--input", str(archive)], stdout=subprocess.DEVNULL)
        config = load_secret("/luma/" + environment + "/runtime")
        backup(environment)
        for name, value in render_environment(config, release["sha"]).items():
            target = runtime / name
            target.write_text(value)
            target.chmod(runtime_file_mode(name))
        try:
            compose(environment, "up", "-d", "--wait", "postgres", "redis")
            seed(environment)
            compose(environment, "up", "-d", "--wait", "--wait-timeout", "180", "api", "web")
            wait_ready(environment)
        except Exception:
            if previous:
                (runtime / ".env").write_text(f"IMAGE_TAG={previous['sha']}\nENVIRONMENT={environment}\n")
                compose(environment, "up", "-d", "api", "web")
            raise
        release["deployedAt"] = datetime.datetime.now(datetime.timezone.utc).isoformat()
        if previous:
            release["previousSha"] = previous["sha"]
        staged = current_path.with_suffix(".tmp")
        staged.write_text(json.dumps(release))
        staged.chmod(0o644)
        staged.replace(current_path)
        print(json.dumps({"environment": environment, "sha": release["sha"], "status": "healthy"}), flush=True)
        # Keep current and previous images for rollback; remove only superseded Luma tags.
        keep = {release["sha"], previous["sha"] if previous else ""}
        for status in (ROOT / "status").glob("*.json"):
            installed = json.loads(status.read_text())
            keep.update((installed["sha"], installed.get("previousSha", "")))
        for name in ("api", "web"):
            listing = run(["docker", "images", "luma-" + name, "--format", "{{.Tag}}"], capture_output=True, text=True)
            for tag in listing.stdout.splitlines():
                if re.fullmatch(r"[a-f0-9]{40}", tag) and tag not in keep:
                    run(["docker", "image", "rm", "luma-" + name + ":" + tag], stdout=subprocess.DEVNULL)


def main():
    if fcntl is None:
        raise RuntimeError("The deployment service requires Linux")
    ROOT.mkdir(parents=True, exist_ok=True)
    with (ROOT / "deploy.lock").open("w") as lock:
        fcntl.flock(lock, fcntl.LOCK_EX)
        if len(sys.argv) > 1 and sys.argv[1] == "backup":
            for environment in ("dev", "production"):
                backup(environment)
        else:
            for environment in ("dev", "production"):
                try:
                    install(environment)
                except Exception as error:
                    # Do not print command output or provider configuration containing secrets.
                    print(json.dumps({"environment": environment, "error": type(error).__name__}), flush=True)


if __name__ == "__main__":
    main()
