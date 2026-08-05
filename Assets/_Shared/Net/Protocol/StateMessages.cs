using System.IO;

namespace VortexArena.Protocol
{
    // UDP state katmanı — binary, little-endian (Docs/ArenaNet-Protokol.md §6).
    // Write tip baytını yazar; Read, tip baytının dispatcher tarafından
    // OKUNMUŞ olduğunu varsayar (geri kalanı parse eder).

    /// Paket tip baytları.
    public static class UdpPacketType
    {
        public const byte UdpHello = 0x00;
        public const byte PoseUpdate = 0x01;
        public const byte Snapshot = 0x02;
        public const byte FireEvent = 0x03;
        public const byte EventBatch = 0x04;

        /// <summary>0x05 — snapshot + olay batch'i TEK datagramda (<see cref="SnapshotWithEvents"/>).
        /// Sığmadığında sunucu <c>0x02</c>+<c>0x04</c>'e düşer; ikisi de kaldırılmadı.</summary>
        public const byte SnapshotWithEvents = 0x05;

        /// <summary>0x06 — RTT yoklaması; sunucu aynı baytları geri yollar (<see cref="RttProbe"/>).
        /// <para>⚠️ WS'teki <c>MessageTypes.Ping</c> ile <b>alakası yoktur</b>: o, sunucunun
        /// "bana bir <c>status</c> yolla" tetiğidir ve TCP üzerindedir — gecikme ölçmez ve ölçemez
        /// (TCP retransmit'i sonuca karışır). Gecikme oyunun aktığı kanaldan, buradan ölçülür.</para></summary>
        public const byte RttProbe = 0x06;

        /// <summary>0x07 — retarget edilmiş iskelet blob'u + arena-uzayı kökü
        /// (<see cref="SkeletonUpdate"/>, §6.9). İstemci → sunucu,
        /// <see cref="ArenaProtocol.SKELETON_RATE_HZ"/>; yalnız player.</summary>
        public const byte SkeletonUpdate = 0x07;

        /// <summary>0x08 — iskelet girdilerinin batch'i (<see cref="SkeletonBatch"/>, §6.10).
        /// <para>⚠️ <b>Neden batch:</b> oyuncu başına ayrı datagram yollamak tik başına hedef
        /// başına N paket demek olurdu ve bu üründe darboğaz bant değil paket sayısıdır
        /// (<c>Docs/Sistem-Ozeti.md</c> §3.12).</para></summary>
        public const byte SkeletonBatch = 0x08;
    }

    /// Poz bloğu: f32 px,py,pz,qx,qy,qz,qw — 28 B, arena uzayında.
    public struct PoseData
    {
        public const int SIZE = 28;

        public float px, py, pz, qx, qy, qz, qw;

        public void Write(BinaryWriter w)
        {
            w.Write(px); w.Write(py); w.Write(pz);
            w.Write(qx); w.Write(qy); w.Write(qz); w.Write(qw);
        }

        public static PoseData Read(BinaryReader r)
        {
            PoseData p;
            p.px = r.ReadSingle(); p.py = r.ReadSingle(); p.pz = r.ReadSingle();
            p.qx = r.ReadSingle(); p.qy = r.ReadSingle(); p.qz = r.ReadSingle(); p.qw = r.ReadSingle();
            return p;
        }
    }

    /// 0x00 — [u8 tip][u8 playerId][u32 udpToken] = 6 B. Sunucu aynı paketi ack olarak geri yollar.
    public struct UdpHello
    {
        public const int SIZE = 6;

        public byte playerId;
        public uint udpToken;

        public void Write(BinaryWriter w)
        {
            w.Write(UdpPacketType.UdpHello);
            w.Write(playerId);
            w.Write(udpToken);
        }

        public static UdpHello Read(BinaryReader r)
        {
            UdpHello m;
            m.playerId = r.ReadByte();
            m.udpToken = r.ReadUInt32();
            return m;
        }
    }

