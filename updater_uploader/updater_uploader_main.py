"""updater_uploader_main.py - publish endpoint for the game APK (single file, stdlib only).

Runs on the server, in the folder the APKs live in (e.g. D:\\WebHost\\player_apk_updater).
Versioned builds are written into the "game_versions" sub folder as game_v<N>.apk, so
several versions stay published side by side.

  POST /upload?v=132  writes the body to game_versions/game_v132.apk
                      (lands in a .tmp first, then atomic replace - a half
                      finished upload cannot corrupt an existing APK)
  GET  /versions      JSON list of published versions, newest first
  GET  /upload        says "up" (health check)
  any other path      404 - this script grabs no other endpoint

PORT = 8091. Downloading is NOT this script's job: the IIS site (8090) serving the same
folder as its root hands out game_versions/game_v<N>.apk. The two processes use separate
ports so they do not clash; IIS needs the .apk MIME mapping (updater/README.md).

The legacy game.apk in the root folder is left untouched - old updaters keep downloading
it - but nothing writes there any more.

Run: python updater_uploader_main.py
"""

import json
import os
import re
import sys
from datetime import datetime
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import parse_qs, urlsplit

PORT = 8091
UPLOAD_PATH = "/upload"
VERSIONS_PATH = "/versions"
BASE_DIR = os.path.dirname(os.path.abspath(__file__))
TARGET_DIR = os.path.join(BASE_DIR, "game_versions")
CHUNK = 1024 * 1024
MAX_VERSION = 2000000000
APK_RE = re.compile(r"^game_v([0-9]+)\.apk$")


def log(msg):
    print(f"[{datetime.now():%Y-%m-%d %H:%M:%S}] {msg}", flush=True)


def apk_path(version):
    return os.path.join(TARGET_DIR, f"game_v{version}.apk")


class Handler(BaseHTTPRequestHandler):
    # Single log() format instead of stdlib's per-request line.
    def log_message(self, fmt, *args):
        pass

    def _reply(self, code, text):
        data = (text + "\n").encode("utf-8")
        self.send_response(code)
        self.send_header("Content-Type", "text/plain; charset=utf-8")
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)

    def _reply_json(self, code, payload):
        data = (json.dumps(payload, ensure_ascii=False) + "\n").encode("utf-8")
        self.send_response(code)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)

    # The path carries a query string now, so compare on the path part only.
    def _route(self):
        return urlsplit(self.path).path

    def _version_arg(self):
        raw = parse_qs(urlsplit(self.path).query).get("v", [""])[0].strip()
        if not raw.isdigit():
            return None
        value = int(raw)
        if value <= 0 or value > MAX_VERSION:
            return None
        return value

    def do_GET(self):
        route = self._route()
        if route == VERSIONS_PATH:
            self._reply_json(200, self._collect_versions())
            return
        if route == UPLOAD_PATH:
            self._reply(200, "uploader ayakta - APK'yi bu yola POST edin: /upload?v=<surum>")
            return
        self._reply(404, "bilinmeyen yol")

    @staticmethod
    def _collect_versions():
        items = []
        try:
            names = os.listdir(TARGET_DIR)
        except OSError:
            names = []

        for name in names:
            m = APK_RE.match(name)
            if not m:
                continue
            full = os.path.join(TARGET_DIR, name)
            try:
                st = os.stat(full)
            except OSError:
                continue
            items.append({
                "version": int(m.group(1)),
                "file": name,
                "size": st.st_size,
                "modified": f"{datetime.fromtimestamp(st.st_mtime):%Y-%m-%d %H:%M:%S}",
            })

        items.sort(key=lambda i: i["version"], reverse=True)
        return {"count": len(items), "versions": items}

    def do_POST(self):
        client = self.client_address[0]
        if self._route() != UPLOAD_PATH:
            self._reply(404, "bilinmeyen yol")
            return

        version = self._version_arg()
        if version is None:
            log(f"{client}: reddedildi - gecersiz veya eksik surum")
            self._reply(400, "surum gerekli: /upload?v=<pozitif tam sayi>, ornek /upload?v=132")
            return

        try:
            length = int(self.headers.get("Content-Length", "0"))
        except ValueError:
            length = 0
        if length <= 0:
            self._reply(411, "Content-Length gerekli")
            return

        target = apk_path(version)
        tmp = target + ".tmp"
        received = 0
        try:
            os.makedirs(TARGET_DIR, exist_ok=True)
            with open(tmp, "wb") as out:
                while received < length:
                    chunk = self.rfile.read(min(CHUNK, length - received))
                    if not chunk:
                        break
                    out.write(chunk)
                    received += len(chunk)
        except OSError as e:
            self._cleanup(tmp)
            self._reply(500, f"diske yazilamadi: {e}")
            return

        if received != length:
            self._cleanup(tmp)
            log(f"{client}: yukleme yarida kaldi ({received}/{length} bayt)")
            self._reply(400, "yukleme eksik geldi")
            return

        # APK = zip; if the first two bytes are not "PK" this is not an APK.
        with open(tmp, "rb") as f:
            magic = f.read(2)
        if magic != b"PK":
            self._cleanup(tmp)
            log(f"{client}: reddedildi - govde APK degil")
            self._reply(400, "govde APK degil")
            return

        # Atomic replace: an existing file of the same version is swapped in one move.
        # If IIS is serving it right then Windows can block the replace; 503 tells the
        # uploader to retry.
        try:
            os.replace(tmp, target)
        except OSError as e:
            self._cleanup(tmp)
            self._reply(503, f"game_v{version}.apk degistirilemedi (dosya kilitli olabilir): {e}")
            return

        log(f"{client}: surum {version} yayinlandi ({received} bayt)")
        self._reply(200, f"tamam - surum {version}, {received} bayt yayinlandi")

    @staticmethod
    def _cleanup(tmp):
        try:
            os.remove(tmp)
        except OSError:
            pass


def main():
    try:
        os.makedirs(TARGET_DIR, exist_ok=True)
    except OSError as e:
        log(f"HATA: surum klasoru olusturulamiyor: {TARGET_DIR}: {e}")
        sys.exit(1)

    try:
        server = ThreadingHTTPServer(("", PORT), Handler)
    except OSError as e:
        log(f"HATA: {PORT} portu dinlenemiyor: {e}")
        log("Baska bir surec (or. betigin eski bir kopyasi) ayni portu tutuyor olabilir.")
        sys.exit(1)

    log(f"dinleniyor: 0.0.0.0:{PORT}")
    log(f"  POST {UPLOAD_PATH}?v=<surum>  -> game_versions/game_v<surum>.apk")
    log(f"  GET  {VERSIONS_PATH}          -> yayindaki surumlerin JSON listesi")
    log(f"hedef klasor: {TARGET_DIR}")
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        log("kapatildi (Ctrl+C)")


if __name__ == "__main__":
    main()
