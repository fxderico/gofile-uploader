#!/usr/bin/env python3
"""
gofile-upload - simple CLI to upload a file to gofile.io and get a shareable link.

Cross-platform (Windows/Linux/macOS) companion to the Windows GofileUploader GUI in
this repo. Requires Python 3 plus `requests` (and optionally `requests_toolbelt` for
a live progress bar):

    pip install requests requests_toolbelt

Usage:
    python gofile_upload.py <file> [-t TOKEN] [-f FOLDER_ID]

Examples:
    python gofile_upload.py video.mp4
    python gofile_upload.py archive.zip --token YOUR_API_TOKEN
    python gofile_upload.py build.exe -t YOUR_API_TOKEN -f 1234-5678-folder-id
"""

import argparse
import os
import sys

import requests

try:
    from requests_toolbelt.multipart.encoder import MultipartEncoder, MultipartEncoderMonitor
    HAVE_TOOLBELT = True
except ImportError:
    HAVE_TOOLBELT = False

API_BASE = "https://api.gofile.io"


def human_size(n):
    for unit in ("B", "KB", "MB", "GB", "TB"):
        if n < 1024:
            return f"{n:.1f}{unit}"
        n /= 1024
    return f"{n:.1f}PB"


def get_best_server():
    """Ask gofile which upload server to use."""
    resp = requests.get(f"{API_BASE}/servers", timeout=15)
    resp.raise_for_status()
    data = resp.json()
    if data.get("status") != "ok":
        raise RuntimeError(f"could not get server list: {data}")
    servers = data["data"]["servers"]
    if not servers:
        raise RuntimeError("gofile returned no available servers")
    return servers[0]["name"]


def print_progress(monitor):
    pct = monitor.bytes_read / monitor.len * 100 if monitor.len else 0
    bar_len = 30
    filled = int(bar_len * pct // 100)
    bar = "#" * filled + "-" * (bar_len - filled)
    sys.stdout.write(
        f"\r[{bar}] {pct:5.1f}%  {human_size(monitor.bytes_read)}/{human_size(monitor.len)}"
    )
    sys.stdout.flush()


def upload_to_gofile(file_path, token=None, folder_id=None):
    if not os.path.isfile(file_path):
        raise FileNotFoundError(f"file not found: {file_path}")

    file_size = os.path.getsize(file_path)
    file_name = os.path.basename(file_path)

    print("Requesting best gofile server...")
    server_name = get_best_server()
    upload_url = f"https://{server_name}.gofile.io/contents/uploadFile"
    print(f"Uploading '{file_name}' ({human_size(file_size)}) to {server_name}...")

    with open(file_path, "rb") as f:
        fields = {"file": (file_name, f, "application/octet-stream")}
        if folder_id:
            fields["folderId"] = folder_id
        if token:
            fields["token"] = token

        headers = {}
        if token:
            headers["Authorization"] = f"Bearer {token}"

        if HAVE_TOOLBELT:
            encoder = MultipartEncoder(fields=fields)
            monitor = MultipartEncoderMonitor(encoder, print_progress)
            headers["Content-Type"] = monitor.content_type
            response = requests.post(upload_url, data=monitor, headers=headers, timeout=None)
            print()  # newline after progress bar
        else:
            response = requests.post(upload_url, files=fields, headers=headers, timeout=None)

    response.raise_for_status()
    result = response.json()

    if result.get("status") != "ok":
        raise RuntimeError(f"upload failed: {result}")

    return result["data"]


def main():
    parser = argparse.ArgumentParser(
        prog="gofile-upload",
        description="Upload a file to gofile.io from the command line and get a shareable link.",
    )
    parser.add_argument("file", help="path to the file to upload")
    parser.add_argument(
        "-t", "--token",
        default=os.environ.get("GOFILE_TOKEN"),
        help="gofile API token, to upload to your own account (or set GOFILE_TOKEN env var)",
    )
    parser.add_argument(
        "-f", "--folder",
        default=None,
        help="destination folder ID (requires --token)",
    )
    args = parser.parse_args()

    try:
        data = upload_to_gofile(args.file, token=args.token, folder_id=args.folder)
    except Exception as e:
        print(f"\nError: {e}", file=sys.stderr)
        sys.exit(1)

    print("\nUpload complete!")
    print(f"Download page : {data.get('downloadPage')}")
    if data.get("guestToken"):
        print(f"Guest token   : {data['guestToken']} (save this to manage the file later)")
    if args.folder:
        print(f"Folder ID     : {args.folder}")


if __name__ == "__main__":
    main()
