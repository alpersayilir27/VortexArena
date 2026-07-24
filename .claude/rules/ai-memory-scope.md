# Kural: AI hafızası yalnızca proje scope'unda

Bu proje ile ilgili hiçbir bilgi user/global scope'a kaydedilmez (`~/.claude/**/memory/` dahil).
- Hatırlanması gereken her şey repo içinde kalır: kök `CLAUDE.md` ve `.claude/rules/`.
- "Şunu hatırla / not al" denince hedef her zaman proje scope'udur (bu repo).
- Yeni kalıcı kural → `.claude/rules/` altına yeni `.md`; kalıcı proje bilgisi/mimari → `CLAUDE.md`.
