import 'package:flutter/material.dart';

import 'launcher_page.dart';

void main() {
  runApp(const VortexLauncherApp());
}

/// VortexArena operatör launcher'ı (Windows desktop).
///
/// Görevi tek şey: admin (yönetim) oyununu doğru sunucu adresiyle başlatmak.
/// Sunucunun kendisi bu uygulamadan başlatılmaz — o her zaman elle çalıştırılır.
class VortexLauncherApp extends StatelessWidget {
  const VortexLauncherApp({super.key});

  @override
  Widget build(BuildContext context) {
    return MaterialApp(
      title: 'VortexArena Launcher',
      debugShowCheckedModeBanner: false,
      themeMode: ThemeMode.dark,
      theme: _theme(Brightness.light),
      darkTheme: _theme(Brightness.dark),
      home: const LauncherPage(),
    );
  }

  ThemeData _theme(Brightness brightness) => ThemeData(
        useMaterial3: true,
        colorScheme: ColorScheme.fromSeed(
          seedColor: const Color(0xFF5B6CFF),
          brightness: brightness,
        ),
        inputDecorationTheme: const InputDecorationTheme(
          border: OutlineInputBorder(),
        ),
      );
}
