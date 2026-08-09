"""
updater_uploader_main.py - oyun APK'sinin yukleme ucu (tek dosya, yalniz stdlib).

Sunucuda APK'nin yasadigi klasorde calistirilir (or. D:\\WebHost\\player_apk_updater);
hedef dosya her zaman betigin KENDI klasorundeki game.apk'dir.

  POST /upload    govdedeki APK'yi game.apk olarak yazar
                  (once .tmp dosyasina iner, sonra atomik degistirilir -
                  yarim kalan yukleme mevcut APK'yi bozamaz)
  GET  /upload    "ayakta" der (saglik kontrolu)
  baska her yol   404 - betik baska endpoint yakalamaz

PORT = 8091. Indirme bu betigin isi DEGILDIR: game.apk'yi ayni klasoru kok
olarak yayinlayan IIS sitesi (8090) indirtir. Iki surec ayri portlarda
oldugu icin cakisma yoktur; IIS tarafinda .apk MIME eslemesi gerekir
(updater/README.md).

Calistirma: python updater_uploader_main.py
"""

import os
import sys
from datetime import datetime
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

PORT = 8091
UPLOAD_PATH = "/upload"
TARGET_DIR = os.path.dirname(os.path.abspath(__file__))
TARGET = os.path.join(TARGET_DIR, "game.apk")
CHUNK = 1024 * 1024


def log(msg):
    print(f"[{datetime.now():%Y-%m-%d %H:%M:%S}] {msg}", flush=True)


class Handler(BaseHTTPRequestHandler):
    # stdlib'in kendi satir basina log'u yerine tek bicimli log() kullaniliyor.
    def log_message(self, fmt, *args):
        pass

    def _reply(self, code, text):
        data = (text + "\n").encode("utf-8")
        self.send_response(code)
        self.send_header("Content-Type", "text/plain; charset=utf-8")
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)

    def do_GET(self):
        if self.path != UPLOAD_PATH:
            self._reply(404, "bilinmeyen yol")
            return
        self._reply(200, "uploader ayakta - APK'yi bu yola POST edin")

    def do_POST(self):
        client = self.client_address[0]
        if self.path != UPLOAD_PATH:
            self._reply(404, "bilinmeyen yol")
            return

        try:
            length = int(self.headers.get("Content-Length", "0"))
        except ValueError:
            length = 0
        if length <= 0:
            self._reply(411, "Content-Length gerekli")
            return

        tmp = TARGET + ".tmp"
        received = 0
        try:
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

        # APK = zip; ilk iki bayt "PK" degilse bu dosya APK degildir.
        with open(tmp, "rb") as f:
            magic = f.read(2)
        if magic != b"PK":
            self._cleanup(tmp)
            log(f"{client}: reddedildi - govde APK degil")
            self._reply(400, "govde APK degil")
            return

        # Atomik degistirme: var olan game.apk tek hamlede yenisiyle degisir.
        # IIS dosyayi o anda servis ediyorsa Windows degistirmeyi kilitleyebilir;
        # o durumda 503 doner, yukleme tekrar denenir.
        try:
            os.replace(tmp, TARGET)
        except OSError as e:
            self._cleanup(tmp)
            self._reply(503, f"game.apk degistirilemedi (dosya kilitli olabilir): {e}")
            return

        log(f"{client}: yeni APK yayinlandi ({received} bayt)")
        self._reply(200, f"tamam - {received} bayt yayinlandi")

    @staticmethod
    def _cleanup(tmp):
        try:
            os.remove(tmp)
        except OSError:
            pass


def main():
    try:
        server = ThreadingHTTPServer(("", PORT), Handler)
    except OSError as e:
        log(f"HATA: {PORT} portu dinlenemiyor: {e}")
        log("Baska bir surec (or. betigin eski bir kopyasi) ayni portu tutuyor olabilir.")
        sys.exit(1)

    log(f"dinleniyor: 0.0.0.0:{PORT}  (POST {UPLOAD_PATH})")
    log(f"hedef dosya: {TARGET}")
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        log("kapatildi (Ctrl+C)")


if __name__ == "__main__":
    main()
