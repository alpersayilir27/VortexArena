using UnityEngine;

namespace VortexArena.Core.Combat
{
    /// <summary>
    /// Mermi izi (tracer): namludan vuruş noktasına kısa süre çizilen çizgi. Sahnede DURMAZ ve
    /// kendi başına bir şey dinlemez — yalnız çizer; <b>ne çizileceğine iki çağıran karar verir:</b>
    /// atanın kendi izi için <see cref="Weapon"/>, uzak oyuncuların izi için
    /// <see cref="RemoteShotFx"/> (§6.4/6.5). İkisi ayrı olmak ZORUNDA: sunucu atış olayını atana
    /// geri yollamaz, istemci de kendi <c>playerId</c>'sini süzer.
    /// <para>
    /// ⚠️ <b>Havuz PAYLAŞIMLIDIR</b> (<see cref="Shared"/>) ve çağıran başına açılmaz: silahlar
    /// <c>weaponSource:"random"</c> modlarında sürekli üretilip yok ediliyor, silah başına havuz
    /// materyali + <c>Update</c> döngüsünü silah sayısınca çoğaltırdı.
    /// </para>
    /// <para>
    /// ⚠️ <b>HAVUZLU ve round-robin</b>: hedef yük tam ateşte ~53 olay/sn (16 oyuncu × üçte bir
    /// tracer). Bu hızda <c>Instantiate</c>/<c>Destroy</c> döngüsü Quest'te doğrudan GC dikeni
    /// demektir; düğümler bir kez üretilir, sonra yalnız yeniden konumlanır. Havuz dolduğunda en
    /// eski tracer kesilir (yeni atışı düşürmek, bir kare fazla duran çizgiden kötüdür).
    /// </para>
    /// <para>
    /// Görünüm parametreleri (<c>tracerColor/Width/Lifetime</c>) eşyanın kendisinden gelir
    /// (<see cref="ItemDefinition"/>) — tracer uzak çizim verisidir, playtest'te oradan ayarlanır.
    /// </para>
    /// </summary>
    public class ShotTracer : MonoBehaviour
    {
        /// <summary>Havuzdaki çizgi sayısı (aynı anda canlı kalabilecek tracer).</summary>
        // ~53 olay/sn × 0.06 sn ömür ≈ 3-4 eşzamanlı; ömür playtest'te uzatılabildiği için pay var.
        private const int PoolSize = 24;

        /// <summary>Bu mesafeden kısa "atış"a tracer çizilmez (dejenere/eksik mesafe).</summary>
        private const float MinTracerMeters = 0.5f;

        /// <summary>Çizgi materyalinin shader arama zinciri (ilk bulunan kullanılır).</summary>
        // ⚠️ "Sprites/Default" başta duruyor çünkü Graphics Settings'in *Always Included Shaders*
        // listesinde varsayılan olarak bulunur → build'de kesin paketlenir (çalışma anında
        // Shader.Find ile bulunan, hiçbir materyalde referanslanmayan shader STRIPLENİR ve tracer
        // sahada sessizce çizilmez). Vertex rengini de çarptığı için LineRenderer.startColor işler.
        private static readonly string[] ShaderCandidates =
        {
            "Sprites/Default",
            "Universal Render Pipeline/Unlit",
            "Unlit/Color",
        };

        /// <summary>Havuz düğümü; LineRenderer üretim anında önbelleklenir.</summary>
        private sealed class TracerNode
        {
            public LineRenderer Line;
            public float ExpireAt;
            public bool Active;
        }

        private readonly TracerNode[] _pool = new TracerNode[PoolSize];
        private int _nextNode;

        private Material _material;
        private bool _warnedNoShader;

        private static ShotTracer _shared;

        /// <summary>
        /// Tüm mermi izlerinin kullandığı TEK havuz; ilk istendiğinde kendini kurar ve
        /// <c>DontDestroyOnLoad</c> olur (harita değişiminde havuz + materyal yeniden kurulmasın).
        /// Sahneye konmaz, kimse referans bağlamaz — çağıran yalnız <c>ShotTracer.Shared.Play(…)</c>
        /// der.
        /// </summary>
        public static ShotTracer Shared
        {
            get
            {
                if (_shared == null)
                {
                    var go = new GameObject("[ShotTracer]");
                    DontDestroyOnLoad(go);
                    _shared = go.AddComponent<ShotTracer>();
                }

                return _shared;
            }
        }

