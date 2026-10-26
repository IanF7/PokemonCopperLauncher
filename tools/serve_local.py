"""Pretends to be GitHub Releases on your own PC, for testing the launcher.

Usage: python tools/serve_local.py [--releases-dir DIR] [--port 8000]

Then put a launcher.ini next to Launcher.exe containing:
    release_base = http://localhost:8000

It serves the same addresses GitHub uses:
    /latest/download/<file>      -> newest release in history.txt
    /download/<tag>/<file>       -> that release
Stop it with Ctrl+C.
"""
import argparse
import os
import re
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import unquote

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)


def make_handler(releases_dir):
    class Handler(BaseHTTPRequestHandler):
        def resolve(self):
            path = unquote(self.path.split("?")[0])
            m = re.fullmatch(r"/latest/download/([^/]+)", path)
            if m:
                hist = os.path.join(releases_dir, "history.txt")
                tags = [l.strip() for l in open(hist, encoding="utf-8") if l.strip()] if os.path.exists(hist) else []
                if not tags:
                    return None
                return os.path.join(releases_dir, tags[-1], "upload", m.group(1))
            m = re.fullmatch(r"/download/([^/]+)/([^/]+)", path)
            if m:
                return os.path.join(releases_dir, m.group(1), "upload", m.group(2))
            return None

        def do_GET(self):
            target = self.resolve()
            if not target or ".." in target or not os.path.isfile(target):
                self.send_error(404)
                return
            size = os.path.getsize(target)
            start, end = 0, size - 1
            rng = self.headers.get("Range")
            m = re.fullmatch(r"bytes=(\d+)-(\d*)", rng or "")
            if m and int(m.group(1)) < size:
                start = int(m.group(1))
                if m.group(2):
                    end = min(int(m.group(2)), size - 1)
                self.send_response(206)
                self.send_header("Content-Range", "bytes %d-%d/%d" % (start, end, size))
            else:
                self.send_response(200)
            self.send_header("Content-Length", str(end - start + 1))
            self.send_header("Content-Type", "application/octet-stream")
            self.end_headers()
            with open(target, "rb") as f:
                f.seek(start)
                left = end - start + 1
                try:
                    while left > 0:
                        chunk = f.read(min(1 << 16, left))
                        if not chunk:
                            break
                        self.wfile.write(chunk)
                        left -= len(chunk)
                except (ConnectionResetError, BrokenPipeError):
                    pass

    return Handler


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--releases-dir", default=os.path.join(ROOT, "releases"))
    ap.add_argument("--port", type=int, default=8000)
    args = ap.parse_args()
    server = ThreadingHTTPServer(("127.0.0.1", args.port), make_handler(os.path.abspath(args.releases_dir)))
    print("Serving %s at http://localhost:%d  (Ctrl+C to stop)" % (args.releases_dir, args.port))
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        pass


if __name__ == "__main__":
    main()
