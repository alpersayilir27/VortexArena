using UnityEngine;

namespace VortexArena.Core.Combat
{
    /// <summary>
    /// Uzak atışların mermi izi (tracer): namludan vuruş noktasına kısa süre çizilen çizgi.
    /// <see cref="RemoteShotFx"/> tarafından kurulur ve beslenir (§6.4/6.5) — sahnede DURMAZ,
    /// kendi başına da bir şey dinlemez: olay çözümü tek yerde (RemoteShotFx) kalsın.
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

        /// <summary>
        /// Tracer havuzunu <paramref name="parent"/> altında (RemoteShotFx'in DDOL kökü) kurar.
        /// Havuz sahne geçişinde yok olmasın diye kök DDOL olmak zorundadır — bunu çağıran sağlar.
        /// </summary>
        public static ShotTracer Create(Transform parent)
        {
            var go = new GameObject("[ShotTracer]");
            go.transform.SetParent(parent, false);
            return go.AddComponent<ShotTracer>();
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
            if (_material != null)
            {
                Destroy(_material);
                _material = null;
            }
        }
    }
}
