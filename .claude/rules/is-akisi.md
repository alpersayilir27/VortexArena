# Kural: İş akışı — arama, bağlam maliyeti, devretme, doğrulama

> ⛔ **Önce kapı:** `UnityMCP` ayakta değilse Unity verisine dayanan iş yapılmaz → [[unity-erisim]]

## 1. Arama: hedefli Grep, geniş keşif alt-ajana

Tam simge / string / dosya adı biliniyorsa **doğrudan `Grep` / `Glob` / `Read`** — en hızlı ve en
kesin yol; `.meta`/`.asset`/JSON gibi üretilmiş dosyalarda da tek doğru araç budur.

- **Alt-ajana ver:** "bu nasıl çalışıyor / X nerede / bunu değiştirirsem nereler etkilenir",
  çapraz katman izleri (`_Shared/Net` ↔ `Protocol` ↔ `Server/`), refactor öncesi etki alanı, adını
  bilmediğin bir davranışın kaynağı — yani **çok dosya taranacak ama ana bağlama yalnız sonuç
  lazımsa**. Salt okuma keşfi için `subagent_type: "Explore"`, model elle `"opus"` (§3).
- ⚠️ Ajanın döndürdüğü `dosya:satır` referansları kullanıcıya verilmeden önce `Read`/`Grep` ile
  teyit edilir — ajan alıntıyı özetlerken kaydırabilir.
- Tek dosya okunacaksa ajan açma; doğrudan `Read`.

## 2. Bağlam maliyeti — pahalı okuma desenleri

- ⚠️ **Büyük dokümanlarda geniş bağlamlı grep YASAKTIR.** `Docs/Sistem-Ozeti.md` ~500 KB,
  `Docs/ArenaNet-Protokol.md` ~200 KB; `Grep -C 10` + yüksek `head_limit` ile birkaç arama on
  binlerce token yer. Yerine: **dar `Grep`** (`-C 2`, düşük `head_limit`) ile bölümü bul, sonra
  yalnız o bölümü `Read offset/limit` ile oku.
- ⚠️ **Birden çok bölüm gerekiyorsa iş tek alt-ajan görevine toplanır** — alt soruların hepsi tek
  görev metninde sorulur; aynı dokümanı üç ayrı aramayla kovalamak maliyeti üçe katlar. Dönen
  bölüm kritikse dar bir `Read offset/limit` ile teyit et.
- ⚠️ **1000 satırdan büyük dosya ana bağlama OKUNMAZ** — alt-ajana okutulur, özet alınır (1486
  satırlık bir dosyanın tek okuması 27.6k token). Önce `Grep`/hedefli `Read offset/limit` ile
  aranan bölüm bulunur, yalnız o okunur.
- ⚠️ **Unity MCP okumaları ayrıntılıdır:** yol biliniyorsa `get_hierarchy` yerine doğrudan bileşen
  sorgulanır (`mcpforunity://scene/gameobject/{id}/component/{ad}`) — tek `components` sorgusu
  bileşen başına onlarca alan döndürür.
- ⚠️ **`find_gameobjects` geniş sorguyla çağrılmaz:** ad filtresi dar tutulur, yoksa çıktı bağlamı
  taşırır ve diske düşer.

## 3. Ağır uygulama işini alt-ajana ver