        /// <summary>
        /// Bir tracer çizer. <paramref name="lifetime"/> ve <paramref name="width"/> eşyanın
        /// tanımından gelir; geçersiz (≤0) değerler güvenli tabana çekilir.
        /// Mesafe çok kısaysa hiç çizilmez (return false).
        /// </summary>
        public bool Play(Vector3 from, Vector3 to, Color color, float width, float lifetime)
        {
            if ((to - from).sqrMagnitude < MinTracerMeters * MinTracerMeters)
            {
                return false;
            }

            Material material = EnsureMaterial();
            if (material == null)
            {
                return false;
            }

            TracerNode node = TakeNode(material);
            if (node == null || node.Line == null)
            {
                return false;
            }

            float w = width > 0f ? width : 0.02f;
            float life = lifetime > 0f ? lifetime : 0.06f;

            node.Line.startWidth = w;
            node.Line.endWidth = w;
            node.Line.startColor = color;
            node.Line.endColor = color;
            node.Line.SetPosition(0, from);
            node.Line.SetPosition(1, to);
            node.Line.enabled = true;

            // Time.unscaledTime: maç duraklatılıp timeScale düşse bile tracer takılı kalmasın.
            node.ExpireAt = Time.unscaledTime + life;
            node.Active = true;
            return true;
        }

        /// <summary>Ömrü geçen çizgileri kapatır (havuz düğümü yok EDİLMEZ, yalnız gizlenir).</summary>
        private void Update()
        {
            float now = Time.unscaledTime;
            for (int i = 0; i < _pool.Length; i++)
            {
                TracerNode node = _pool[i];
                if (node == null || !node.Active || now < node.ExpireAt)
                {
                    continue;
                }

                node.Active = false;
                if (node.Line != null)
                {
                    node.Line.enabled = false;
                }
            }
        }

        /// <summary>Round-robin: sıradaki (en eski) düğümü döndürür; henüz yoksa tembel üretir.</summary>
        private TracerNode TakeNode(Material material)
        {
            TracerNode node = _pool[_nextNode];
            if (node == null || node.Line == null)
            {
                node = CreateNode(material);
                _pool[_nextNode] = node;
            }

            _nextNode = (_nextNode + 1) % PoolSize;
            return node;
        }

        private TracerNode CreateNode(Material material)
        {
            var go = new GameObject("[Tracer]");
            go.transform.SetParent(transform, false);

            var line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = material;
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.numCapVertices = 0;
            line.numCornerVertices = 0;
            line.textureMode = LineTextureMode.Stretch;
            line.alignment = LineAlignment.View;
            // Tracer bir ışık kaynağı değil, bir iz: gölge/ışık probu kapalı (Quest bütçesi).
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            line.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            line.enabled = false;

            return new TracerNode { Line = line };
        }

        /// <summary>
        /// Tüm tracer'ların PAYLAŞTIĞI materyal (renk LineRenderer'ın vertex rengiyle verilir —
        /// eşya başına materyal örneği açmak SRP batch'ini bölerdi).
        /// </summary>
        private Material EnsureMaterial()
        {
            if (_material != null)
            {
                return _material;
            }

            for (int i = 0; i < ShaderCandidates.Length; i++)
            {
                Shader shader = Shader.Find(ShaderCandidates[i]);
                if (shader != null)
                {
                    _material = new Material(shader) { name = "M_ShotTracer(runtime)" };
                    return _material;
                }
            }

            if (!_warnedNoShader)
            {
                _warnedNoShader = true;
                Debug.LogWarning(
                    "[ShotTracer] Tracer için shader bulunamadı (Sprites/Default dahil) — mermi izi " +
                    "çizilmeyecek. Graphics Settings > Always Included Shaders listesini kontrol et.");
            }

            return null;
        }

        private void OnDestroy()
        {
            if (_shared == this)
            {
                // Statik alan yıkılmış bileşene bağlı kalmaz: bir sonraki Play isteği havuzu
                // yeniden kurar (Play modundan çıkışta domain reload kapalıysa bu şart).
                _shared = null;
            }

            if (_material != null)
            {
                Destroy(_material);
                _material = null;
            }
        }
    }
}
