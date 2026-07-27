import 'dart:async';
import 'dart:io';

import 'package:file_selector/file_selector.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import 'launcher_config.dart';

/// Tek ekranlık operatör konsolu: sunucu adresi + admin exe yolu + Başlat.
///
/// Oyunu `--server-ip/--server-port` argümanlarıyla başlatır; Unity tarafında
/// `AppBoot` bu argümanları okur; admin gözlemci Lobby'den bağlanıp sunucunun sahnesini izler.
class LauncherPage extends StatefulWidget {
  const LauncherPage({super.key});

  @override
  State<LauncherPage> createState() => _LauncherPageState();
}

class _LauncherPageState extends State<LauncherPage> {
  LauncherConfig _config = LauncherConfig();
  bool _loading = true;
  String _status = '';
  bool _statusIsError = false;

  Process? _gameProcess;
  int? _gamePid;

  late final TextEditingController _ipController;
  late final TextEditingController _portController;

  @override
  void initState() {
    super.initState();
    _ipController = TextEditingController();
    _portController = TextEditingController();
    _load();
  }

  @override
  void dispose() {
    _ipController.dispose();
    _portController.dispose();
    super.dispose();
  }

  Future<void> _load() async {
    final config = await LauncherConfig.load();
    if (!mounted) return;
    setState(() {
      _config = config;
      _ipController.text = config.serverIp;
      _portController.text = '${config.serverPort}';
      _loading = false;
    });
  }

  void _setStatus(String message, {bool isError = false}) {
    setState(() {
      _status = message;
      _statusIsError = isError;
    });
  }

  Future<void> _persist() async {
    _config.serverIp = _ipController.text.trim();
    _config.serverPort =
        int.tryParse(_portController.text.trim()) ?? LauncherConfig.defaultPort;
    await _config.save();
  }

  Future<void> _pickAdminExe() async {
    final file = await openFile(
      acceptedTypeGroups: const [
        XTypeGroup(label: 'Uygulama', extensions: ['exe']),
      ],
      confirmButtonText: 'Seç',
    );
    if (file == null) return;

    setState(() => _config.adminExePath = file.path);
    await _persist();
    _setStatus('Admin exe seçildi.');
  }

  Future<void> _launch() async {
    await _persist();

    final problem = _config.validate();
    if (problem != null) {
      _setStatus(problem, isError: true);
      return;
    }

    if (_gameProcess != null) {
      _setStatus('Oyun zaten çalışıyor (PID $_gamePid).', isError: true);
      return;
    }

    try {
      final process = await Process.start(
        _config.adminExePath,
        _config.gameArguments,
        workingDirectory: File(_config.adminExePath).parent.path,
      );

      // Pipe'ları boşalt: normal modda stdout/stderr pipe'lanır ve kimse
      // okumazsa tampon dolduğunda ÇOCUK PROCESS KİLİTLENİR. Unity kendi log
      // dosyasına yazdığı için içeriği kullanmıyoruz, sadece akıtıyoruz.
      unawaited(process.stdout.drain<void>());
      unawaited(process.stderr.drain<void>());

      setState(() {
        _gameProcess = process;
        _gamePid = process.pid;
      });
      _setStatus(
        'Başlatıldı — PID ${process.pid} · '
        '${_config.serverIp}:${_config.serverPort}',
      );

      // Oyun kapanınca butonu tekrar aç.
      process.exitCode.then((code) {
        if (!mounted) return;
        setState(() {
          _gameProcess = null;
          _gamePid = null;
        });
        _setStatus(
          code == 0
              ? 'Oyun kapandı.'
              : 'Oyun $code çıkış koduyla kapandı.',
          isError: code != 0,
        );
      });
    } on ProcessException catch (e) {
      _setStatus('Başlatılamadı: ${e.message}', isError: true);
    }
  }

  void _stop() {
    final process = _gameProcess;
    if (process == null) {
      _setStatus('Bu launcher\'dan başlatılmış oyun yok.', isError: true);
      return;
    }
    process.kill();
    _setStatus('Kapatma sinyali gönderildi.');
  }

