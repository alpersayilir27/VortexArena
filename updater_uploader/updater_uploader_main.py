"""updater_uploader_main.py - publish endpoint for the game APK, the admin build and the server (single file, stdlib only).

Runs on the server, in the folder the artifacts live in (e.g. D:\\WebHost\\player_apk_updater).
Versioned builds are written into a per-kind sub folder, so several versions stay published
side by side.

  POST /upload?v=132        writes the body to game_versions/game_v132.apk
  GET  /versions            JSON list of published APK versions, newest first
  GET  /upload              says "up" (health check)

  POST /admin/upload?v=16   writes the body to admin_versions/admin_v16.zip
  GET  /admin/versions      JSON list of published admin versions, newest first
  GET  /admin/upload        says "up" (health check)

  POST /server/upload?v=16  writes the body to server_versions/server_v16.zip
  GET  /server/versions     JSON list of published server versions, newest first
  GET  /server/upload       says "up" (health check)

  any other path            404 - this script grabs no other endpoint

Every upload lands in a .tmp first and is then atomically replaced - a half finished
upload cannot corrupt an existing artifact.

PORT = 8091. Downloading is NOT this script's job: the IIS site (8090) serving the same
folder as its root hands out game_versions/game_v<N>.apk, admin_versions/admin_v<N>.zip
and server_versions/server_v<N>.zip.
The two processes use separate ports so they do not clash; IIS needs the .apk MIME mapping
(.zip it already knows - updater/README.md).

The legacy game.apk in the root folder is left untouched - old updaters keep downloading
it - but nothing writes there any more.

Run: python updater_uploader_main.py
"""

import json
import os
import re
import sys
import zipfile
from datetime import datetime
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import parse_qs, urlsplit

PORT = 8091
BASE_DIR = os.path.dirname(os.path.abspath(__file__))
CHUNK = 1024 * 1024
MAX_VERSION = 2000000000


def log(msg):
    print(f"[{datetime.now():%Y-%m-%d %H:%M:%S}] {msg}", flush=True)


def check_admin_zip(path):
    """Extra validation for the admin archive; returns an error text or None.

    The launcher extracts the archive straight into admin_versions/admin_v<N>/ and
    starts VortexArena.exe from the top: an archive that wraps the build in a folder
    would install silently broken.
    """
    try:
        with zipfile.ZipFile(path) as archive:
            names = archive.namelist()
    except (zipfile.BadZipFile, OSError) as e:
        return f"zip arsivi okunamadi: {e}"

    if "VortexArena.exe" not in names:
        return "arsiv kokunde VortexArena.exe yok - build klasorunun ICERIGI zip'lenmeli"
    return None


def check_server_zip(path):
    """Extra validation for the server archive; returns an error text or None.

    The launcher extracts it straight into server_versions/server_v<N>/ and starts the exe
    from the top. config/maps.json ships WITH the version (the server reads it from the
    version folder, not from the venue data dir): without it every match would start with
    map validation off.
    """
    try:
        with zipfile.ZipFile(path) as archive:
            names = archive.namelist()
    except (zipfile.BadZipFile, OSError) as e:
        return f"zip arsivi okunamadi: {e}"

    if "VortexArena.Server.App.exe" not in names:
        return "arsiv kokunde VortexArena.Server.App.exe yok - publish klasorunun ICERIGI zip'lenmeli"
    if "config/maps.json" not in names:
        return "arsivde config/maps.json yok - maps.json surumle birlikte dagilir"
    return None


# One row per artifact kind. Routes, folder, naming and the extra validator live
# together so a new kind needs no new request handling code.
KINDS = (
    {
        "upload": "/upload",
        "versions": "/versions",
        "dir": os.path.join(BASE_DIR, "game_versions"),
        "prefix": "game_v",
        "ext": ".apk",
        "regex": re.compile(r"^game_v([0-9]+)\.apk$"),
        "health": "uploader ayakta - APK'yi bu yola POST edin: /upload?v=<surum>",
        "version_hint": "surum gerekli: /upload?v=<pozitif tam sayi>, ornek /upload?v=132",
        # APK = zip; if the first two bytes are not "PK" this is not an APK.
        "magic_error": "govde APK degil",
        "validate": None,
    },
    {
        "upload": "/admin/upload",
        "versions": "/admin/versions",
        "dir": os.path.join(BASE_DIR, "admin_versions"),
        "prefix": "admin_v",
        "ext": ".zip",
        "regex": re.compile(r"^admin_v([0-9]+)\.zip$"),
        "health": "uploader ayakta - admin zip'ini bu yola POST edin: /admin/upload?v=<surum>",
        "version_hint": "surum gerekli: /admin/upload?v=<pozitif tam sayi>, ornek /admin/upload?v=16",
        "magic_error": "govde zip degil",
        "validate": check_admin_zip,
    },
    {
        "upload": "/server/upload",
        "versions": "/server/versions",
        "dir": os.path.join(BASE_DIR, "server_versions"),
        "prefix": "server_v",
        "ext": ".zip",
        "regex": re.compile(r"^server_v([0-9]+)\.zip$"),
        "health": "uploader ayakta - sunucu zip'ini bu yola POST edin: /server/upload?v=<surum>",
        "version_hint": "surum gerekli: /server/upload?v=<pozitif tam sayi>, ornek /server/upload?v=16",
        "magic_error": "govde zip degil",
        "validate": check_server_zip,
    },
)