    /// <summary>
    /// 0x05 — [u8 tip][u8 playerCount][u8 eventCount][u32 serverTick] + playerCount×
    /// <see cref="SnapshotEntry"/> + eventCount×<see cref="FireEventEntry"/>. Başlık <b>7 B</b>.
    /// <para><b>Varlık sebebi paket sayısı:</b> tipik bir maçta (10 oyuncu) snapshot 886 B ve 5 olay
    /// 45 B — ikisi tek datagrama rahat sığıyor, oysa ayrı gönderildiklerinde tik başına hedef başına
    /// <b>iki</b> datagram üretiliyordu. Bant kazancı yok denecek kadar az; kazanç
    /// <b>airtime</b>'dadır (Docs/Sistem-Ozeti.md §3.12).</para>
    /// <para>⚠️ <c>0x02</c> ve <c>0x04</c> <b>kaldırılmadı ve kaldırılmaz</b>: snapshot parçalanmak
    /// zorunda kaldığında (16'dan fazla girdi) ya da toplam
    /// <see cref="ArenaProtocol.COMBINED_MAX_BYTES"/>'ı aştığında sunucu onlara düşer.</para>
    /// <para>⚠️ <b>Tik başına ya 0x05 ya 0x04 üretilir, ikisi birden ASLA.</b> §6.5'in kopya
    /// koruması "tik başına en fazla bir olay datagramı" değişmezine dayanıyor ve kimlik
    /// <c>serverTick</c>; aynı tik için iki olay datagramı çıkarsa istemci ikincisini birebir tekrar
    /// sanıp <b>düşürür</b>. Aynı sebeple parçalanmış snapshot'ta olaylar bu pakete HİÇ girmez —
    /// parçalar arasında olay bloğu çoğaltmak tam olarak bu değişmezi kırardı.</para>
    /// </summary>
    public struct SnapshotWithEvents
    {
        /// <summary>[tip][playerCount][eventCount][serverTick] — girdilerden önceki sabit kısım.</summary>
        public const int HEADER_SIZE = 7;

        public uint serverTick;
        public SnapshotEntry[] players;
        public FireEventEntry[] events;

        public void Write(BinaryWriter w)
        {
            w.Write(UdpPacketType.SnapshotWithEvents);
            w.Write((byte)players.Length);
            w.Write((byte)events.Length);
            w.Write(serverTick);
            for (int i = 0; i < players.Length; i++) players[i].Write(w);
            for (int i = 0; i < events.Length; i++) events[i].Write(w);
        }

        public static SnapshotWithEvents Read(BinaryReader r)
        {
            SnapshotWithEvents m;
            byte playerCount = r.ReadByte();
            byte eventCount = r.ReadByte();
            m.serverTick = r.ReadUInt32();
            m.players = new SnapshotEntry[playerCount];
            for (int i = 0; i < playerCount; i++) m.players[i] = SnapshotEntry.Read(r);
            m.events = new FireEventEntry[eventCount];
            for (int i = 0; i < eventCount; i++) m.events[i] = FireEventEntry.Read(r);
            return m;
        }
    }

    /// <summary>
    /// 0x06 — [u8 tip][u8 playerId][u32 clientStamp] = <b>6 B</b>. İstemci → sunucu, 1 Hz; sunucu
    /// <b>aynı 6 baytı</b> geri yollar (<see cref="UdpHello"/> ack'inin birebir aynı deseni).
    /// <para><b>Ölçen taraf İSTEMCİDİR:</b> <c>RTT = şimdi − clientStamp</c>. Damga opaktır — sunucu
    /// yorumlamaz, yalnız taşır; bu yüzden <b>saat senkronu gerekmez</b> (iki damga da istemcinin).</para>
    /// <para><b>Neden ayrı bir paket gerekiyor</b> (üçü de denendi ve reddedildi):
    /// <c>clientTimeMs</c> saat senkronu olmadan mutlak gecikme vermez; sunucunun snapshot'ta
    /// istemcinin damgasını geri yollaması damgayı <b>hedefe özel</b> yapardı ve tek paylaşımlı
    /// buffer'ı tik başına N serileştirmeye çevirirdi (§6.5 aynı gerekçeyle olay batch'ini de hedefe
    /// özelleştirmiyor); WS/TCP üzerinden ölçmek ise retransmit'i gecikmeye karıştırır.</para>
    /// <para>⚠️ <b>1 Hz'in üstüne çıkarılmaz:</b> her yoklama 2 datagram (gidiş + echo) demektir ve
    /// bu ürünün darboğazı bant değil paket sayısıdır. Jitter zaten snapshot varışlarından 20 Hz
    /// çözünürlükle ve <b>sıfır ek paketle</b> ölçülüyor; bu paket yalnız operatörün okuduğu
    /// "ping" sayısı içindir.</para>
    /// </summary>
    public struct RttProbe
    {
        public const int SIZE = 6;

        public byte playerId;

        /// <summary>İstemcinin gönderim anı — <b>yalnız istemci için anlamlı</b>, sunucu okumaz.</summary>
        public uint clientStamp;

        public void Write(BinaryWriter w)
        {
            w.Write(UdpPacketType.RttProbe);
            w.Write(playerId);
            w.Write(clientStamp);
        }

        public static RttProbe Read(BinaryReader r)
        {
            RttProbe m;
            m.playerId = r.ReadByte();
            m.clientStamp = r.ReadUInt32();
            return m;
        }
    }

