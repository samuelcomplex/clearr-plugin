#!/usr/bin/env python3
"""
Packages the built plugin into dist/clearr_<version>.zip and adds it to manifest.json.

  python3 tools/package.py <build-output-dir> [--dry]

Version: 2.<Clearr minor>.<Clearr patch>.<n>, where the Clearr part comes from
external/clearr/src/Clearr.Core/Clearr.Core.csproj and n counts plugin releases on top of that Clearr version.
Prints the version on stdout (last line).
"""
import hashlib, json, os, re, subprocess, sys, zipfile, datetime

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
REPO_RAW = "https://raw.githubusercontent.com/samuelcomplex/clearr-plugin/main"
GUID = "94d2b53c-7f01-48dd-8726-c645ebe5ff9b"
TARGET_ABI = "10.11.0.0"
KEEP_VERSIONS = 8

def core_version():
    s = open(os.path.join(ROOT, "external/clearr/src/Clearr.Core/Clearr.Core.csproj")).read()
    m = re.search(r"<Version>(\d+)\.(\d+)\.(\d+)</Version>", s)
    return int(m.group(2)), int(m.group(3))

def clearr_sha():
    return subprocess.check_output(["git", "-C", os.path.join(ROOT, "external/clearr"), "rev-parse", "--short", "HEAD"], text=True).strip()

def next_version(manifest):
    minor, patch = core_version()
    base = f"2.{minor}.{patch}"
    n = 1 + max([int(v["version"].split(".")[3]) for v in manifest[0]["versions"] if v["version"].startswith(base + ".")] or [0])
    return f"{base}.{n}"

def main():
    out = sys.argv[1]
    dry = "--dry" in sys.argv
    mpath = os.path.join(ROOT, "manifest.json")
    manifest = json.load(open(mpath))
    version = os.environ.get("PLUGIN_VERSION") or next_version(manifest)
    sha = clearr_sha()
    now = datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")
    changelog = f"Built from Clearr {sha} (https://github.com/samuelcomplex/clearr/commit/{sha})"

    meta = {
        "category": "General", "changelog": changelog, "description": manifest[0]["description"], "guid": GUID,
        "name": "Clearr", "overview": manifest[0]["overview"], "owner": "samuelcomplex", "targetAbi": TARGET_ABI,
        "timestamp": now, "version": version, "status": "Active", "autoUpdate": True, "imagePath": "logo.png",
        "assemblies": ["Jellyfin.Plugin.Clearr.dll", "Clearr.Core.dll"],
    }
    os.makedirs(os.path.join(ROOT, "dist"), exist_ok=True)
    zpath = os.path.join(ROOT, "dist", f"clearr_{version}.zip")
    if dry:
        print(version); return
    with zipfile.ZipFile(zpath, "w", zipfile.ZIP_DEFLATED) as z:
        for dll in meta["assemblies"]:
            z.write(os.path.join(out, dll), dll)
        z.write(os.path.join(ROOT, "external/clearr/docs/logo/banner.png"), "logo.png")
        z.writestr("meta.json", json.dumps(meta, indent=2))
    md5 = hashlib.md5(open(zpath, "rb").read()).hexdigest()

    manifest[0]["name"] = "Clearr"
    manifest[0]["imageUrl"] = "https://raw.githubusercontent.com/samuelcomplex/clearr/main/docs/logo/banner.png"
    entry = {"version": version, "changelog": changelog, "targetAbi": TARGET_ABI,
             "sourceUrl": f"{REPO_RAW}/dist/clearr_{version}.zip", "checksum": md5, "timestamp": now}
    versions = [entry] + [v for v in manifest[0]["versions"] if v["version"] != version]
    # keep the newest few; delete zips that drop off
    for old in versions[KEEP_VERSIONS:]:
        f = os.path.join(ROOT, "dist", old["sourceUrl"].rsplit("/", 1)[-1])
        if os.path.exists(f):
            os.remove(f)
    manifest[0]["versions"] = versions[:KEEP_VERSIONS]
    json.dump(manifest, open(mpath, "w"), indent=2)
    print(version)

if __name__ == "__main__":
    main()
