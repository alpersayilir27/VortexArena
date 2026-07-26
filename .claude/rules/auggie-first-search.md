# Kural: Bağlam/kod aramasında birincil araç auggie

`mcp__auggie__codebase-retrieval` (Augment indeksi) **birincil arama aracıdır**. "Bu nasıl
çalışıyor / X nerede / bunu değiştirirsem nereler etkilenir" gibi doğal dilli sorularda ÖNCE
auggie'ye sor, dönen dosya/simge listesini Read/Grep ile teyit et.

- **Kullan:** keşif ve oryantasyon, "hangi bileşen ne yapıyor", çapraz katman izleri
  (Unity `_Shared/Net` ↔ `Protocol` ↔ `Server/`), refactor öncesi etki alanı çıkarma,
  adını bilmediğin bir davranışın kaynağını bulma.
- **Kullanma (doğrudan Grep/Glob/Read daha hızlı ve kesin):** tam simge/string adı biliniyorsa,
  tek bir dosya okunacaksa, `.meta`/`.asset`/JSON gibi üretilmiş dosyalarda arama yapılacaksa.
- **Bayatlık uyarısı:** auggie indeksten cevap verir; oturum içinde az önce yazılan/değiştirilen
  kodu bilmeyebilir. Kritik satırları Read ile doğrula; kullanıcıya verilen `dosya:satır`
  referansları asla yalnız auggie çıktısına dayanmasın.
- Alt-ajan açmadan önce auggie'yi dene ([[delegate-to-subagents]]): tek `codebase-retrieval`
  çağrısı çoğu keşif sorusunda Explore ajanından ucuz ve hızlıdır. Ajan yine de gerekiyorsa,
  auggie'nin çıktısını ajana başlangıç bağlamı olarak ver.
- Kayıt `.mcp.json`'da, Windows'ta `cmd /c auggie --mcp` ile çalışır ([[unity-cli]] MCP tablosu).
  İlk çağrı workspace'i indekslediği için gecikebilir; hata alırsan Grep/Glob'a düş, sessizce
  arama yapmadan kalma.
