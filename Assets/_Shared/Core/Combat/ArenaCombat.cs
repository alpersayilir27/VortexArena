using System.Collections.Generic;
using UnityEngine;
using VortexArena.Core.Arena;
using VortexArena.Core.Player;
using VortexArena.Net;
using VortexArena.Protocol;

namespace VortexArena.Core.Combat
{
    /// <summary>
    /// <b>Oyun kodunun ağa açılan tek kapısı.</b> Kendi silahını, okunu, baltanı, bombanı ya da
    /// tuzağını yazarken protokol DTO'suna, arena uzayı dönüşümüne ve sıra numarasına hiç dokunma —
    /// buradaki metotları çağır.
    ///
    /// <para><b>Neden bu sınıf var:</b> bir vuruşu doğru bildirmek dört ayrı şeyi bilmeyi gerektirir
    /// (poz arena uzayına çevrilmeli, YÖN bir nokta değildir, hedef bir <see cref="RemoteHitBox"/>
    /// olmalı, hasarı istemci belirler). Bunlar <see cref="Weapon"/> içinde gömülü kalsaydı ikinci
    /// bir hasar kaynağı yazan herkes aynı dört şeyi yeniden keşfetmek zorunda kalırdı — ve biri
    /// yanlış keşfedince vuruş sessizce kaybolurdu.</para>
    ///
    /// <para><b>Otorite sende değil:</b> bu metotlar hasarı UYGULAMAZ, yalnız sunucuya BİLDİRİR.
    /// Can, ölüm, skor ve maç fazı sunucudan <c>health_update</c>/<c>kill_event</c> ile geri gelir
    /// (Docs/ArenaNet-Protokol.md §10.3). Canı yerelde düşürme — iki taraf sapar.</para>
    ///
    /// <para><b>Hepsi bağlantı yokken sessizce no-op'tur.</b> Sunucusuz editör oturumunda oyun
    /// kodun aynen çalışır; hiçbir çağrının etrafına <c>if (bağlıysa)</c> yazman gerekmez.</para>
    /// </summary>
    public static class ArenaCombat
    {
        // Her atışta yeni DTO ayırmamak için tek örnek yeniden kullanılır: ArenaClient.Send
        // JSON'a ÇAĞRI İÇİNDE çevirir (JsonUtility.ToJson senkron), dolayısıyla gönderim
        // bittiğinde nesne serbesttir. Dizi alanları da bir kez ayrılır.
        private static readonly ShotFiredMsg Shot = new ShotFiredMsg { muzzlePos = new float[3], muzzleDir = new float[3] };
        private static readonly HitReportMsg Hit = new HitReportMsg { hitPos = new float[3] };
        private static int _seq;

        /// <summary><see cref="ReportAreaHit"/>'in çakışma tamponu — her patlamada yeni dizi
        /// ayırmamak için.</summary>
        private static readonly Collider[] OverlapBuffer = new Collider[64];
        private static readonly HashSet<int> AreaHitOnce = new HashSet<int>();

        // ------------------------------------------------------------------ durum

        /// <summary>Yerel oyuncunun sunucu kimliği; bağlanmadıysa <c>0</c>.</summary>
        public static int LocalPlayerId =>
            PlayerCombatState.Instance != null ? PlayerCombatState.Instance.PlayerId : 0;

        /// <summary>Sunucuya bağlı mıyız (mesajlar gerçekten gidiyor mu).</summary>
        public static bool IsConnected => ArenaClient.Instance != null && ArenaClient.Instance.IsConnected;

        /// <summary>Yerel oyuncu hayatta mı (sunucu-otoriter, <c>health_update</c>'ten).</summary>
        public static bool IsAlive => PlayerCombatState.Instance == null || PlayerCombatState.Instance.IsAlive;

        /// <summary>Yerel oyuncunun canı (0..<see cref="ArenaProtocol.PLAYER_MAX_HP"/>).</summary>
        public static float LocalHp =>
            PlayerCombatState.Instance != null ? PlayerCombatState.Instance.Hp : ArenaProtocol.PLAYER_MAX_HP;

        /// <summary>Yerel oyuncunun takımı; takımsız modda <see cref="Team.Neutral"/>.</summary>
        public static Team LocalTeam =>
            PlayerCombatState.Instance != null ? PlayerCombatState.Instance.Team : Team.Neutral;

        /// <summary>
        /// <b>Tetiğe basılabilir mi.</b> Hayatta + faz Lobby/Live + (bir kez bağlanıldıysa) bağlantı
        /// açık. Her ateş eden şey bunu kontrol ETMELİDİR: ölüyken ya da geri sayımda atılan atış
        /// sunucuda zaten reddedilir, ama yerelde ses/efekt oynatmak oyuncuya yalan söyler.
        /// </summary>
        public static bool CanFire => PlayerCombatState.Instance == null || PlayerCombatState.Instance.CanFire;