    /// <summary>
    /// 0x01 — [u8 tip][u8 playerId][u16 seq][u32 clientTimeMs][u8 itemL][u8 itemR][u8 gripFlags]
    /// [head][handL][handR] = <b>95 B</b> (§6.2).
    /// <para><b>Eşya baytları neden pozla aynı pakette:</b> ikisi de aynı otoriteye ait —
    /// "elimde ne var" da "elim nerede" gibi <b>istemci-otoriter bir sunum bilgisidir</b>. Sunucu
    /// bunları doğrulamaz, snapshot'a kopyalar (§6.3); sunucuda eşya tablosu YOKTUR.</para>
    /// <para>⚠️ <c>gripFlags</c>'te bit0 (<see cref="SnapshotEntry.FLAG_ALIVE"/>) gelirse yok
    /// sayılır — sunucu <see cref="SnapshotEntry.GRIP_FLAG_MASK"/> ile süzer, istemci kendini canlı
    /// ilan edemez.</para>
    /// </summary>
    public struct PoseUpdate
    {
        public const int SIZE = 95;

        public byte playerId;
        public ushort seq;
        public uint clientTimeMs;

        /// <summary>Sol/sağ eldeki eşyanın <c>netItemId</c>'si; 0 = el boş (§6.6).</summary>
        public byte itemL;
        public byte itemR;

        /// <summary>Snapshot'a kopyalanan istemci bitleri: <c>FLAG_GRIP_LINKED</c> |
        /// <c>FLAG_PRIMARY_RIGHT</c> | <c>FLAG_HAND_L_STALE</c> | <c>FLAG_HAND_R_STALE</c> (§6.3).
        /// Ad kavramadan gelir ama içerik yalnız kavrama değildir — süzgeç
        /// <see cref="SnapshotEntry.GRIP_FLAG_MASK"/>.</summary>
        public byte gripFlags;

        public PoseData head;
        public PoseData handL;
        public PoseData handR;

        public void Write(BinaryWriter w)
        {
            w.Write(UdpPacketType.PoseUpdate);
            w.Write(playerId);
            w.Write(seq);
            w.Write(clientTimeMs);
            w.Write(itemL);
            w.Write(itemR);
            w.Write(gripFlags);
            head.Write(w);
            handL.Write(w);
            handR.Write(w);
        }

        public static PoseUpdate Read(BinaryReader r)
        {
            PoseUpdate m;
            m.playerId = r.ReadByte();
            m.seq = r.ReadUInt16();
            m.clientTimeMs = r.ReadUInt32();
            m.itemL = r.ReadByte();
            m.itemR = r.ReadByte();
            m.gripFlags = r.ReadByte();
            m.head = PoseData.Read(r);
            m.handL = PoseData.Read(r);
            m.handR = PoseData.Read(r);
            return m;
        }
    }

    /// <summary>
    /// Snapshot oyuncu girdisi: [u8 playerId][u8 flags][u8 itemL][u8 itemR][head][handL][handR]
    /// = <b>88 B</b> (§6.3).
    /// <para><b>flags: tek bayt, iki yazar</b> (otorite bölünmesinin tel karşılığı) — bit0
    /// <b>sunucunun</b>dur (otoriter <c>alive</c>), bit1-5 istemcinin <c>gripFlags</c>'inden
    /// kopyalanır. Bit6-7 rezerv: sıfır yazılır, okuyucu yok sayar.</para>
    /// </summary>
    public struct SnapshotEntry
    {
        public const int SIZE = 88;

        /// <summary>Sunucu yazar: oyuncu hayatta (otoriter durum).</summary>
        public const byte FLAG_ALIVE = 1 << 0;

        /// <summary>İstemciden kopya: iki el AYNI eşyayı tutuyor (çift tabanca DEĞİL, §6.6).</summary>
        public const byte FLAG_GRIP_LINKED = 1 << 1;

        /// <summary>İstemciden kopya: ana el sağ. Yalnız <see cref="FLAG_GRIP_LINKED"/> iken anlamlı.</summary>
        public const byte FLAG_PRIMARY_RIGHT = 1 << 2;

