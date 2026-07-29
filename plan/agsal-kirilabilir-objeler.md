# Ağsal kırılabilir objeler (sunucu-otoriter obje canı)

**Hedef:** hasar alabilen ağ-dışı hiçbir şey kalmasın. Kırılabilir/yıkılabilir sahne objeleri
`NetIdentity` genişletilerek sunucu-otoriter hâle gelsin — can, kırılma ve durum sıfırlama kararı
sunucuda verilsin, istemci yalnız sunum yapsın.

## Bugünkü durum

- Yerel `Health` bileşeni **silindi**; istemcide can tutan hiçbir bileşen yok.
- `ArenaCombat.ReportRaycastHit` `false` dönünce **hiçbir şey olmuyor** — dönüş değeri yalnız
  sunum kararı içindir (gövde efekti mi, duvar efekti mi).
- Arena sahnelerinde hedef/talim objesi kalmadı; ağa bağlı olmayan tüm geometri **dekor**dur.
- Yani bugün oyunda kırılabilir obje yok; bu iş o boşluğu ağsal olarak dolduracak.

## Altyapıda hazır olan

- `NetIdentity` — sahne objesine benzersiz `sceneId` verir
  (`GameObject > VortexArena > Network Parent`), `SceneIdGuard` çakışmaları onarır.
- `NetSpawnCatalog` — dinamik obje eşlemesi için iskelet.

⚠️ İkisi de bugün yalnız **iskelet**: hiçbir mesaj `sceneId` taşımıyor, oyuncular `playerId` ile
senkronlanıyor ve `NetIdentity` gerektirmiyor.

## Sıra (kritik)

1. **ÖNCE `Docs/ArenaNet-Protokol.md`** — obje canı/kırılma mesajları, `sceneId` ile hedefleme,
   doğrulama kuralları, geç bağlanan istemciye durum aktarımı.
2. **SONRA iki uç:** `Assets/_Shared/Net/Protocol` (saf C# DTO) + `Server/`.
3. En son istemci sunumu (kırılma efekti, obje görünürlüğü).

Kod-önce gidilirse iki uçlu sapma başlar — protokol tek doğruluk kaynağıdır.

## Açık sorular (karar verilmedi)

- **Obje canı sunucuda kimde durur?** Aktif mod (`IGameMode`) mu, haritaya bağlı ayrı bir servis
  mi? Mod başına farklı kırılma davranışı istenecek mi?
- **Hedefleme:** `hit_report`'un hedefi `playerId` yerine `sceneId` olabilir mi (tek mesaj, iki
  hedef tipi), yoksa ayrı bir mesaj mı daha temiz? Ayrımı ne taşıyacak?
- **Sıfırlama:** kırılma durumu maç bitişinde / harita değişiminde sıfırlanır mı, yoksa maç boyu
  kalıcı mı? Kim tetikler?
- **Geç bağlanan istemci** sahne objelerinin mevcut durumunu nasıl alır — `welcome` içinde toplu
  liste mi, sahne yüklendikten sonra ayrı bir senkron mesajı mı?
- Kırılan obje **çarpışması** ne olur (free-roam'da oyuncu fiziksel olarak o hacimde yürüyecek)?
