# Kalibre modu · T-poz sağlamlaştırma · ölçek geri bildirimi — kalan iş

Kod (protokol v13, sunucu, istemci, admin arayüzü, panel prefabındaki üç mod düğmesi) ve
dokümanlar yazıldı. Kalıcı bilgi: `Docs/ArenaNet-Protokol.md` (§1 sabitler, §5.1/§5.2/§5.3,
§6.9 T-poz, §10.6 kalibre modu + zemin sağlığı, §10.8 ölçüm geri bildirimi),
`Docs/Sistem-Ozeti.md` (§3.11, §4, §7) ve `Docs/Kullanim-Kilavuzu.md` (§4.3 kalibre modu,
§4.4 alan verisi temizliği, §8).

---

## 1. Doğrulama (kullanıcı koşar)

- [ ] Unity derlemesi + `dotnet build` (Server) temiz
- [ ] Panelde üç düğme hizalı; ÇAPA BULUTU pasif/soluk ve tıklanamıyor; panel 1080p'de taşmıyor
      (alt kenar: BAĞLANTI bölümü ekranda)
- [ ] Sunucu açılışında mod `two_anchor`; panelde 2 ÇAPA vurgulu; ESKİ KALİBRE'ye basınca duyuru
      düşüyor ve ikinci admin ekranında da vurgu değişiyor
- [ ] `two_anchor`: başlık uygulamayı kapatıp açınca eski kalibreyi YÜKLEMİYOR (elle kalibre
      istiyor); oturum içinde harita değişince kalibre korunuyor
- [ ] `saved_anchor`: uygulama yeniden açılınca kalibre kayıttan geri geliyor
- [ ] Mod değişimi o an bağlı başlığa işlemiyor (yeniden bağlanınca işliyor) — panel bunu yazıyor
- [ ] Admin `KAL` sıfırlaması sonrası ne disk ne bellek yolundan geri yükleme oluyor
- [ ] Zemin sapması: kalibre kasıtlı yükseltide alınınca (uç masada) duyuru + satırda turuncu
      `KAL ?`; doğru zeminde alınca temizleniyor
- [ ] Gövde takibi kapalıyken ÖLÇ → satırda `ÖLÇÜLEMEDİ`, duyuruda "gövde pozu yok"; eğilerek
      ölçümde "oyuncu hareketli/eğilmiş"; dik tekrar ölçümde çarpan dönüyor ve hata siliniyor
- [ ] T-poz (ilk açılış): body tracking izinsizken oyuncu diğer ekranlarda konumunu izleyen
      T-pozda (görünmez değil)
- [ ] T-poz (oyun içi): kök saçmalayınca (çok katlı senaryo) uzak taraf ışınlanma yerine T-poz
      görüyor; veri düzelince ~1 sn içinde normale dönüyor; konsolda giriş/çıkış logları tek atışlık
- [ ] Ölçeklenmiş/T-pozlu oyuncuda regresyon yok: normal oturumda yedek hiç devreye girmiyor

⚠️ **Protokol v13** — tüm başlıklara yeni APK + admin build + sunucu dağıtımı gerekir.

## 2. adb alan-verisi temizliği denemesi (saha)

- [ ] Tek başlıkta dene: `adb shell pm clear com.oculus.vrguardianservice` — Ayarlar'daki
      "fiziksel alan verilerini temizle" ile aynı etkiyi veriyor mu (temiz açılış + sıfırdan
      sorunsuz kalibre)?
- [ ] Veriyorsa: `scripts/` altına bağlı tüm başlıklarda temizliği koşan bakım betiği (adb Wi-Fi
      dahil) + `Docs/Kullanim-Kilavuzu.md` §4.4'e betik yolu. Vermiyorsa bu bölüm silinir,
      prosedür yalnız Ayarlar menüsünden kalır.
