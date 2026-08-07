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
    /// <para><b>İki bildirimin İKİ AYRI kanalı vardır ve bu bilinçlidir:</b>
    /// <see cref="ReportShot"/>/<see cref="ReportThrow"/> birer <i>sunum</i> olayıdır (namlu alevi,
    /// ses, tracer) → UDP olay kanalı, güvenilirlik aranmaz, kaybolursa yalnız bir efekt eksilir
    /// (§6.4). <see cref="ReportHit"/> ise <i>otoriter durumu</i> değiştirir (can, ölüm, skor) →
    /// WS'te <c>hit_report</c> olarak kalır. 600 RPM'de atış olayları otoriter kanalı boğuyordu;
    /// ayırmak o kanalı atış gürültüsünden tümüyle kurtarır.</para>
    ///
    /// <para><b>Hepsi bağlantı yokken sessizce no-op'tur.</b> Sunucusuz editör oturumunda oyun
    /// kodun aynen çalışır; hiçbir çağrının etrafına <c>if (bağlıysa)</c> yazman gerekmez.</para>
    ///
    /// <para><b>Tek sunum işi burada durur: isabet göstergesi</b> (<see cref="HitMarker"/>) —
    /// bildirilen her vuruşun değdiği noktada vuran oyuncuya bir X çizilir. Kapının içinde
    /// olmasının sebebi kapının kendisiyle aynı: yeni bir hasar kaynağı (ok, balta, bomba) onu
    /// bedavaya alsın, "vurdum mu" sorusunun cevabı hasar kaynağına göre değişmesin. Kendi
    /// göstergeni yazma.</para>
    /// </summary>
    public static class ArenaCombat
    {
        // Her vuruşta yeni DTO ayırmamak için tek örnek yeniden kullanılır: ArenaClient.Send
        // JSON'a ÇAĞRI İÇİNDE çevirir (JsonUtility.ToJson senkron), dolayısıyla gönderim
        // bittiğinde nesne serbesttir. Dizi alanları da bir kez ayrılır.
        // (Atış olayının DTO'su burada yok: UDP kanalı kendi ön-ayrılmış tamponunu kullanır.)
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

        // ------------------------------------------------------------------- atış ışını

        /// <summary>
        /// Namlunun <b>gerisinden</b> yoklanan mesafe (m): namlu gövdesi bir engelin içinden
        /// geçiyor mu. Bir tüfek namlusu kadardır — daha uzunu köşeye yaslanmış meşru bir atışı
        /// engellemeye başlar, daha kısası ince bir siperi ıskalar.
        /// </summary>
        private const float BarrelProbeMeters = 0.30f;

        /// <summary>Yutulan atışın iz uzunluğu (m): mermi namludan çıkar çıkmaz ölür.</summary>
        private const float BlockedTracerMeters = 0.05f;

        /// <summary>
        /// Bir atışın ışın sonucu: nereye kadar gitti, bir şeye çarptı mı, engel tarafından
        /// <b>yutuldu</b> mu.
        /// </summary>
        public readonly struct ShotTrace
        {
            /// <summary>Mermiyi bir iç engel yuttu — <b>hiçbir hedefe hasar yazılmaz</b>.</summary>
            public readonly bool Blocked;

            /// <summary>Işın bir collider'a çarptı (yalnız <see cref="Blocked"/> değilken anlamlı).</summary>
            public readonly bool HasHit;

            /// <summary>Çarpma kaydı; <see cref="HasHit"/> false ise anlamsızdır.</summary>
            public readonly RaycastHit Hit;

            /// <summary>Işının GERÇEKTE gittiği mesafe — iz ve <see cref="ReportShot"/> bunu kullanır.</summary>
            public readonly float Distance;

            private ShotTrace(bool blocked, bool hasHit, RaycastHit hit, float distance)
            {
                Blocked = blocked;
                HasHit = hasHit;
                Hit = hit;
                Distance = distance;
            }

            internal static ShotTrace BlockedShot() =>
                new ShotTrace(true, false, default, BlockedTracerMeters);

            internal static ShotTrace HitShot(in RaycastHit hit) =>
                new ShotTrace(false, true, hit, hit.distance);

            internal static ShotTrace Miss(float range) =>
                new ShotTrace(false, false, default, range);
        }

        /// <summary>
        /// <b>Bir hitscan atışının ışını — ateş eden her şey bunu kullanır.</b> Kendi
        /// <c>Physics.Raycast</c>'ini yazma: engel kuralı burada durur ve yarın eklenen ok/balta/
        /// mermi onu bedavaya alır.
        ///
        /// <para><b>Neden düz bir raycast yetmiyor:</b> Unity'de <b>ışının orijini bir collider'ın
        /// içindeyse o collider hiç vurulmaz</b>. Namlusunu sandığın içine sokan oyuncunun mermisi
        /// bu yüzden sandığı delip geçer ve arkasındaki oyuncuyu vurur — namlunun ucunu ince bir
        /// duvarın öbür yüzüne geçirmek de aynı kapıdır (orijin artık duvarın ötesindedir). İki
        /// durumu da yalnızca <b>orijini ayrıca sınamak</b> yakalar.</para>
        ///
        /// <para>Engel yuttuğunda iz namluda biter ve <c>hit_report</c> hiç gönderilmez.
        /// ⚠️ <b>Tetikli silahlar buraya normalde HİÇ GELMEZ:</b> onların kapısı
        /// <see cref="IsMuzzleBlocked"/>'tır ve tetiği tümden öldürür (cephane gitmez, ses/alev
        /// oynamaz). Buradaki dal, tetiği olmayan ya da kapıyı bilmeyen bir hasar kaynağı için
        /// <b>ikinci savunma hattıdır</b>.</para>
        ///
        /// <para>⚠️ Ana ışın <b>maskesiz</b> kalır (uzak isabet kutuları Default layer'ındadır) ama
        /// <b>trigger'ları elemek zorundadır</b>: proje ayarı <c>Queries Hit Triggers</c> açık ve
        /// sahnedeki silahların ISDK kavrama hacimleri trigger — elenmezse tezgâhın önünden atılan
        /// mermi kavrama hacmine çarpıp durur.</para>
        /// </summary>
        /// <param name="muzzleWorld">Namlu ucunun dünya konumu.</param>
        /// <param name="direction">Merminin yönü, <b>birim uzunlukta</b> (mesafeler buna dayanır).</param>
        /// <param name="range">Silahın menzili (m).</param>
        public static ShotTrace TraceShot(Vector3 muzzleWorld, Vector3 direction, float range)
        {
            if (IsMuzzleBlocked(muzzleWorld, direction))
            {
                return ShotTrace.BlockedShot();
            }

            return Physics.Raycast(muzzleWorld, direction, out RaycastHit hit, range,
                       ~0, QueryTriggerInteraction.Ignore)
                ? ShotTrace.HitShot(hit)
                : ShotTrace.Miss(range);
        }

        /// <summary>
        /// <b>Namlu bir iç engel tarafından tıkanmış mı</b> — duvar arkasından ateş etmeyi engelleyen
        /// tek test.
        ///
        /// <para>İki soru sorar: <b>(1)</b> namlu ucu bir engelin içinde mi, <b>(2)</b> namlu gövdesi
        /// (<see cref="BarrelProbeMeters"/> geri) bir engelin içinden geçiyor mu. İkincisi ince siper
        /// içindir: ucunu öbür yüze geçiren oyuncunun namlu ucu artık boşluktadır, yani birinci test
        /// onu kaçırır.</para>
        ///
        /// <para>⚠️ Geri yoklama <b>yalnız <c>Obstacle</c> maskesine</b> bakar: maskesiz bir ışın
        /// oyuncunun kendi eline/silahına takılır ve meşru atışları sessizce yutardı.</para>
        ///
        /// <para><b>İki tüketicisi vardır ve testin tek yerde durması şarttır:</b> tetik kapısı
        /// (<c>Weapon</c> — tıkalıyken tetik hiç işlemez, cephane gitmez) ve
        /// <see cref="TraceShot"/> (tetiği olmayan hasar kaynakları için ikinci savunma hattı).
        /// İkisi ayrı yazılsaydı biri sapar ve belirti "bazı silahlar duvardan ateş edebiliyor"
        /// olurdu.</para>
        /// </summary>
        /// <param name="muzzleWorld">Namlu ucunun dünya konumu.</param>
        /// <param name="direction">Merminin yönü, <b>birim uzunlukta</b>.</param>
        public static bool IsMuzzleBlocked(Vector3 muzzleWorld, Vector3 direction)
        {
            if (ObstacleVolumes.ContainsPoint(muzzleWorld))
            {
                return true;
            }

            int obstacleMask = ArenaLayers.ObstacleMask;
            return obstacleMask != 0 &&
                   Physics.Raycast(muzzleWorld - direction * BarrelProbeMeters, direction,
                       BarrelProbeMeters, obstacleMask, QueryTriggerInteraction.Ignore);
        }

        /// <summary>
        /// <b>Tetik kapısı: silahın HERHANGİ bir parçası bir iç engele değiyor mu</b> (§10.9).
        /// Tetikli her silah ateşlemeden önce bunu sormalıdır.
        ///
        /// <para><b>Neden namlu testi yetmiyor:</b> namlu bir NOKTA, silah ise bir HACİMDİR. Oyuncu
        /// tüfeği tuğlanın arkasına iyice geçirip yalnız namlu ucunu boşlukta bırakabiliyor —
        /// gövdesini hiç göstermeden ateş ediyor ve nokta testi bunu göremiyor. Buradaki kutu testi
        /// silahın çizilen gövdesini olduğu gibi sorar.</para>
        ///
        /// <para>Kutu <b>yönlendirilmiştir</b> (silahın kendi rotasyonu): eksen hizalı bir kutu,
        /// çapraz tutulan bir tüfekte gövdenin iki katı hacim kaplar ve siperin yanında duran meşru
        /// atışları da keserdi.</para>
        ///
        /// <para>⚠️ <paramref name="bodyRoot"/> <b>silahın geometri kökü olmalıdır</b> (model), silah
        /// prefabının kökü DEĞİL: kökün altında kavrama çerçevesi gibi silaha ait olmayan görseller
        /// duruyor ve onları kutuya katmak silahı olduğundan çok daha büyük gösterirdi.</para>
        /// </summary>
        /// <param name="bodyRoot">Silah geometrisinin kökü; <c>null</c> ise kutu testi atlanır.</param>
        /// <param name="localBounds">Silah gövdesinin <paramref name="bodyRoot"/> uzayındaki sınırları.</param>
        /// <param name="muzzleWorld">Namlu ucunun dünya konumu.</param>
        /// <param name="direction">Merminin yönü, <b>birim uzunlukta</b>.</param>
        public static bool IsWeaponBlocked(Transform bodyRoot, in Bounds localBounds,
            Vector3 muzzleWorld, Vector3 direction)
        {
            // Namlu kapısı önce: tek nokta + kısa ışın, kutu sorgusundan ucuz.
            if (IsMuzzleBlocked(muzzleWorld, direction))
            {
                return true;
            }

            if (bodyRoot == null)
            {
                return false;
            }

            Vector3 scale = bodyRoot.lossyScale;
            var halfExtents = new Vector3(
                localBounds.extents.x * Mathf.Abs(scale.x),
                localBounds.extents.y * Mathf.Abs(scale.y),
                localBounds.extents.z * Mathf.Abs(scale.z));

            return ObstacleVolumes.OverlapsBox(bodyRoot.TransformPoint(localBounds.center),
                halfExtents, bodyRoot.rotation);
        }

        // ------------------------------------------------------------- hedef çözme

        /// <summary>
        /// Bir çarpışmanın arkasında AĞ OYUNCUSU var mı. Raycast'in vurduğu collider'ı ver;
        /// <c>true</c> dönerse <paramref name="playerId"/> o oyuncunun kimliğidir ve hasarı
        /// <see cref="ReportHit"/> ile BİLDİRMELİSİN. <c>false</c> dönerse hedef ağ oyuncusu
        /// değildir (dekor, duvar) ve <b>hasar diye bir şey yoktur</b>: istemcide can
        /// tutan bir yol yok, o hedef hasar almaz. Kırılabilir objeler ileride ağsal olacak.
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

        /// <summary>Çarpan collider'ın vuruş bölgesi; ağ oyuncusu değilse <c>HitZone.Body</c>
        /// (çarpan 1×). Bölge çarpanını UYGULAMAK senin işin: hasarı
        /// <c>WeaponDefinition.GetZoneMultiplier</c> ile çarpıp <see cref="ReportHit"/>'e ver —
        /// sunucu gönderdiğin sayıyı aynen uygular (§10.3).</summary>
        public static HitZone GetHitZone(Collider collider)
        {
            if (collider == null)
            {
                return HitZone.Body;
            }

            RemoteHitBox hitBox = collider.GetComponentInParent<RemoteHitBox>();
            return hitBox != null ? hitBox.Zone : HitZone.Body;
        }

        // ---------------------------------------------------------------- bildirim

        /// <summary>
        /// <b>§6.4: bir atış yapıldı</b> — uzak namlu alevi/sesi/tracer'ı için. Sunucu bunu
        /// DOĞRULAMAZ, yalnız relay eder ve <b>hasarla hiçbir ilgisi yoktur</b> (o ayrı bir
        /// bildirimdir: <see cref="ReportHit"/>). İkisini birden çağırmak normaldir — bir atış
        /// hem olur hem isabet eder.
        /// <para>
        /// <b>Namlu KONUMU gönderilmez</b> (ve gönderilmemeli): tracer alıcının ÇİZDİĞİ silahın
        /// namlusundan çıkmalı. Mutlak bir orijin, alıcı silahı interpole edilmiş el pozundan
        /// çizdiği için çizilen namludan kaymış bir tracer verirdi — tutarlılık &gt; sadakat (§6.4).
        /// </para>
        /// </summary>
        /// <param name="worldDirection">Merminin gittiği dünya yönü (normalize edilmesi gerekmez).</param>
        /// <param name="distanceMeters">Işının GERÇEKTE gittiği mesafe: isabet varsa
        /// <c>hit.distance</c>, yoksa silahın menzili. Tracer'ın uzunluğu bundan gelir.</param>
        /// <param name="netItemId">Ateş eden eşyanın <c>netItemId</c>'si (§6.6); <c>0</c> =
        /// çözülemedi (uzak taraf sunum profilini bulamaz, olayı yine de duyar).</param>
        /// <param name="rightHand">Olay sağ elden mi çıktı (telde tek bit — "bilinmiyor" yok).</param>
        public static void ReportShot(Vector3 worldDirection, float distanceMeters, byte netItemId, bool rightHand)
        {
            SendFireEvent(FireEventEntry.KIND_SHOT, worldDirection, distanceMeters, netItemId, rightHand);
        }

        /// <summary>
        /// <b>§6.4: bir eşya atıldı</b> (bomba). Alıcılar aynı balistiği <b>YEREL simüle eder</b> —
        /// yerçekimi tek kuvvet olduğu için deterministiktir, akış (poz akışı) GEREKMEZ. Bu yüzden
        /// telde yalnız yön + başlangıç hızı gider; sapma kozmetiktir ve patlamayla kendini bitirir.
        /// <para>Patlamanın hasarı bu metotla DEĞİL, mevcut yoldan bildirilir:
        /// <see cref="ReportAreaHit"/> (hedef başına bir <c>hit_report</c>).</para>
        /// </summary>
        /// <param name="worldDirection">Atış yönü, dünya uzayı (normalize edilmesi gerekmez).</param>
        /// <param name="speedMetersPerSecond">Başlangıç hızı (m/sn).</param>
        /// <param name="netItemId">Atılan eşyanın <c>netItemId</c>'si (§6.6).</param>
        /// <param name="rightHand">Hangi elden atıldı.</param>
        public static void ReportThrow(Vector3 worldDirection, float speedMetersPerSecond, byte netItemId, bool rightHand)
        {
            SendFireEvent(FireEventEntry.KIND_THROW, worldDirection, speedMetersPerSecond, netItemId, rightHand);
        }

        /// <summary>
        /// İki olay türünün ortak gönderim yolu. Kanal/kayıt yoksa <b>sessiz no-op</b> (sınıfın
        /// sözleşmesi): sunucusuz editör oturumunda silahlar aynen çalışır.
        /// <para>Yön dönüşümü <see cref="ArenaSpace.WorldToArenaDirection"/>'a bırakılır — Net
        /// katmanı arena uzayını bilmez, çevrim çağıranın (yani bu kapının) işidir.</para>
        /// </summary>
        private static void SendFireEvent(byte kind, Vector3 worldDirection, float magnitudeMeters,
            byte netItemId, bool rightHand)
        {
            UdpStateChannel channel = ArenaClient.Instance?.UdpChannel;
            if (channel == null)
            {
                return;
            }

            channel.SendFireEvent(kind, rightHand, netItemId,
                ArenaSpace.WorldToArenaDirection(worldDirection), magnitudeMeters);
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
        /// <para>Canı YERELDE DÜŞÜRME: hedefin canı sunucudan geri gelir. Bu bir tercih değil,
        /// mutlak kuraldır — istemcide can tutan hiçbir bileşen yoktur.</para>
        /// <para><b>İsabet göstergesini bu metot çizer</b> (<see cref="HitMarker"/>): vuruş
        /// noktasında yalnız VURANIN gördüğü bir X. Ayrıca bir şey çağırma. ⚠️ Gösterge
        /// <i>bildirimin yapıldığını</i> söyler, hasarın uygulandığını değil — sunucu vuruşu
        /// reddedebilir (dost ateşi kapalı, faz <c>playing</c> değil, hedef zaten ölü; §10.3).
        /// Otoriter sonucu beklemek göstergeyi gidiş-dönüş kadar geciktirir ve
        /// <c>health_update</c> vuruşun NEREYE değdiğini taşımaz.</para>
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

            // İsabet göstergesi — bilinçli olarak gönderimden SONRA: bildirilmemiş bir vuruş için
            // X çizmek oyuncuya yalan söylerdi (CanFire'ın gerekçesiyle aynı). Yalnız vuranın
            // ekranında koşar (bu metot yalnız hasarı veren istemcide çağrılır) ve telde karşılığı
            // yoktur.
            HitMarker.Shared.Play(worldHitPoint);
        }

        /// <summary>
        /// Hitscan silahlar için kısayol: raycast sonucundaki hedef bir ağ oyuncusuysa vuruşu
        /// bildirir ve <c>true</c> döner.
        /// <para>
        /// <c>false</c> dönerse hedef ağ oyuncusu DEĞİLDİR ve <b>hasar uygulanmaz</b> (istemcide
        /// can yok). Dönüş değeri yalnız bir SUNUM kararı içindir — gövde efekti mi, duvar efekti
        /// mi oynatayım:
        /// <code>
        /// if (Physics.Raycast(muzzle.position, dir, out var hit, range))
        /// {
        ///     bool isPlayer = ArenaCombat.ReportRaycastHit(hit, damage, "ok");
        ///     Instantiate(isPlayer ? bloodFx : sparkFx, hit.point, Quaternion.LookRotation(hit.normal));
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