        /// <summary>
        /// İstemciden kopya: sol el pozu <b>ölçülmüş değil</b> — gönderen son geçerli pozu
        /// <b>kafaya göreli</b> tutuyor (el arena uzayında donmaz, gövdeyle taşınır).
        /// <para>Kumandanın pili bittiğinde rig el anchor'ını koşulsuz yazar ve okuma
        /// <c>(0,0,0)</c> döner; akışı kesmek ya da eli sıfırlamak seçenek DEĞİLDİR (paket sabit
        /// uzunluklu — "eli olmayan oyuncu" diye bir tel durumu yok; sıfır poz eli oyuncunun
        /// ayağının dibine koyar), bu yüzden son geçerli poz tutulup burada işaretlenir.</para>
        /// <para>⚠️ Bayrağın işi alıcının <b>yorumudur</b>: bayat el bir ölçüm değil tahmindir —
        /// nişan/temas teşhisi ona dayandırılmaz, admin bunu operatöre gösterir.</para>
        /// </summary>
        public const byte FLAG_HAND_L_STALE = 1 << 3;

        /// <inheritdoc cref="FLAG_HAND_L_STALE"/>
        public const byte FLAG_HAND_R_STALE = 1 << 4;

        /// <summary>
        /// İstemciden kopya: gönderenin gövdesi bir <b>iç engelin İÇİNDE</b> (§10.9) — gövdenin
        /// %30'u, kafanın tamamı ya da silahın tamamı engel hacmine girmiş.
        /// <para>⚠️ <b>Ölçüm istemcinin, SONUÇ sunucunun:</b> bu bit yalnız "gövdem içeride" der;
        /// can eritmeyi sunucu kendi tikinde ve kendi saatiyle yapar (<c>hit_report</c>'un hasar
        /// modelinin aynısı — ölçüm istemcide, otorite sunucuda). İstemci bu bitle kendine hasar
        /// yazdıramaz, yalnız cezanın <b>başlamasını</b> bildirir.</para>
        /// <para>⚠️ Bayrak <b>durumdur, olay değil</b>: her pakette yeniden gönderilir. Kaybolan bir
        /// paket 50 ms sonra kendini onarır — kenar tetikli bir bildirimde kaybolan "çıktım"
        /// oyuncuyu sonsuza kadar duvarda bırakırdı.</para>
        /// </summary>
        public const byte FLAG_IN_OBSTACLE = 1 << 5;

        /// <summary>
        /// İstemciden kopyalanmasına izin verilen bitler. <b>Varlık sebebi bir bekçidir:</b> sunucu
        /// <c>PoseUpdate.gripFlags</c>'i bu maskeyle süzüp snapshot'a yazar, böylece istemci bit0'ı
        /// (<see cref="FLAG_ALIVE"/> — kendini canlı ilan etmeyi) set EDEMEZ. Maskesiz kopyalama
        /// ölü bir oyuncunun kendini diriltmesi olurdu.
        /// <para>Maskedeki beş bit de <b>doğrulanmadan</b> kopyalanır: kavrama, bayat el ve engel
        /// ihlali eşya baytlarıyla aynı türden <b>istemci-otoriter ÖLÇÜM bilgisidir</b>
        /// (§6.6/§10.3/§10.9). Sunucunun yazdığı bitler maskenin DIŞINDA kalır.</para>
        /// </summary>
        public const byte GRIP_FLAG_MASK =
            FLAG_GRIP_LINKED | FLAG_PRIMARY_RIGHT | FLAG_HAND_L_STALE | FLAG_HAND_R_STALE |
            FLAG_IN_OBSTACLE;

        public byte playerId;
        public byte flags;

        /// <summary>Sol/sağ eldeki eşyanın <c>netItemId</c>'si; 0 = el boş (§6.6).</summary>
        public byte itemL;
        public byte itemR;

        public PoseData head;
        public PoseData handL;
        public PoseData handR;

        public void Write(BinaryWriter w)
        {
            w.Write(playerId);
            w.Write(flags);
            w.Write(itemL);
            w.Write(itemR);
            head.Write(w);
            handL.Write(w);
            handR.Write(w);
        }

        public static SnapshotEntry Read(BinaryReader r)
        {
            SnapshotEntry e;
            e.playerId = r.ReadByte();
            e.flags = r.ReadByte();
            e.itemL = r.ReadByte();
            e.itemR = r.ReadByte();
            e.head = PoseData.Read(r);
            e.handL = PoseData.Read(r);
            e.handR = PoseData.Read(r);
            return e;
        }
    }

    /// 0x02 — [u8 tip][u8 playerCount][u32 serverTick] + playerCount × SnapshotEntry.
    /// 16 oyuncu: 6 + 16×88 = 1414 B (tek UDP paketi).
    public struct Snapshot
    {
        public uint serverTick;
        public SnapshotEntry[] players;

        public void Write(BinaryWriter w)
        {
            w.Write(UdpPacketType.Snapshot);
            w.Write((byte)players.Length);
            w.Write(serverTick);
            for (int i = 0; i < players.Length; i++)
                players[i].Write(w);
        }

