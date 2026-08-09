"""
updater_uploader_main.py - oyun APK'sinin yayin ucu (tek dosya, yalniz stdlib).

Sunucuda APK'nin yasayacagi klasorde calistirilir (or. D:\\WebHost\\player_apk_updater);
hedef dosya her zaman betigin KENDI klasorundeki game.apk'dir.

  POST /upload    govdedeki APK'yi game.apk olarak yazar
                  (once .tmp dosyasina iner, sonra atomik degistirilir -
                  yarim kalan yukleme mevcut APK'yi bozamaz)
  GET  /game.apk  guncel APK'yi indirir (gozlukteki Vortex Updater'in adresi)
  GET  /upload    ayni dosyayi indirir
  baska her yol   404 - betik baska endpoint yakalamaz

PORT = 8090. Ayni portu iki surec dinleyemez: bu betik ayaktayken IIS'te ayni
porta bagli site DURDURULMUS olmali. Betik indirmeyi de yaptigi icin IIS'e
gerek kalmaz (.apk MIME eslemesi derdi de yoktur).

Calistirma: python updater_uploader_main.py
"""

import os
import sys
from datetime import datetime
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

PORT = 8090
UPLOAD_PATH = "/upload"
DOWNLOAD_PATHS = ("/game.apk", "/upload")
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

    # --- Indirme ---------------------------------------------------------

    def do_GET(self):
        if self.path not in DOWNLOAD_PATHS:
            self._reply(404, "bilinmeyen yol")
            return
        if not os.path.isfile(TARGET):
            self._reply(404, "henuz apk yuklenmedi")
            return
        size = os.path.getsize(TARGET)
        try:
            with open(TARGET, "rb") as f:
                self.send_response(200)
                self.send_header("Content-Type", "application/vnd.android.package-archive")
                self.send_header("Content-Length", str(size))
                self.end_headers()
                while True:
                    chunk = f.read(CHUNK)
                    if not chunk:
                        break
                    self.wfile.write(chunk)
            log(f"{self.client_address[0]}: indirdi ({size} bayt)")
        except (ConnectionError, OSError):
            # istemci yarida birakti - sunucu icin sorun degil
            log(f"{self.client_address[0]}: indirme yarida kaldi")

    # --- Yukleme ---------------------------------------------------------

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

        # Atomik degistirme: var olan game.apk tek hamlede yenisiyle degisir,
        # o an indirme yapan istemci ya eskisini ya yenisini butun olarak alir.
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
        log("Buyuk olasilikla ayni porta bagli IIS sitesi (ya da eski bir kopya) calisiyor.")
        log("IIS Manager'da o siteyi durdurun ve betigi yeniden baslatin.")
        sys.exit(1)

    log(f"dinleniyor: 0.0.0.0:{PORT}  (POST {UPLOAD_PATH} = yayinla, GET /game.apk = indir)")
    log(f"hedef dosya: {TARGET}")
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        log("kapatildi (Ctrl+C)")


if __name__ == "__main__":
    main()
