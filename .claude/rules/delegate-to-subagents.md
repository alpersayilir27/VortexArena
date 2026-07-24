# Kural: İşi alt-ajanlara devret, ana bağlamı yalın tut

Uygulama işini (script yazımı, editor tool'ları — saf dosya yazımı) mümkün olduğunca
sub-agent'lara ver; kısa özet döndürsünler.
- Unity MCP orkestrasyonunu (derle / build / doğrula) ana thread'de, toplu yap.
- Aynı dosyaları düzenleyen ajanları sıralı çalıştır.
- Neden: ana bağlam sahne dump'ları ve tool çıktılarıyla hızla dolar; ağır iş ajanlarda
  kalınca ana bağlam orkestrasyon ve kararlara kalır.