UPLOAD_ROUTES = {kind["upload"]: kind for kind in KINDS}
VERSIONS_ROUTES = {kind["versions"]: kind for kind in KINDS}


def artifact_path(kind, version):
    return os.path.join(kind["dir"], f"{kind['prefix']}{version}{kind['ext']}")


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
        kind = VERSIONS_ROUTES.get(route)
        if kind is not None:
            self._reply_json(200, self._collect_versions(kind))
            return
        kind = UPLOAD_ROUTES.get(route)
        if kind is not None:
            self._reply(200, kind["health"])
            return
        self._reply(404, "bilinmeyen yol")

    @staticmethod
    def _collect_versions(kind):
        items = []
        try:
            names = os.listdir(kind["dir"])
        except OSError:
            names = []

        for name in names:
            m = kind["regex"].match(name)
            if not m:
                continue
            full = os.path.join(kind["dir"], name)
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
        kind = UPLOAD_ROUTES.get(self._route())
        if kind is None:
            self._reply(404, "bilinmeyen yol")
            return

        version = self._version_arg()
        if version is None:
            log(f"{client}: reddedildi - gecersiz veya eksik surum")
            self._reply(400, kind["version_hint"])
            return

        try:
            length = int(self.headers.get("Content-Length", "0"))
        except ValueError:
            length = 0
        if length <= 0:
            self._reply(411, "Content-Length gerekli")
            return

        target = artifact_path(kind, version)
        name = os.path.basename(target)
        tmp = target + ".tmp"
        received = 0
        try:
            os.makedirs(kind["dir"], exist_ok=True)
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

        # Every kind is a zip container; wrong first bytes mean the body is something else.
        with open(tmp, "rb") as f:
            magic = f.read(2)
        if magic != b"PK":
            self._cleanup(tmp)
            log(f"{client}: reddedildi - {kind['magic_error']}")
            self._reply(400, kind["magic_error"])
            return

        validate = kind["validate"]
        if validate is not None:
            problem = validate(tmp)
            if problem is not None:
                self._cleanup(tmp)
                log(f"{client}: reddedildi - {problem}")
                self._reply(400, problem)
                return

        # Atomic replace: an existing file of the same version is swapped in one move.
        # If IIS is serving it right then Windows can block the replace; 503 tells the
        # uploader to retry.
        try:
            os.replace(tmp, target)
        except OSError as e:
            self._cleanup(tmp)
            self._reply(503, f"{name} degistirilemedi (dosya kilitli olabilir): {e}")
            return

        log(f"{client}: surum {version} yayinlandi ({received} bayt) -> {name}")
        self._reply(200, f"tamam - surum {version}, {received} bayt yayinlandi")

    @staticmethod
    def _cleanup(tmp):
        try:
            os.remove(tmp)
        except OSError:
            pass


def main():
    for kind in KINDS:
        try:
            os.makedirs(kind["dir"], exist_ok=True)
        except OSError as e:
            log(f"HATA: surum klasoru olusturulamiyor: {kind['dir']}: {e}")
            sys.exit(1)

    try:
        server = ThreadingHTTPServer(("", PORT), Handler)
    except OSError as e:
        log(f"HATA: {PORT} portu dinlenemiyor: {e}")
        log("Baska bir surec (or. betigin eski bir kopyasi) ayni portu tutuyor olabilir.")
        sys.exit(1)

    log(f"dinleniyor: 0.0.0.0:{PORT}")
    for kind in KINDS:
        folder = os.path.basename(kind["dir"])
        log(f"  POST {kind['upload']}?v=<surum>  -> {folder}/{kind['prefix']}<surum>{kind['ext']}")
        log(f"  GET  {kind['versions']}          -> yayindaki surumlerin JSON listesi")
        log(f"hedef klasor: {kind['dir']}")
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        log("kapatildi (Ctrl+C)")


if __name__ == "__main__":
    main()