        // ------------------------------------------------------------- hedef çözme

        /// <summary>
        /// Bir çarpışmanın arkasında AĞ OYUNCUSU var mı. Raycast'in vurduğu collider'ı ver;
        /// <c>true</c> dönerse <paramref name="playerId"/> o oyuncunun kimliğidir ve hasarı
        /// <see cref="ReportHit"/> ile BİLDİRMELİSİN. <c>false</c> dönerse hedef ağ oyuncusu
        /// değildir (pratik dummy'si, kırılabilir obje) — orada eski yerel
        /// <see cref="Health.TakeDamage"/> yolu geçerlidir.
        /// </summary>
        public static bool TryGetTargetPlayerId(Collider collider, out int playerId)
        {
            playerId = 0;
            if (collider == null)
            {
                return false;
            }

            // Isabet kutusu gövdenin herhangi bir çocuğunda olabilir — yukarı doğru aranır.
            RemoteHitBox hitBox = collider.GetComponentInParent<RemoteHitBox>();
            if (hitBox == null || hitBox.PlayerId <= 0)
            {
                return false;
            }

            playerId = hitBox.PlayerId;
            return true;
        }

        /// <summary>Kafa vuruşu mu (çarpan collider bir <see cref="RemoteHitBox.IsHead"/> kutusu mu).
        /// Kafa çarpanını UYGULAMAK senin işin: hasarı çarpıp <see cref="ReportHit"/>'e ver —
        /// sunucu gönderdiğin sayıyı aynen uygular (§10.3).</summary>
        public static bool IsHeadshot(Collider collider)
        {
            if (collider == null)
            {
                return false;
            }

            RemoteHitBox hitBox = collider.GetComponentInParent<RemoteHitBox>();
            return hitBox != null && hitBox.IsHead;
        }

        // ---------------------------------------------------------------- bildirim

        /// <summary>
        /// <b>Bir atış yapıldı</b> — namlu alevi/sesi diğer oyuncuların ekranında da görünsün diye.
        /// Sunucu bunu DOĞRULAMAZ, yalnız atan hariç herkese relay eder; hasarla ilgisi yoktur.
        /// <para>Dünya uzayında ver — arena uzayına çevrimi bu metot yapar.</para>
        /// </summary>
        /// <param name="worldMuzzlePosition">Namlu ucunun dünya konumu.</param>
        /// <param name="worldDirection">Merminin gittiği dünya yönü (normalize edilmesi gerekmez).</param>
        /// <param name="weaponId">Kill feed etiketi ("ak47"). Sunucu doğrulamaz, serbesttir.</param>
        public static void ReportShot(Vector3 worldMuzzlePosition, Vector3 worldDirection, string weaponId)
        {
            ArenaClient client = ArenaClient.Instance;
            if (client == null || !client.IsConnected)
            {
                return;
            }

            Vector3 arenaPos = ArenaSpace.WorldToArena(worldMuzzlePosition);
            // ⚠️ YÖN BİR NOKTA DEĞİLDİR: iki noktayı ayrı ayrı çevirip farkını almak origin
            // ötelemesini düşürür, geriye yalnız dönüş kalır. Doğrudan WorldToArena(dir) demek
            // yönü arena origin'i kadar KAYDIRIRDI.
            Vector3 arenaDir = (ArenaSpace.WorldToArena(worldMuzzlePosition + worldDirection.normalized) - arenaPos).normalized;

            Shot.seq = ++_seq;
            Shot.weaponId = weaponId ?? "";
            Write(Shot.muzzlePos, arenaPos);
            Write(Shot.muzzleDir, arenaDir);
            client.Send(Shot);
        }

        /// <summary>
        /// <b>Bir ağ oyuncusuna hasar verdim</b> — sunucu doğrular ve <c>health_update</c> yayınlar.
        /// <para>
        /// <b>Hasarı SEN belirlersin</b> (§10.3): sunucuda silah tablosu yoktur, gönderdiğin sayı
        /// aynen uygulanır. Mesafeye göre düşen patlama, yay çekiş gücü, kafa çarpanı — hepsi
        /// burada hesaplanıp tek sayı olarak verilir. Sunucu yalnız durumu doğrular (faz Live mı,
        /// atıcı ve hedef canlı mı, dost ateşi açık mı) ve sayının kullanılabilir olduğuna bakar
        /// (NaN/∞/negatif reddedilir).
        /// </para>
        /// <para>Canı YERELDE DÜŞÜRME: hedefin canı sunucudan geri gelir.</para>
        /// </summary>
        /// <param name="targetPlayerId"><see cref="TryGetTargetPlayerId"/>'den gelen kimlik.</param>
        /// <param name="worldHitPoint">İsabet noktasının dünya konumu (efekt/istatistik için).</param>
        /// <param name="damage">Uygulanacak hasar — <b>pozitif ve sonlu olmalı</b>.</param>
        /// <param name="weaponId">Kill feed etiketi; boş bırakılabilir (yalnız etiket kaybolur).</param>
        public static void ReportHit(int targetPlayerId, Vector3 worldHitPoint, float damage, string weaponId)
        {
            if (targetPlayerId <= 0)
            {
                return;
            }

            if (!float.IsFinite(damage) || damage <= 0f)
            {
                Debug.LogWarning($"[ArenaCombat] Geçersiz hasar ({damage}) — vuruş gönderilmedi. " +
                                 "Hasar pozitif ve sonlu olmalı (sunucu da reddeder).");
                return;
            }

            ArenaClient client = ArenaClient.Instance;
            if (client == null || !client.IsConnected)
            {
                return;
            }

            Hit.seq = ++_seq;
            Hit.targetPlayerId = targetPlayerId;
            Hit.weaponId = weaponId ?? "";
            Hit.damage = damage;
            Write(Hit.hitPos, ArenaSpace.WorldToArena(worldHitPoint));
            client.Send(Hit);
        }

