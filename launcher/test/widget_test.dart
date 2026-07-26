import 'dart:io';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:shared_preferences/shared_preferences.dart';
import 'package:vortex_launcher/launcher_config.dart';
import 'package:vortex_launcher/main.dart';

void main() {
  group('LauncherConfig', () {
    test('gameArguments Unity AppBoot sozlesmesine uyar', () {
      final config = LauncherConfig(
        adminExePath: r'C:\deploy\admin\VortexArena.exe',
        serverIp: '192.168.1.10',
        serverPort: 47821,
      );

      // AppBoot.ArgServerIp / ArgServerPort ile birebir ayni olmali.
      expect(config.gameArguments, [
        '--server-ip',
        '192.168.1.10',
        '--server-port',
        '47821',
      ]);
    });

    test('gameArguments IP bosluklarini kirpar', () {
      final config = LauncherConfig(serverIp: '  10.0.0.5  ');
      expect(config.gameArguments[1], '10.0.0.5');
    });

    test('IP dogrulamasi', () {
      expect(LauncherConfig.isValidIp('192.168.1.10'), isTrue);
      expect(LauncherConfig.isValidIp('127.0.0.1'), isTrue);
      expect(LauncherConfig.isValidIp('  10.0.0.5  '), isTrue);
      expect(LauncherConfig.isValidIp('192.168.1'), isFalse);
      expect(LauncherConfig.isValidIp('arena-pc'), isFalse);
      expect(LauncherConfig.isValidIp(''), isFalse);
    });

    test('port dogrulamasi', () {
      expect(LauncherConfig.isValidPort(47821), isTrue);
      expect(LauncherConfig.isValidPort(0), isFalse);
      expect(LauncherConfig.isValidPort(70000), isFalse);
    });

    test('varsayilan port protokolun WS kontrol portudur', () {
      expect(LauncherConfig.defaultPort, 47821);
    });

    test('validate: exe secilmediyse sebep doner', () {
      final config = LauncherConfig(serverIp: '127.0.0.1');
      expect(config.validate(), contains('Admin exe'));
    });

    test('validate: exe yolu varsa ama dosya yoksa yakalanir', () {
      final config = LauncherConfig(
        adminExePath: r'C:\olmayan\klasor\VortexArena.exe',
        serverIp: '127.0.0.1',
      );
      expect(config.validate(), contains('bulunamadı'));
    });

    test('validate: gecersiz IP yakalanir', () {
      // Var olan bir dosya veriyoruz ki dogrulama IP adimina kadar gelsin.
      final config = LauncherConfig(
        adminExePath: Platform.resolvedExecutable,
        serverIp: 'bozuk',
      );
      expect(config.validate(), contains('Geçersiz IP'));
    });

    test('validate: her sey yerindeyse null', () {
      final config = LauncherConfig(
        adminExePath: Platform.resolvedExecutable,
        serverIp: '192.168.1.10',
      );
      expect(config.validate(), isNull);
    });
  });

  testWidgets('launcher ekrani IP ve ayar alanlarini gosterir', (tester) async {
    SharedPreferences.setMockInitialValues({});

    await tester.pumpWidget(const VortexLauncherApp());
    await tester.pumpAndSettle();

    expect(find.text('VortexArena Launcher'), findsOneWidget);
    expect(find.text('Sunucu IP'), findsOneWidget);
    expect(find.text('Port'), findsOneWidget);
    expect(find.text('Ayarlar'), findsOneWidget);
    expect(find.text('Yönetimi Başlat'), findsOneWidget);
    expect(find.text('Seçilmedi'), findsOneWidget);
  });

  testWidgets('kayitli ayarlar alanlara yuklenir', (tester) async {
    SharedPreferences.setMockInitialValues({
      'serverIp': '192.168.1.50',
      'serverPort': 47821,
      'adminExePath': r'C:\deploy\admin\VortexArena.exe',
    });

    await tester.pumpWidget(const VortexLauncherApp());
    await tester.pumpAndSettle();

    expect(find.widgetWithText(TextField, '192.168.1.50'), findsOneWidget);
    expect(find.text(r'C:\deploy\admin\VortexArena.exe'), findsOneWidget);
  });

  testWidgets('exe secilmeden Baslat basilirsa sebep gosterilir', (tester) async {
    SharedPreferences.setMockInitialValues({'serverIp': '127.0.0.1'});

    await tester.pumpWidget(const VortexLauncherApp());
    await tester.pumpAndSettle();

    await tester.tap(find.text('Yönetimi Başlat'));
    await tester.pumpAndSettle();

    expect(find.textContaining('Admin exe'), findsWidgets);
  });
}
