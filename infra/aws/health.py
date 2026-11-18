"""Check both the selected deployment and API while DNS/routes converge."""
import json
import sys
import time
import urllib.error
import urllib.request


def fetch_json(url):
    request = urllib.request.Request(url, headers={"Cache-Control": "no-cache", "User-Agent": "Luma-deployment-health"})
    with urllib.request.urlopen(request, timeout=15) as response:
        return json.load(response)


def probe(base, sha, environment, fetch=fetch_json):
    try:
        check = str(int(time.time()))
        deployment = fetch(base.rstrip("/") + "/deployment?check=" + check)
        if deployment.get("sha") != sha or deployment.get("environment") != environment:
            return False
        health = fetch(base.rstrip("/") + "/health?check=" + check)
        return health.get("status") == "ok" and health.get("database") == "connected"
    except urllib.error.HTTPError as error:
        error.close()
        return False
    except (OSError, ValueError, urllib.error.URLError):
        return False


if __name__ == "__main__":
    base, sha, environment = sys.argv[1:4]
    deadline = time.monotonic() + 600
    while time.monotonic() < deadline:
        if probe(base, sha, environment):
            print(json.dumps({"environment": environment, "sha": sha, "healthy": True}))
            sys.exit(0)
        print("Waiting for deployment revision and API health", flush=True)
        time.sleep(5)
    sys.exit("Deployment did not become healthy within ten minutes")