        public static Snapshot Read(BinaryReader r)
        {
            Snapshot s;
            byte count = r.ReadByte();
            s.serverTick = r.ReadUInt32();
            s.players = new SnapshotEntry[count];
            for (int i = 0; i < count; i++)
                s.players[i] = SnapshotEntry.Read(r);
            return s;
        }
    }

    /// <summary>
    /// Tek atış/atma olayının kaydı: [u8 playerId][u8 kindHand][u8 itemId][i16 dirOctX]
    /// [i16 dirOctY][u16 magnitude] = <b>9 B</b>. Hem <c>0x03</c> (§6.4) hem <c>0x04</c> (§6.5)
    /// gövdesinde aynı alanlar taşınır; <c>seq</c> yalnız yukarı yönde vardır.
    /// <para><b>Yön neden telde:</b> 20 Hz interpole el pozundan türetilse aynı tik'e düşen iki
    /// atış aynı yöne giderdi ve geri tepme kaybolurdu. <b>Orijin neden telde DEĞİL:</b> tracer
    /// alıcının ÇİZDİĞİ namludan çıkmalı — mutlak namlu konumu gönderilirse gözle kaymış görünür
    /// (tutarlılık > sadakat, §6.4).</para>
    /// </summary>
    public struct FireEventEntry
    {
        public const int SIZE = 9;

        /// <summary>Tür: hitscan atış.</summary>
        public const byte KIND_SHOT = 0;

        /// <summary>Tür: fırlatma (atma).</summary>
        public const byte KIND_THROW = 1;

        /// <summary><c>kindHand</c>'in alt nibble'ı türdür.</summary>
        public const byte KIND_MASK = 0x0F;

        /// <summary><c>kindHand</c>'in bit7'si el: set = sağ, temiz = sol.</summary>
        public const byte HAND_RIGHT_BIT = 0x80;

        public byte playerId;

        /// <summary>Alt nibble = tür (<see cref="KIND_MASK"/>), bit7 = el (<see cref="HAND_RIGHT_BIT"/>).</summary>
        public byte kindHand;

        /// <summary>Olay anındaki eşyanın <c>netItemId</c>'si (§6.6) — sunum profilini çözer;
        /// durum baytı kaybolsa da olay kendi kendine yeter.</summary>
        public byte itemId;

        /// <summary>Oktahedral sıkıştırılmış birim yön, arena uzayında
        /// (<see cref="OctahedralDirection"/>).</summary>
        public short dirOctX, dirOctY;

        /// <summary>Türe göre: atışta <b>mesafe</b> (cm → 0–655 m), atmada <b>başlangıç hızı</b>
        /// (cm/sn).</summary>
        public ushort magnitude;

        public static byte PackKindHand(byte kind, bool rightHand)
            => (byte)((kind & KIND_MASK) | (rightHand ? HAND_RIGHT_BIT : 0));

        public byte Kind => (byte)(kindHand & KIND_MASK);
        public bool IsRightHand => (kindHand & HAND_RIGHT_BIT) != 0;

        /// <summary>Gövde yazıcısı — tip baytı YOKTUR. Yalnız <see cref="EventBatch"/> kullanır;
        /// <c>0x03</c>'te alan sırası farklıdır (bkz. <see cref="FireEvent"/>).</summary>
        public void Write(BinaryWriter w)
        {
            w.Write(playerId);
            w.Write(kindHand);
            w.Write(itemId);
            w.Write(dirOctX);
            w.Write(dirOctY);
            w.Write(magnitude);
        }

        public static FireEventEntry Read(BinaryReader r)
        {
            FireEventEntry e;
            e.playerId = r.ReadByte();
            e.kindHand = r.ReadByte();
            e.itemId = r.ReadByte();
            e.dirOctX = r.ReadInt16();
            e.dirOctY = r.ReadInt16();
            e.magnitude = r.ReadUInt16();
            return e;
        }
    }

    /// <summary>
    /// 0x03 — [u8 tip][u8 playerId][u16 seq][u8 kindHand][u8 itemId][i16][i16][u16] = <b>12 B</b>
    /// (istemci → sunucu, olay başına; §6.4). <b>HEMEN gönderilir</b>, poz tik'i beklenmez.
    /// <para><b><c>seq</c> sözleşmesi — YALNIZ yukarı yön:</b>
    /// ✅ <b>kopya bastırma</b> (sunucu oyuncu başına son <c>seq</c>'i tutar; UDP paket çoğaltabilir
    /// → çift tracer + çift ses), ✅ <b>kayıp ölçümü</b> (<c>seq</c> boşluğu = kaybolan olay sayısı),
    /// ❌ <b>SIRA ZORLAMASI YOK.</b></para>
    /// <para>⚠️ "Eski <c>seq</c>'i at" kuralı <b>POZ</b> kuralıdır (durum: son gelen kazanır) ve
    /// olaylara UYGULANMAZ: sırası bozuk gelen atış gerçekten olmuş bir atıştır; atmak sessizce bir
    /// tracer ve bir ses silmektir.</para>
    /// </summary>
    public struct FireEvent
    {
        public const int SIZE = 12;

