# Kalibre modu · T-poz sağlamlaştırma · ölçek geri bildirimi — kalan iş

Kod (sunucu, istemci, admin arayüzü, panel prefabındaki üç mod düğmesi) ve dokümanlar yazıldı.
Kalıcı bilgi: `Docs/ArenaNet-Protokol.md` (§1 sabitler, §5.1/§5.2/§5.3, §6.9 T-poz, §10.6 kalibre
modu + zemin sağlığı, §10.8 ölçüm geri bildirimi), `Docs/Sistem-Ozeti.md` (§3.11, §4, §7) ve
`Docs/Kullanim-Kilavuzu.md` (§4.3 kalibre modu, §4.4 alan verisi temizliği, §8).

---

## 1. Doğrulama (kullanıcı koşar)

- [ ] Zemin sapması çapadan geri yüklemede: kaymış zeminli gözlükte açılış/harita geçişi sonrası
      duyuru + KAL turuncu
- [ ] T-poz (izin yok): `BODY_TRACKING` izni verilmemişken oyuncu diğer ekranlarda lobide ve maçta
      konumunu izleyen T-pozda (görünmez değil); admin satırında `G:X` + duyuru
- [ ] T-poz (maç ortası): takip kesilince (ayar anahtarı ya da arka plana atma) T-poz, kollar bükülü
      donmuyor; takip dönünce normal gövde
- [ ] Regresyon yok: sağlıklı oyuncuda bir maç boyu yedek hiç devreye girmiyor

Doğrulama listeleri Notion'da: "Doğrulama 32" (maç ortası T-poz) ve "Doğrulama 36" (gövde durumu
admin satırında, uzamsal veri dialogu, çapadan zemin sapması).

Alan verisi temizliği yalnız gözlüğün Ayarlar menüsünden yapılır (`Docs/Kullanim-Kilavuzu.md`
§4.4); `vrguardianservice` paketi bu sistem sürümünde yok, adb ile temizlik yolu kapalı.