        /// <summary>
        /// Hitscan silahlar için kısayol: raycast sonucundaki hedef bir ağ oyuncusuysa vuruşu
        /// bildirir ve <c>true</c> döner.
        /// <para>
        /// <c>false</c> dönerse hedef ağ oyuncusu DEĞİLDİR — çağıran orada kendi yerel hasar
        /// yolunu işletir. Tipik kullanım:
        /// <code>
        /// if (Physics.Raycast(muzzle.position, dir, out var hit, range))
        /// {
        ///     if (!ArenaCombat.ReportRaycastHit(hit, damage, "ok"))
        ///         hit.collider.GetComponentInParent&lt;Health&gt;()?.TakeDamage(damage, null);
        /// }
        /// </code>
        /// </para>
        /// </summary>
        public static bool ReportRaycastHit(in RaycastHit hit, float damage, string weaponId)
        {
            if (!TryGetTargetPlayerId(hit.collider, out int playerId))
            {
                return false;
            }

            ReportHit(playerId, hit.point, damage, weaponId);
            return true;
        }

        /// <summary>
        /// <b>Alan etkisi</b> (bomba, el bombası, şok dalgası): yarıçap içindeki HER ağ oyuncusuna
        /// AYRI bir vuruş bildirir — protokolde "alan hasarı" diye bir mesaj yoktur (§10.3), alan
        /// etkisi n tane <c>hit_report</c> demektir.
        /// <para>
        /// Hasar merkeze uzaklıkla doğrusal düşer: merkezde <paramref name="damage"/>, kenarda
        /// <paramref name="damage"/> × <paramref name="edgeScale"/>. Her oyuncuya en fazla BİR
        /// vuruş gider (bir gövdede birden çok isabet kutusu var).
        /// </para>
        /// <para>⚠️ Duvar arkası kontrolü YAPILMAZ. Görüş hattı istiyorsan
        /// <see cref="TryGetTargetPlayerId"/> + kendi <c>Physics.Linecast</c>'inle kur.</para>
        /// </summary>
        /// <returns>Vuruş bildirilen oyuncu sayısı.</returns>
        public static int ReportAreaHit(Vector3 worldCenter, float radius, float damage, string weaponId,
            float edgeScale = 0.25f, int layerMask = ~0)
        {
            if (radius <= 0f || !float.IsFinite(damage) || damage <= 0f)
            {
                return 0;
            }

            AreaHitOnce.Clear();
            int count = Physics.OverlapSphereNonAlloc(worldCenter, radius, OverlapBuffer, layerMask,
                QueryTriggerInteraction.Collide);

            if (count >= OverlapBuffer.Length)
            {
                Debug.LogWarning($"[ArenaCombat] Alan etkisi tamponu doldu ({OverlapBuffer.Length}); " +
                                 "yarıçapı küçült ya da layerMask ver — bazı hedefler atlanmış olabilir.");
            }

            int reported = 0;
            for (int i = 0; i < count; i++)
            {
                if (!TryGetTargetPlayerId(OverlapBuffer[i], out int playerId) || !AreaHitOnce.Add(playerId))
                {
                    continue;
                }

                Vector3 point = OverlapBuffer[i].ClosestPoint(worldCenter);
                float t = Mathf.Clamp01(Vector3.Distance(worldCenter, point) / radius);
                float applied = damage * Mathf.Lerp(1f, Mathf.Clamp01(edgeScale), t);
                if (applied <= 0f)
                {
                    continue;
                }

                ReportHit(playerId, point, applied, weaponId);
                reported++;
            }

            return reported;
        }

        // ---------------------------------------------------------------- yardımcı

        private static void Write(float[] target, in Vector3 value)
        {
            target[0] = value.x;
            target[1] = value.y;
            target[2] = value.z;
        }
    }
}