        public ushort seq;

        /// <summary>Olay gövdesi. <c>entry.playerId</c> telde <c>seq</c>'ten ÖNCE gider (aşağıya bak).</summary>
        public FireEventEntry entry;

        // ⚠️ Alanlar elle sırayla yazılır/okunur, entry.Write/Read ÇAĞRILMAZ: 0x03'te tel düzeni
        // [tip][playerId][seq][kindHand]… yani entry'nin playerId'si seq'in ÖNÜNDE. FireEventEntry'nin
        // kendi Write/Read'i yalnız 0x04 gövdesi içindir (orada seq yoktur, alanlar bitişiktir).
        public void Write(BinaryWriter w)
        {
            w.Write(UdpPacketType.FireEvent);
            w.Write(entry.playerId);
            w.Write(seq);
            w.Write(entry.kindHand);
            w.Write(entry.itemId);
            w.Write(entry.dirOctX);
            w.Write(entry.dirOctY);
            w.Write(entry.magnitude);
        }

        public static FireEvent Read(BinaryReader r)
        {
            FireEvent m;
            m.entry = default;
            m.entry.playerId = r.ReadByte();
            m.seq = r.ReadUInt16();
            m.entry.kindHand = r.ReadByte();
            m.entry.itemId = r.ReadByte();
            m.entry.dirOctX = r.ReadInt16();
            m.entry.dirOctY = r.ReadInt16();
            m.entry.magnitude = r.ReadUInt16();
            return m;
        }
    }

    /// <summary>
    /// 0x04 — [u8 tip][u8 count][u32 serverTick] + count × <see cref="FireEventEntry"/>
    /// = 6 + count×9 B (sunucu → tüm istemciler, 20 Hz; §6.5). Olay yoksa <b>paket yok</b>.
    /// <para><b>Kopya koruması <c>seq</c> DEĞİL TİK'tir:</b> batch'in kimliği <c>serverTick</c> ve
    /// <b>tik başına en fazla bir batch</b> üretilir (bu yüzden taşan olay atılmaz, sonraki tik'e
    /// kayar — <see cref="ArenaProtocol.EVENT_MAX_ENTRIES_PER_PACKET"/>). İstemci son işlediği
    /// <see cref="ArenaProtocol.EVENT_TICK_HISTORY"/> tik'i halkada tutar ve yalnız <b>birebir
    /// tekrarı</b> düşürür.</para>
    /// <para>⚠️ Eski tik'li ama görülmemiş batch <b>OYNATILIR</b> (interp saati o tik'i geçmişse
    /// hemen): ~50 ms gecikmiş tracer, kaybolmuş tracer'dan iyidir.</para>
    /// <para>⚠️ Snapshot'a EKLENMEZ, ayrı datagramdır — 1414 B + tek olay MTU'yu aşar.</para>
    /// </summary>
    public struct EventBatch
    {
        public uint serverTick;
        public FireEventEntry[] events;

        public void Write(BinaryWriter w)
        {
            w.Write(UdpPacketType.EventBatch);
            w.Write((byte)events.Length);
            w.Write(serverTick);
            for (int i = 0; i < events.Length; i++)
                events[i].Write(w);
        }

        public static EventBatch Read(BinaryReader r)
        {
            EventBatch b;
            byte count = r.ReadByte();
            b.serverTick = r.ReadUInt32();
            b.events = new FireEventEntry[count];
            for (int i = 0; i < count; i++)
                b.events[i] = FireEventEntry.Read(r);
            return b;
        }
    }

