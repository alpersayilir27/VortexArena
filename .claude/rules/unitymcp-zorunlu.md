# Kural: Önce UnityMCP kapısı — kapalıysa Unity verisine dayanan iş YAPILMAZ

Bu repoda (`VortexArena`) her oturumun ilk adımı **`UnityMCP` MCP sunucusu ayakta mı** kontrolüdür.
Kapı kapalıyken üretilen "Unity cevabı" YAML tahminine ya da bayat bilgiye dayanır ve sessizce
yanlış olur — bu yüzden kapı diğer bütün kuralların ÜSTÜNDEDİR.

## 1. Kapı kontrolü

Kullanıcının isteği ne olursa olsun, önce **okuma-yazma yapmayan hafif bir çağrı**:
`mcp__UnityMCP__manage_editor` → `action: "telemetry_status"`. Şema yüklü değilse
`ToolSearch("select:mcp__UnityMCP__manage_editor")` ile yüklenir.

**Başarılıysa** iş normal akışında sürer ([[unity-mcp-first]] basamakları geçerlidir). Bitti.

## 2. Kapı düştüyse: önce SEBEBİ ayır

Çağrı hata/timeout verdiyse "Unity kapalı" diye varsayma — **süreçlere bak**
(bunun MCP karşılığı yok, [[unity-mcp-first]] gereği shell meşru; komut Windows komutudur →
[[windows-shell]]):

```powershell
Get-Process Unity, unity-mcp-server, relay_win -ErrorAction SilentlyContinue | Select-Object Name, Id
```

| Bulgu | Anlamı | Ne yapılır |
|---|---|---|
| Hiç `Unity.exe` yok | Editör gerçekten kapalı | **3. adıma** geç (işe göre karar) |
| `Unity.exe` var ama MCP düşüyor | Köprü arızası (8080 ayakta değil / başka örneğe pinlenmiş) | Sessizce esnetme: **kullanıcıya söyle**, `set_active_instance`'ı ve köprüyü kontrol et |

## 3. Editör kapalıysa kararı AJAN verir: bu iş Unity verisine dokunuyor mu?

- **Dokunuyorsa dur.** (Prefab/sahne/asset içeriği, bileşen alanı, hiyerarşi, materyal/shader,
  editor tool'u çalıştırma, konsol logu, "şu prefabda şu alan ne", sahnede ne var…)
  Tek çıktı: **"MCP'yi çalıştır."** + neyin engellendiği tek satır. Tahmin yürütme,
  prefab/sahne YAML'ı **grep'leyerek** cevap uydurma.
- **Dokunmuyorsa devam et, sorma.** (git · `Docs/` · `plan/` · `.claude/` · `Server/` ve
  `launcher/` C# kaynağı · `scripts/` · `updater/` · saf tasarım/mimari sorusu · repo içi
  `Read`/`Grep`/`Edit` ile hallolan kod işi.) Cevabın başına **tek satır** not düşülür:
  *"UnityMCP kapalı — bu iş Unity verisine dokunmuyor, devam ettim."*
- **Sınırda kalıyorsa** (işin bir kısmı Unity'ye dokunuyor): dokunmayan kısmı bitir, dokunan kısmı
  **yapma** ve neyin MCP beklediğini açıkça yaz.

## 4. "Zorla devam et" — kullanıcı kapıyı açıkça geçer

Kullanıcı *"zorla devam et"*, *"Unity kapalı, yine de yap"* gibi **açık** bir talimat verdiyse kural
düşer ve Unity verisine dokunan iş de yapılır. Koşulları:

- Yapılan her Unity varsayımı **açıkça yazılır** ("şu prefabda şu alanın adının `x` olduğunu
  varsaydım") — teyit edilmiş gibi sunulmaz.
- İş **"Unity açılınca doğrulanacaklar"** listesiyle biter.
- İzin **o iş içindir**, oturum boyunca sürmez.
- ⚠️ Yasak yine yasaktır: prefab/sahne/asset YAML'ı okuyarak "teyit ettim" denmez
  ([[unity-mcp-first]]), derleme/build/test yine ajana kapalıdır
  ([[derleme-kullaniciya-aittir]]).

- Sunucu kaydı **proje scope'undadır**, `.mcp.json` içinde; HTTP transport olduğu için köprü
  `http://127.0.0.1:8080/mcp` üzerinde ayakta ve editör açık olmalıdır → [[unity-cli]] MCP tablosu.
- Birden çok Unity örneği açıksa hedefi `set_active_instance` ile sabitle.
