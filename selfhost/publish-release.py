"""Create a reviewable draft, upload a verified client ZIP, then publish it.

Uses Git's credential helper in memory. Credentials are never printed or saved.
"""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import urllib.error
import urllib.parse
import urllib.request

REPO = "Necthus/EldenRingSeamlessCoopThirdPartyServer"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--tag", required=True)
    parser.add_argument("--asset", type=Path, required=True)
    parser.add_argument("--notes", type=Path, required=True)
    parser.add_argument("--publish", action="store_true")
    args = parser.parse_args()
    credential = subprocess.run(["git", "credential", "fill"], input="protocol=https\nhost=github.com\n\n",
                                capture_output=True, text=True, check=True)
    fields = dict(line.split("=", 1) for line in credential.stdout.splitlines() if "=" in line)
    token = fields.get("password")
    if not token:
        raise RuntimeError("No GitHub credential available")

    def call(url, method="GET", body=None, content_type="application/json", raw=False):
        headers = {"Authorization": "Bearer " + token, "Accept": "application/vnd.github+json",
                   "User-Agent": "SeamlessCoop-Release", "X-GitHub-Api-Version": "2022-11-28"}
        if body is not None:
            headers["Content-Type"] = content_type
        request = urllib.request.Request(url, data=body, method=method, headers=headers)
        try:
            with urllib.request.urlopen(request, timeout=120) as response:
                data = response.read()
                return data if raw else json.loads(data)
        except urllib.error.HTTPError as error:
            if error.code == 404:
                return None
            raise RuntimeError(f"GitHub API returned HTTP {error.code} for {method} request") from None

    asset = args.asset.resolve()
    if asset.suffix != ".zip" or not asset.name.startswith("EldenRingSeamlessCoop-Enhanced-"):
        raise ValueError("Expected client ZIP asset")
    fingerprint = hashlib.sha256(asset.read_bytes()).hexdigest()
    commit = subprocess.check_output(["git", "rev-parse", "HEAD"], text=True).strip()
    base = "https://api.github.com/repos/" + REPO
    releases = call(base + "/releases?per_page=100")
    release = next((r for r in releases if r["tag_name"] == args.tag), None)
    if release and not release["draft"]:
        print(json.dumps({"status": "already_published", "url": release["html_url"], "assets": [a["name"] for a in release["assets"]]}, indent=2))
        return
    notes = args.notes.read_text(encoding="utf-8-sig") + f"\n\n附件 SHA-256：\n```text\n{fingerprint}\n```\n"
    if release is None:
        release = call(base + "/releases", "POST", json.dumps({"tag_name": args.tag, "target_commitish": commit,
            "name": "增强无缝联机客户端 " + args.tag, "body": notes, "draft": True, "prerelease": False}, ensure_ascii=False).encode("utf-8"))
    else:
        if release["target_commitish"] != commit:
            raise ValueError("Draft commit differs from current HEAD")
        release = call(base + "/releases/" + str(release["id"]), "PATCH", json.dumps({"tag_name": args.tag, "body": notes}, ensure_ascii=False).encode("utf-8"))
    # One client ZIP only. Never upload deployment files or extra local trees.
    assets = call(base + "/releases/" + str(release["id"]) + "/assets")
    if any(item["name"] != asset.name for item in assets):
        raise ValueError("Draft has unexpected assets; refusing to publish")
    uploaded = next((item for item in assets if item["name"] == asset.name), None)
    if uploaded is None:
        upload = release["upload_url"].split("{", 1)[0] + "?name=" + urllib.parse.quote(asset.name)
        uploaded = call(upload, "POST", asset.read_bytes(), "application/zip")
    if uploaded["size"] != asset.stat().st_size:
        raise ValueError("Uploaded asset size mismatch")
    remote = call(uploaded["url"], raw=False)
    remote_digest = remote.get("digest")
    if remote_digest != "sha256:" + fingerprint:
        raise ValueError("GitHub uploaded asset SHA-256 mismatch or unavailable")
    if args.publish:
        release = call(base + "/releases/" + str(release["id"]), "PATCH", json.dumps({"tag_name": args.tag, "target_commitish": commit, "draft": False}).encode("utf-8"))
        if release["tag_name"] != args.tag:
            raise ValueError("Published Release tag differs from requested tag")
        uploaded = next(item for item in call(base + "/releases/" + str(release["id"]) + "/assets") if item["name"] == asset.name)
    print(json.dumps({"status": "published" if not release["draft"] else "draft_ready", "url": release["html_url"],
        "id": release["id"], "tag": release["tag_name"], "asset": asset.name,
        "download_url": uploaded["browser_download_url"], "bytes": uploaded["size"], "sha256": fingerprint}, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