    /// <summary>
    /// 0x07 — [u8 tip][u8 playerId][u16 seq][root: <see cref="PoseData"/> 28][u16 len][blob]
    /// (istemci → sunucu, <see cref="ArenaProtocol.SKELETON_RATE_HZ"/>; yalnız player, §6.9).
    /// <para><b>Blob OPAKTIR.</b> İçeriği Meta Movement SDK'nın native serileştirmesidir
    /// (<c>SerializeSkeletonAndFace</c>); sunucu onu açmaz, doğrulamaz, yalnız kopyalar — sunucuda
    /// iskelet tablosu YOKTUR ve eklenmez. Gerekçe <c>netItemId</c> baytlarınınkiyle aynıdır (§6.6):
    /// bu bir <b>istemci-otoriter sunum bilgisi</b>dir.</para>
    /// <para>⚠️ <b><c>root</c> neden ayrı bir alan:</b> blob'un kendi 0. eklemi SDK tarafından
    /// <c>JointType.NoWorldSpace</c> ile yazılır, yani <b>gönderenin dünya pozudur</b> ve alıcının
    /// arenasıyla ilgisi yoktur. Blob opak olduğu için içindeki kökü çeviremeyiz; bu yüzden kök
    /// arena uzayında AYRICA taşınır ve alıcı <c>ApplyBodyPose</c>'dan sonra karakterin kökünü
    /// bununla yazar. Blob'un kendi kökü kullanılmaz.</para>
    /// <para>⚠️ Bu kanalda <b>parçalama YOKTUR</b>: blob
    /// <see cref="ArenaProtocol.SKELETON_MAX_BLOB_BYTES"/>'ı aşarsa paket hiç gönderilmez. Yarım
    /// bir kareyi deserialize etmek bozuk iskelet demektir.</para>
    /// </summary>
    public struct SkeletonUpdate
    {
        /// <summary>[tip][playerId][seq][root 28][len] — blob'dan önceki sabit kısım.</summary>
        public const int HEADER_SIZE = 34;

        public byte playerId;

        /// <summary>Sarmalanır (u16); eski <c>seq</c> gelirse paket atılır — <c>0x01</c> ile aynı
        /// "son gelen kazanır" kuralı (§6.2). Durum kanalıdır, olay değil.</summary>
        public ushort seq;

        /// <summary>Karakter kökünün <b>arena uzayı</b> pozu (§3).</summary>
        public PoseData root;

        /// <summary>Serileştirilmiş iskelet. ⚠️ Yalnız ilk <see cref="blobLength"/> baytı geçerlidir
        /// — gönderen havuzlanmış tampon verebilsin diye dizi boyu bağlayıcı değildir.</summary>
        public byte[] blob;

        public int blobLength;

        public void Write(BinaryWriter w)
        {
            w.Write(UdpPacketType.SkeletonUpdate);
            w.Write(playerId);
            w.Write(seq);
            root.Write(w);
            w.Write((ushort)blobLength);
            w.Write(blob, 0, blobLength);
        }

        public static SkeletonUpdate Read(BinaryReader r)
        {
            SkeletonUpdate m;
            m.playerId = r.ReadByte();
            m.seq = r.ReadUInt16();
            m.root = PoseData.Read(r);
            int len = r.ReadUInt16();

            // Meşru bir gönderen bu sınırı aşamaz (gönderim tarafı da aşanı yollamıyor); aşan değer
            // bozuk/kırpılmış datagram demektir — boş blob geri döner, çağıran girdiyi düşürür.
            if (len > ArenaProtocol.SKELETON_MAX_BLOB_BYTES)
            {
                m.blob = System.Array.Empty<byte>();
                m.blobLength = 0;
                return m;
            }

            m.blob = r.ReadBytes(len);

            // ⚠️ Kırpılmış blob boş sayılır — bkz. <see cref="SkeletonEntry.Read"/>. Bu yol
            // ÖZELLİKLE kritiktir: sunucu uplink'i buradan okuyup blob'u OLDUĞU GİBİ tüm
            // istemcilere relay ediyor, yani yarım bir kare tek bir oyuncuyu değil arenadaki
            // HERKESİ bozuk iskeletle çizerdi. Sunucunun blobLength == 0 denetimi ancak bu
            // atama doğruysa kırpılmışı da yakalar.
            if (m.blob.Length != len)
            {
                m.blob = System.Array.Empty<byte>();
                m.blobLength = 0;
                return m;
            }

            m.blobLength = len;
            return m;
        }
    }

    /// <summary>
    /// <c>0x08</c> batch'inin oyuncu girdisi: [u8 playerId][root 28][u16 len][blob]
    /// = <b>31 + len</b> B (§6.10). Alanların anlamı <see cref="SkeletonUpdate"/> ile birebir aynı;
    /// <c>seq</c> taşınmaz (sunucu eskisini zaten ayıkladı).
    /// </summary>
    public struct SkeletonEntry
    {
        /// <summary>[playerId][root 28][len] — blob'dan önceki sabit kısım.</summary>
        public const int HEADER_SIZE = 31;

        public byte playerId;
        public PoseData root;
        public byte[] blob;
        public int blobLength;

        /// <summary>Bu girdinin telde kapladığı bayt — batch'i bölerken kullanılır.</summary>
        public int Size => HEADER_SIZE + blobLength;