Ağır iş (yeni script/bileşen, editor tool'u, çok dosyaya yayılan refactor, uzun doküman bölümü)
**alt-ajana devredilir**; ana bağlam orkestrasyona kalır. **Kullanıcının istemesi beklenmez.**
Ölçüt: *"ana bağlama uzun tool çıktısı mı yığacak, ve spesifikasyonu şu an net mi?"* İkisi de
evetse devret.

- **Ajan ayarı sabittir:** Opus 5 + medium effort, garantisi `.claude/agents/uygulayici.md`
  frontmatter'ında → `subagent_type: "uygulayici"` seç, çağrıda model/effort tekrar yazma. Model
  bilinçli olarak **sürüme** sabitlendi, alias'a (`opus`) değil: alias ileride başka modele çözülür
  ve davranış sessizce değişirdi.
- Yerleşik ajan tipinde (`Explore`, `general-purpose`, `claude-code-guide`) **`model: "opus"`
  parametresini elle geç** — yoksa ana oturumun modelini miras alır.
- `effort` **yalnız ajan tanımından** gelir (`Workflow` içindeki `agent()` hariç çağrı başına
  parametresi yoktur); farklı effort gerekiyorsa yeni ajan tanımı yazılır.
- **Aynı dosyalara dokunan ajanları paralel çalıştırma** — sıralı çalıştır ya da tek ajana ver;
  paralel çalışacaklara **ayrık dosya kümeleri** ver.
- **Kararı verilmemiş iş devredilmez:** tasarım tartışması, kapsam belirleme, kullanıcıya sorulacak
  seçim ana bağlamda kalır — ajan spesifikasyon uygular, yazmaz.
- **Keşif ile uygulamayı ayır** (§1): önce salt okuma `Explore` ajanı nereye dokunulacağını
  bulur, sonra `uygulayici`'ya o dosya listesi başlangıç bağlamı olarak verilir — uygulama ajanı
  aramayla vakit harcamaz.
- Ajanın döndürdüğü özet **kullanıcıya gösterilmez** — önemli olanı sen aktar.
- Arka planda koşan ajanın yerleşik araç seti kırpılır (MCP araçları kırpılmaz); yeni ajan yazarken
  ihtiyacı olan aracı `tools` listesinde açıkça belirt.

## 4. ⚠️ Derleme/build/test KULLANICIYA aittir — ajan projeyi DERLEMEZ

İş bitince ajan yalnız *"bitti, doğrulanacak"* der. Aşağıdakiler **hiçbir basamakta** çağrılmaz —
MCP'den de, shell'den de:

| Yasak | MCP karşılığı | Shell karşılığı |
|---|---|---|
| Script derlemesi | `recompile`, `recompile_status` | `unity cmd recompile` |
| Oyun build'i | `build`, `build_status`, `switch_build_target` | `unity build`, `scripts\deploy-*.bat` |
| Test koşusu | `run_tests`, `test_status` | `unity cmd run_tests` |
| Oynatma kipi | `editor_play`, `editor_pause`, `editor_stop` | — |
| Sunucu/launcher derlemesi | — | `dotnet build`, `dotnet publish`, `dotnet run` |
| Asset yeniden import | `refresh_unity`, `import_asset` | — |

⚠️ **"Sadece kontrol etmek için" istisnası YOKTUR:** tek bir `recompile` bile editörü kilitler,
domain reload tetikler ve kullanıcının elindeki sahne/Play oturumunu bozar.

- Bunun yerine **ne değiştiğini ve neyin doğrulanması gerektiğini** yaz — yeri sohbet değil,
  Notion doğrulama kartıdır (§5); gerçekten gerekiyorsa **kullanıcıdan iste**.
- **Kod doğruluğu yine ajanındır:** imzalar, namespace'ler, asmdef bağımlılıkları ve kullanılan
  API'ler yazmadan önce Read/Grep ile teyit edilir — "derleyici nasılsa yakalar" bir çalışma biçimi
  değildir.
- Konsol logu **okumak** (`get_console_logs` / `read_console`) yasak değildir; hiçbir şeyi
  tetiklemez.
- **Kullanıcı açıkça isterse** ("derle", "build al", "testleri koştur") kural düşer ve iş
  [[unity-erisim]] basamaklarıyla yapılır. "Çalışıyor mu bak" derleme izni DEĞİLDİR.

## 5. Doğrulama batch'lenir

Tüm implementasyon önce yazılır, sonda TEK birleşik doğrulama geçişi yapılır; ara doğrulama yalnız
gerçek bir blocker için, rutin teyit için değil. ⚠️ O tek geçişi de **kullanıcı koşar** (§4) — ajan
hiç doğrulama tetiklemez, o ana kadar biriktirir. Doküman güncellemesi de aynı geçişe girer
([[docs-sync]]).

- ⚠️ **Test maddeleri Notion'a yazılır, sohbete değil.** Kullanıcı derleme/build/testi kendisi
  koşmaz; iş bitince Notion `Todo` veritabanına (`collection://79d48db8-e104-8306-a23e-87a508fc649c`)
  `Doğrulama N — <konu>` kartı açılır (Durum *Yapılacak*): **önce** derleme/dağıtım adımları
  (hangi betik, hangi sırayla, derleme hatası çıkarsa karta yapıştır), sonra adım adım maddeler +
  "görmen gereken". Sohbete yalnız kartın bağlantısı düşer; "şunu derle/koş" listesi verilmez.
- ⚠️ **Kart SADE yazılır — testçi yalnız ne yapacağını ve ne göreceğini okur.** Her madde
  `yap → görmen gereken` biçimindedir. Karta **girmeyenler:** geçmiş anlatısı ("geçen turda şu
  bulunmuştu", "sebebi şuydu", "şu şöyle değişti"), eski kartlara gönderme ("21'den devredildi",
  "27 §2'nin kalanı"), plan dosyası/defter notu ("geçerse `plan/x.md` silinir"), ajanın kendi
  sağlama formülleri. Bunların yeri git geçmişi, `Docs/` ve `plan/`'dır; kart, kararı vermesi
  gereken kişiye ("Karar (Ertu): …") tek satırlık soru taşıyabilir, gerekçe taşımaz.
- **Kart Alper'e atanır, Durum *Yapılacak* kalır.** ⚠️ *Test bekliyor* bir Doğrulama kartına
  KONMAZ: kartın içi zaten testtir, *Test bekliyor* geliştirme kartının "yazıldı, testi bekliyor"
  hâlidir. Atanmamış kart testçinin listesinde görünmez, eski karta büyük harfli not olarak geri
  döner. Aynı build turuna bağlı kartlar tek satırla birbirine bağlanır ("28 · 29 · 30 aynı build
  ile test edilir").
- Biten kart "Todo Arşiv" sayfasına taşınır, açık kalan maddesi önce yeni karta aktarılır. Kalıcı
  bilgi yine repoda ([[docs-sync]]) — kart geçicidir, doküman yerine geçmez.
