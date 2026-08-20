# Admin: oyuncuyu canlandır düğmesi

Operatörün ölü oyuncuyu elle canlandırması (`revive_player`). Kuralın kendisi — hangi kapının
geçildiği, hangisinin uygulandığı — `Docs/ArenaNet-Protokol.md` §5.2 ve §10.4'tedir.

## Kalan iş

- [ ] **Toplu canlandırma düğmesi** admin HUD'ında (`AdminCommands.RevivePlayer(0)` — "tüm ölüleri
      canlandır"). Komut ve sunucu tarafı hazır, eksik olan yalnız düğme: `AdminHud.prefab` +
      `AdminHud.cs`. Satır düğmesiyle aynı komutu kullanır, ek protokol yüzeyi yoktur.
- [ ] Satır düğmesinin dar kartta okunabilirliği: eylem şeridi altı sütuna bölündüğü için slot
      dar kalıyor; sığmayan etiket kısaltılır (prefabta tek alan).

## Doğrulama listesi

- [ ] TDM: tabanına girmeyen ölü oyuncu düğmeyle canlanıyor, ölüm ekranı kapanıyor, ateş edebiliyor
- [ ] FFA: sabit durma sayacı işlerken basılan düğme oyuncuyu anında canlandırıyor
- [ ] Turnuva: tur ortasında ölü oyuncu düğmeyle canlanıyor ve tur bitiş koşulu buna göre yeniden
      değerlendiriliyor
- [ ] Kalibresiz ölü oyuncuda düğme **gri** (istemci kapısı) — sunucuya komut hiç gitmiyor
- [ ] Engelin içindeki ölü oyuncuda düğme basılabiliyor ama **canlandırmıyor**; sunucu konsolunda
      gerekçe satırı var
- [ ] Faz `paused`/`finished` iken komut etkisiz, konsolda faz gerekçesi yazıyor
- [ ] `playerId:0` toplu komut yalnız ölüleri canlandırıyor, canlıların canını sıfırlamıyor ve
      admin satırları için konsola satır basmıyor
- [ ] Oyuncu rolündeki bir istemci elle `revive_player` yollarsa sunucu reddediyor (`RequireAdmin`)
- [ ] Canlanan oyuncunun `deaths` sayacı korunuyor (canlandırma skor defterine dokunmaz)