        public void Write(BinaryWriter w)
        {
            w.Write(playerId);
            root.Write(w);
            w.Write((ushort)blobLength);
            w.Write(blob, 0, blobLength);
        }

        public static SkeletonEntry Read(BinaryReader r)
        {
            SkeletonEntry e;
            e.playerId = r.ReadByte();
            e.root = PoseData.Read(r);
            int len = r.ReadUInt16();

            if (len > ArenaProtocol.SKELETON_MAX_BLOB_BYTES)
            {
                e.blob = System.Array.Empty<byte>();
                e.blobLength = 0;
                return e;
            }

            e.blob = r.ReadBytes(len);

            // ⚠️ KISA OKUMA SESSİZ BOZULMADIR: BinaryReader.ReadBytes akış erken bittiğinde
            // istisna ATMAZ, daha kısa bir dizi döner. Uzunluğu okunan bayta göre yazmak
            // kırpılmış bir blob'u "geçerli" ilan ederdi ve Movement SDK onu deserialize edip
            // bozuk bir iskelet üretirdi (uzak avatarın rastgele şekillere girmesi). Yarım kare
            // yok sayılır: blobLength = 0 olan girdiyi RemoteSkeletonRegistry zaten düşürür.
            if (e.blob.Length != len)
            {
                e.blob = System.Array.Empty<byte>();
                e.blobLength = 0;
                return e;
            }

            e.blobLength = len;
            return e;
        }
    }

    /// <summary>
    /// 0x08 — [u8 tip][u8 count][u32 serverTick] + count × <see cref="SkeletonEntry"/>
    /// (sunucu → tüm istemciler, §6.10). Girdi yoksa <b>paket yok</b>.
    /// <para><b>Parçalama:</b> girdiler değişken uzunluklu olduğu için sunucu hem bayt bütçesine
    /// (<see cref="ArenaProtocol.COMBINED_MAX_BYTES"/>) hem girdi tavanına
    /// (<see cref="ArenaProtocol.SKELETON_MAX_ENTRIES_PER_PACKET"/>) bakar; taşan girdi aynı tik
    /// içinde ek datagrama yazılır. Her datagram kendi <c>count</c>'unu, hepsi aynı
    /// <c>serverTick</c>'i taşır — snapshot parçalamasının aynısı (§6.3), istemcide birleştirme
    /// mantığı gerekmez.</para>
    /// <para>⚠️ <b>Gönderen kendi girdisini de geri alır ve KENDİSİ yok sayar</b> — kendi gövdesini
    /// sensörden çiziyor. Hedefe özel batch üretmek tik başına N serileştirme demek olurdu; §6.5
    /// olay batch'i de aynı gerekçeyle atanı süzmüyor.</para>
    /// <para>⚠️ Snapshot'a (<c>0x05</c>) <b>birleştirilmez</b>: snapshot 16 girdide zaten 1414 B ve
    /// değişken uzunluklu bir blok eklemek onun boyut garantisini çökertir.</para>
    /// </summary>
    public struct SkeletonBatch
    {
        /// <summary>[tip][count][serverTick] — girdilerden önceki sabit kısım.</summary>
        public const int HEADER_SIZE = 6;

        public uint serverTick;
        public SkeletonEntry[] entries;

        /// <summary>
        /// Paylaşılan bir diziden <b>bir dilimi</b> yazar (parçalama). Örnek metot yerine statik:
        /// sunucu tik başına tek bir tampon dizisi tutup onu bölerek yolluyor — her parça için
        /// ayrı dizi kopyalamak tik başına tahsis demek olurdu.
        /// </summary>
        public static void Write(BinaryWriter w, uint serverTick, SkeletonEntry[] entries, int offset, int count)
        {
            w.Write(UdpPacketType.SkeletonBatch);
            w.Write((byte)count);
            w.Write(serverTick);
            for (int i = 0; i < count; i++)
                entries[offset + i].Write(w);
        }

        public static SkeletonBatch Read(BinaryReader r)
        {
            SkeletonBatch b;
            byte count = r.ReadByte();
            b.serverTick = r.ReadUInt32();
            b.entries = new SkeletonEntry[count];
            for (int i = 0; i < count; i++)
            {
                b.entries[i] = SkeletonEntry.Read(r);

                // Kırpılmış girdiden sonrasını okumaya çalışmak akış sonunda istisna atardı ve
                // ondan ÖNCE okunmuş sağlam girdiler de birlikte düşerdi. Döngü burada kesilir;
                // kalan yuvalar blobLength = 0 ile boş kalır ve tüketici tarafında elenir.
                if (b.entries[i].blobLength == 0)
                {
                    break;
                }
            }

            return b;
        }
    }
}
