> ⛔ **Önce kapı:** `UnityMCP` ayakta değilse **Unity verisine dayanan** iş (ve onu yapacak
> alt-ajan) açılmaz; dokunmayan iş sürer → [[unitymcp-zorunlu]]

# Kural: Ağır uygulama işini alt-ajana ver, ana bağlamı yalın tut

Ağır uygulama işi (yeni script/bileşen, editor tool'u, çok dosyaya yayılan tekrarlı refactor,
uzun doküman bölümü) **alt-ajana devredilir**. Ana bağlam orkestrasyona ve kararlara kalsın.

**Kullanıcının istemesi beklenmez.** İş gerçekten devretmeye değiyorsa ajan açılır — sormaya gerek
yok. Ölçüt basit: *"bu iş ana bağlama uzun tool çıktısı mı yığacak, ve spesifikasyonu şu an net mi?"*
İkisi de evetse devret.

## Ajan ayarı — bu projede sabittir

Alt-ajanlar **Opus 5** ve **medium effort** ile çalışır. Garantisi `.claude/agents/uygulayici.md`
frontmatter'ındadır (`model: claude-opus-5` + `effort: medium`) — devrederken
`subagent_type: "uygulayici"` seç, model/effort'u çağrıda tekrar yazmana gerek kalmaz.

- Yerleşik bir ajan tipi (`Explore`, `general-purpose`, `claude-code-guide`) kullanıyorsan
  **`model: "opus"` parametresini elle geç** — yoksa ana oturumun modelini miras alır.
- `effort` **yalnız ajan tanımından** gelir; çağrı başına effort parametresi yoktur (`Workflow`
  içindeki `agent()` hariç). Farklı bir effort gerekiyorsa yeni bir ajan tanımı yaz.
- Model bilinçli olarak **sürüme sabitlenmiştir**, alias'a (`opus`) değil: alias ileride başka bir
  modele çözülür ve bu projedeki davranış sessizce değişirdi. Yükseltmek istendiğinde tek satır.

## Devretmenin kuralları

- **Aynı dosyalara dokunan ajanları paralel çalıştırma** — sıralı çalıştır ya da tek ajana ver.
  Paralel çalışacaklara birbirinden ayrık dosya kümeleri ver.
- **Doğrulama ana thread'de ve toplu:** Unity CLI derleme/build/konsol ve `dotnet build` ajana
  bırakılmaz ([[batch-build-verification]], [[unity-cli]]). Ajan yazdığı kodun iç tutarlılığından
  sorumludur, derlemeden değil.
- **Kararı verilmemiş iş devredilmez.** Tasarım tartışması, kapsam belirleme, kullanıcıya
  sorulacak seçim ana bağlamda kalır — ajan spesifikasyon uygular, spesifikasyon yazmaz.
- **Keşif için önce auggie** ([[auggie-first-search]]): tek `codebase-retrieval` çağrısı çoğu
  "bu nerede / nasıl çalışıyor" sorusunda ajan açmaktan ucuz ve hızlıdır. Ajan yine de
  gerekiyorsa auggie çıktısını ona başlangıç bağlamı olarak ver.
- Ajanın döndürdüğü özet **kullanıcıya gösterilmez** — önemli olanı sen aktar.
- Arka planda koşan ajanın yerleşik araç seti kırpılır (MCP araçları kırpılmaz). `uygulayici`
  tanımındaki `tools` listesi buna göre seçilmiştir; yeni bir ajan yazarken ihtiyacı olan aracı
  açıkça listele.
