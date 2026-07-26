import 'dart:io';

import 'package:shared_preferences/shared_preferences.dart';

/// Launcher'ın kalıcı ayarları (admin exe yolu + sunucu adresi).
///
/// `SharedPreferences` Windows'ta bunları kullanıcı profiline yazar, yani
/// launcher klasörü taşınsa bile ayarlar korunur.
class LauncherConfig {
  LauncherConfig({
    this.adminExePath = '',
    this.serverIp = '127.0.0.1',
    this.serverPort = defaultPort,
  });

  /// `ArenaProtocol.CONTROL_PORT` — protokolün WS kontrol portu.
  static const int defaultPort = 47821;

  static const _keyExe = 'adminExePath';
  static const _keyIp = 'serverIp';
  static const _keyPort = 'serverPort';

  String adminExePath;
  String serverIp;
  int serverPort;

  static Future<LauncherConfig> load() async {
    final prefs = await SharedPreferences.getInstance();
    return LauncherConfig(
      adminExePath: prefs.getString(_keyExe) ?? '',
      serverIp: prefs.getString(_keyIp) ?? '127.0.0.1',
      serverPort: prefs.getInt(_keyPort) ?? defaultPort,
    );
  }

  Future<void> save() async {
    final prefs = await SharedPreferences.getInstance();
    await prefs.setString(_keyExe, adminExePath);
    await prefs.setString(_keyIp, serverIp);
    await prefs.setInt(_keyPort, serverPort);
  }

  /// Seçili exe gerçekten var mı? (Build silinmiş/taşınmış olabilir.)
  bool get adminExeExists =>
      adminExePath.isNotEmpty && File(adminExePath).existsSync();

  /// IPv4/IPv6 olarak ayrıştırılabiliyor mu?
  static bool isValidIp(String value) =>
      InternetAddress.tryParse(value.trim()) != null;

  static bool isValidPort(int value) => value > 0 && value <= 65535;

  /// Oyuna geçilecek komut satırı argümanları — `AppBoot` bunları okur.
  List<String> get gameArguments => [
        '--server-ip',
        serverIp.trim(),
        '--server-port',
        '$serverPort',
      ];

  /// Doğrulama: hazır değilse kullanıcıya gösterilecek sebep, hazırsa null.
  String? validate() {
    if (adminExePath.isEmpty) {
      return 'Admin exe seçilmedi. Ayarlar bölümünden deploy\\admin\\VortexArena.exe dosyasını seçin.';
    }
    if (!adminExeExists) {
      return 'Admin exe bulunamadı:\n$adminExePath';
    }
    if (!isValidIp(serverIp)) {
      return 'Geçersiz IP adresi: "$serverIp"';
    }
    if (!isValidPort(serverPort)) {
      return 'Geçersiz port: $serverPort';
    }
    return null;
  }
}
