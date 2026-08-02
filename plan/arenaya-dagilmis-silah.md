# Arenaya dağılmış silah (raf sisteminin yerine)

Silah rafı (`WeaponRackSpawner` + `RackSlot`) **kaldırıldı**; yerini alan kaynak **`WeaponCanvas`**
(tel değeri `"weaponcanvas"`). Alma yolu — çerçeveden seçme (`WeaponFrame` →
`WeaponGranter.SelectWeapon`, ≤2 m'den seçilir, klonu ele gelir, aynı mermiyle geri döner) — kodda
çalışır durumda. **Kod ve doküman işi bitti; elde yalnız arenalara silah yerleştirmek kaldı.**

Bugünkü durum: `WeaponCanvas` kullanan modlarda (**TDM · turnuva**) sahnede silah olmadığı için
oyuncunun eline silah gelmez. `ffa` ve **lobi** etkilenmez — ikisi de `RandomGrant` kullanıyor.

---

## 1. Kararlar (verildi)

| Soru | Karar |
|---|---|
| Yerleşimi kim belirler | **Arena kararı.** Harita tasarlanırken oyun yapımcısı silahları sahneye **elle** koyar; mod dağıtmaz |
| Silah tükenir mi | **Hayır.** Sahnedeki silah sınırsız kez alınır, yerinden kaybolmaz |
| Elle `WPN_*` koymak serbest mi | **Serbest.** Eski yasak kalktı |
| Kaynağın adı | `rack` **değil**, **`WeaponCanvas`** (telde `"weaponcanvas"`) |

**Yazılacak yeni bir sistem yok.** İlk iki karar `WeaponFrame` + `WeaponGranter`'ın zaten yaptığı
şeydir. Raf, silahı `loadout`'tan ÜRETTİĞİ için vardı; silahı insan koyacaksa üretici bileşene
gerek kalmıyor.

⚠️ **Yetki arenaya geçti:** `WeaponCanvas` modlarında sahnede hangi silahın duracağını artık
`ModeDefinition.loadout` DEĞİL arena belirler. "Moda silah ekleyince tüm arenalarda çıkar"
davranışı bitti — yeni silah her arenaya tek tek konur. `loadout` yalnız `RandomGrant` modlarında
(FFA, lobi) anlamını korur.

---

## 2. Elde kalan tek iş: arenalara yerleştirme

Silah sahneye `WPN_*` prefabının **ÖRNEĞİ** olarak konur (kopyalanmaz, unpack edilmez). Örnekleri
bir `WeaponCanvas` prefabında toplayıp onu her sahneye `BaseZone` gibi tek örnek olarak koymak
yerleşimi tek yerden düzeltilebilir kılar. Çerçeve görseli örnek başına
`WeaponFrame.isFrameVisible` ile açılıp kapanır.

- [ ] `Arena12x12` — kaldırılan iki rafın yerine silah yerleşimi
- [ ] `IceWorld`
- [ ] `ArenaVortexAntep`

Lobiler bu listede YOK: `Lobby12x12` · `LobbyVortexAntep` `RandomGrant` kullanıyor, sahnede iş
gerektirmiyor.

⚠️ Yerleştirmeden önce **AK47 ↔ M4A1 tanım karışıklığı** düzeltilmeli (`plan/turnuva-modu.md`):
`WPN_M4A1` bugün `WD_AK47`'ye bağlı, `WPN_AK47`'nin bağı ölü. Karışık tanımla yerleştirilen silah
yanlış istatistikle ateş eder.

## 3. Yapıldı (kayıt için — silinecek)

- `rack` → `WeaponCanvas` yeniden adlandırması: `ModeWeaponSource` · `ModeRuntime.ParseWeapons` ·
  `ModeDefinition` · `ControlMessages.weaponSource` · `WeaponGranter` · sunucuda `WeaponSource` +
  `ModeRules.ToInfo`.
- **`PROTOCOL_VERSION` artmadı.** Ayrıştırma "random" DEĞİLSE varsayılana düşer ve varsayılan bu
  değerin kendisidir → karışık sürüm iki yönde de doğru davranır (yeni sunucunun
  `"weaponcanvas"`ını eski istemci, eski sunucunun `"rack"`ini yeni istemci aynı yere çözer).
  ⚠️ Bu serbestlik **üçüncü** bir kaynak türü eklenince biter: o zaman açık bir eşleşme dalı
  gerekir, eski istemci onu tanımaz ve sürüm artar.
- "Sahneye elle `WPN_*` KOYULMAZ" yasağı kaldırıldı, yerine yerleştirme reçetesi yazıldı
  (`CLAUDE.md`, `Docs/Gelistirici/Sahne-Kurulumu.md`).
- Bugün her yere düşülmüş "sahneye silah koyan sistem YOK" uyarıları temizlendi
  (`Docs/ArenaNet-Protokol.md` §10.5/§10.7 · `Docs/Sistem-Ozeti.md` §3.9/§4 · `Kullanim-Kilavuzu`
  · `Gelistirici/API-Referansi` · `Gelistirici/Yemek-Kitabi`).

## 4. Bilinçli olarak yapılmayan

- **Enum üyesi silinmedi, yeniden adlandırıldı.** Değer `ModeDefinition` tarafından serialize
  ediliyor (sayısal indeks); üye silinseydi ya da sırası kaysaydı tüm mod asset'lerinin `weapons`
  alanı kayardı. Yeniden adlandırma indeksi değiştirmez — **mod asset'leri elden geçirilmedi ve
  geçirilmemeli.**
- **Yerleştirme bileşeni yazılmadı.** Silahı insan koyacaksa üretici bileşen ikinci bir doğruluk
  kaynağı olurdu (sahnede duran örnek + onu üreten liste).