  @override
  Widget build(BuildContext context) {
    if (_loading) {
      return const Scaffold(body: Center(child: CircularProgressIndicator()));
    }

    final theme = Theme.of(context);
    final running = _gameProcess != null;

    return Scaffold(
      appBar: AppBar(
        title: const Text('VortexArena Launcher'),
        centerTitle: false,
      ),
      body: Center(
        child: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 720),
          child: ListView(
            padding: const EdgeInsets.all(24),
            children: [
              _SectionCard(
                icon: Icons.lan_outlined,
                title: 'Sunucu',
                subtitle:
                    'Oyun bu adrese bağlanır. Adres oyuna komut satırı argümanı '
                    'olarak geçer — oyun içinde IP sorulmaz.',
                children: [
                  Row(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Expanded(
                        flex: 3,
                        child: TextField(
                          controller: _ipController,
                          decoration: const InputDecoration(
                            labelText: 'Sunucu IP',
                            hintText: '192.168.1.10',
                            prefixIcon: Icon(Icons.dns_outlined),
                          ),
                          inputFormatters: [
                            FilteringTextInputFormatter.allow(
                              RegExp(r'[0-9a-fA-F.:]'),
                            ),
                          ],
                        ),
                      ),
                      const SizedBox(width: 12),
                      Expanded(
                        child: TextField(
                          controller: _portController,
                          decoration: const InputDecoration(
                            labelText: 'Port',
                            hintText: '${LauncherConfig.defaultPort}',
                          ),
                          keyboardType: TextInputType.number,
                          inputFormatters: [
                            FilteringTextInputFormatter.digitsOnly,
                          ],
                        ),
                      ),
                    ],
                  ),
                ],
              ),
              const SizedBox(height: 16),
              _SectionCard(
                icon: Icons.settings_outlined,
                title: 'Ayarlar',
                subtitle:
                    'Admin (yönetim) oyununun Windows exe dosyası. '
                    'scripts\\deploy-admin-game.bat bunu deploy\\admin\\ altına üretir.',
                children: [
                  Row(
                    children: [
                      Expanded(
                        child: Container(
                          padding: const EdgeInsets.symmetric(
                            horizontal: 12,
                            vertical: 14,
                          ),
                          decoration: BoxDecoration(
                            border: Border.all(
                              color: theme.colorScheme.outlineVariant,
                            ),
                            borderRadius: BorderRadius.circular(8),
                          ),
                          child: Text(
                            _config.adminExePath.isEmpty
                                ? 'Seçilmedi'
                                : _config.adminExePath,
                            maxLines: 2,
                            overflow: TextOverflow.ellipsis,
                            style: theme.textTheme.bodyMedium?.copyWith(
                              fontFamily: 'Consolas',
                              color: _config.adminExePath.isEmpty
                                  ? theme.colorScheme.onSurfaceVariant
                                  : null,
                            ),
                          ),
                        ),
                      ),
                      const SizedBox(width: 12),
                      OutlinedButton.icon(
                        onPressed: _pickAdminExe,
                        icon: const Icon(Icons.folder_open),
                        label: const Text('Gözat'),
                      ),
                    ],
                  ),
                  if (_config.adminExePath.isNotEmpty && !_config.adminExeExists)
                    Padding(
                      padding: const EdgeInsets.only(top: 8),
                      child: Row(
                        children: [
                          Icon(
                            Icons.warning_amber_rounded,
                            size: 18,
                            color: theme.colorScheme.error,
                          ),
                          const SizedBox(width: 6),
                          Expanded(
                            child: Text(
                              'Dosya bulunamıyor — build silinmiş veya taşınmış.',
                              style: theme.textTheme.bodySmall?.copyWith(
                                color: theme.colorScheme.error,
                              ),
                            ),
                          ),
                        ],
                      ),
                    ),
                ],
              ),
              const SizedBox(height: 24),
              Row(
                children: [
                  Expanded(
                    child: FilledButton.icon(
                      onPressed: running ? null : _launch,
                      style: FilledButton.styleFrom(
                        padding: const EdgeInsets.symmetric(vertical: 18),
                      ),
                      icon: const Icon(Icons.play_arrow_rounded),
                      label: Text(
                        running ? 'Çalışıyor (PID $_gamePid)' : 'Yönetimi Başlat',
                        style: const TextStyle(fontSize: 16),
                      ),
                    ),
                  ),
                  if (running) ...[
                    const SizedBox(width: 12),
                    OutlinedButton.icon(
                      onPressed: _stop,
                      style: OutlinedButton.styleFrom(
                        padding: const EdgeInsets.symmetric(
                          vertical: 18,
                          horizontal: 20,
                        ),
                      ),
                      icon: const Icon(Icons.stop_rounded),
                      label: const Text('Durdur'),
                    ),
                  ],
                ],
              ),
              if (_status.isNotEmpty)
                Padding(
                  padding: const EdgeInsets.only(top: 20),
                  child: Container(
                    padding: const EdgeInsets.all(14),
                    decoration: BoxDecoration(
                      color: _statusIsError
                          ? theme.colorScheme.errorContainer
                          : theme.colorScheme.surfaceContainerHighest,
                      borderRadius: BorderRadius.circular(8),
                    ),
                    child: Row(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Icon(
                          _statusIsError
                              ? Icons.error_outline
                              : Icons.info_outline,
                          size: 20,
                          color: _statusIsError
                              ? theme.colorScheme.onErrorContainer
                              : theme.colorScheme.onSurfaceVariant,
                        ),
                        const SizedBox(width: 10),
                        Expanded(
                          child: SelectableText(
                            _status,
                            style: theme.textTheme.bodyMedium?.copyWith(
                              color: _statusIsError
                                  ? theme.colorScheme.onErrorContainer
                                  : null,
                            ),
                          ),
                        ),
                      ],
                    ),
                  ),
                ),
              const SizedBox(height: 16),
              Text(
                'Sunucu bu launcher\'dan BAŞLATILMAZ — '
                'deploy\\server\\VortexArena.Server.App.exe her zaman elle çalıştırılır.',
                style: theme.textTheme.bodySmall?.copyWith(
                  color: theme.colorScheme.onSurfaceVariant,
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

class _SectionCard extends StatelessWidget {
  const _SectionCard({
    required this.icon,
    required this.title,
    required this.subtitle,
    required this.children,
  });

  final IconData icon;
  final String title;
  final String subtitle;
  final List<Widget> children;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Card(
      elevation: 0,
      color: theme.colorScheme.surfaceContainerLow,
      child: Padding(
        padding: const EdgeInsets.all(20),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              children: [
                Icon(icon, size: 20, color: theme.colorScheme.primary),
                const SizedBox(width: 8),
                Text(title, style: theme.textTheme.titleMedium),
              ],
            ),
            const SizedBox(height: 4),
            Text(
              subtitle,
              style: theme.textTheme.bodySmall?.copyWith(
                color: theme.colorScheme.onSurfaceVariant,
              ),
            ),
            const SizedBox(height: 16),
            ...children,
          ],
        ),
      ),
    );
  }
}
