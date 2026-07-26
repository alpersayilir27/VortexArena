# vortex_launcher — VortexArena operatör launcher'ı

Flutter **Windows desktop** uygulaması. Tek işi var: **admin (yönetim) oyununu doğru sunucu
adresiyle başlatmak.** İşletmede operatörün açtığı tek program budur.

```
launcher'da IP yaz  ──►  VortexArena.exe --server-ip <ip> --server-port <port>
                              │
                              └─ AppBoot argümanı okur → AppSession → AdminConsole
                                 doğrudan bağlı dashboard (IP SORULMAZ)
```

## Ne yapar / ne yapmaz

| Yapar | Yapmaz |
|---|---|
| Admin exe yolunu seçip kalıcı saklar | **Sunucuyu başlatmaz** — o her zaman elle çalıştırılır |
| Sunucu IP + port'unu saklar | Maç yönetmez (mod/harita/start oyunun dashboard'unda) |
| Oyunu `--server-ip`/`--server-port` ile başlatır | Oyunla ağ üzerinden konuşmaz (protokole hiç girmez) |
| Başlatılan process'i izler (PID, çıkış kodu), durdurabilir | APK dağıtmaz |

## Dosyalar

| Dosya | Sorumluluk |
|---|---|
| `lib/main.dart` | Uygulama kabuğu + tema |
| `lib/launcher_config.dart` | Kalıcı ayarlar (`SharedPreferences`) + doğrulama + `gameArguments` |
| `lib/launcher_page.dart` | Tek ekran: Sunucu / Ayarlar / Yönetimi Başlat |
| `test/widget_test.dart` | `gameArguments` sözleşmesi, IP/port doğrulaması, ekran testleri |

> **`gameArguments` bir sözleşmedir:** ürettiği `--server-ip` / `--server-port` adları Unity
> tarafındaki `AppBoot.ArgServerIp` / `ArgServerPort` sabitleriyle birebir aynı olmalıdır.
> Testte bu birebirlik doğrulanır — argüman adını değiştirirsen İKİ tarafı birlikte değiştir.

## Geliştirme

```powershell
cd launcher
flutter pub get
flutter run -d windows     # geliştirme
flutter test               # testler
flutter analyze            # statik analiz
```

Dağıtım build'i: repo kökünden `scripts\deploy-launcher.bat` → `deploy\launcher\`.

## Ön koşullar

- Flutter SDK (stable) + Dart
- Visual Studio + **Desktop development with C++** workload
- **Windows Developer Mode AÇIK** — Flutter'ın plugin symlink'leri için şart:
  `start ms-settings:developers`. Kapalıysa `flutter pub get` şu hatayı verir:
  *"Building with plugins requires symlink support"*.

## Ayarlar nerede saklanıyor?

`SharedPreferences` → Windows'ta kullanıcı profili (launcher klasörü taşınsa bile korunur).
Anahtarlar: `adminExePath`, `serverIp`, `serverPort`.
