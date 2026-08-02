using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace VortexArena.Core.Combat
{
    /// <summary>
    /// Elden çıkan silahın <b>yerinde kalan kopyası</b>: yalnız mesh + dissolve materyali taşır,
    /// <c>_Dissolve</c> 0→1 sürülür ve kendini yok eder. <see cref="WeaponDissolve"/> üretir.
    /// <para>
    /// <b>Neden ayrı bir obje:</b> silah bırakıldığı anda yok ediliyor (<c>Destroy</c> — FFA'nın
    /// tek kullanımlık silahı) ya da gizleniyor (<c>SetActive(false)</c> — çerçeve klonu), yani
    /// kaybolma geçişinin silahın KENDİ üstünde koşacak karesi yok. Kopya bağımsız yaşadığı için
    /// silahın ne zaman öldüğü onu ilgilendirmez.
    /// </para>
    /// <para>
    /// <b>Neden <see cref="WeaponGranter"/> değiştirilmedi:</b> alternatif, silahın yok
    /// edilmesini/gizlenmesini efekt süresi kadar ertelemekti — ama o kapıdan "silah elde mi"
    /// sorusunun TÜM cevapları geçiyor (ölüm, harita değişimi, kural değişimi, tur başı dolum) ve
    /// her birine bir gecikme sokmak görsel bir efekt için maç kurallarını kırılganlaştırırdı.
    /// </para>
    /// <para>
    /// ⚠️ Kopya <b>hareket etmez</b> — silahın bırakıldığı andaki dünya pozunda çözülür. Eli takip
    /// etseydi "silah hâlâ elimde" der, oysa oyuncunun eli çoktan boşalmıştır.
    /// </para>
    /// </summary>
    public class WeaponDissolveGhost : MonoBehaviour
    {
        private static readonly int DissolveId = Shader.PropertyToID("_Dissolve");

        private readonly List<Renderer> _renderers = new List<Renderer>();
        private readonly List<MaterialPropertyBlock> _blocks = new List<MaterialPropertyBlock>();

        /// <summary>Bir parça ekler. Property block parça BAŞINA gelir: silahın gövdesi ile
        /// dürbün camı ayrı materyaller, yani ayrı albedolar.</summary>
        public void AddPart(Renderer renderer, MaterialPropertyBlock block)
        {
            if (renderer == null || block == null)
            {
                return;
            }

            _renderers.Add(renderer);
            _blocks.Add(block);
        }

        /// <summary>Kaybolmayı başlatır. Parça yoksa (ya da süre sıfırsa) kopya hemen ölür —
        /// sahnede görünmez bir obje bırakmaz.</summary>
        public void Begin(float seconds)
        {
            if (_renderers.Count == 0 || seconds <= 0f)
            {
                Destroy(gameObject);
                return;
            }

            StartCoroutine(Fade(seconds));
        }

        private IEnumerator Fade(float seconds)
        {
            float elapsed = 0f;

            while (elapsed < seconds)
            {
                elapsed += Time.deltaTime;

                // Belirmenin aynası: orada 1→0, burada 0→1.
                float k = Mathf.SmoothStep(0f, 1f, elapsed / seconds);
                for (int i = 0; i < _renderers.Count; i++)
                {
                    if (_renderers[i] == null)
                    {
                        continue;
                    }

                    _blocks[i].SetFloat(DissolveId, k);
                    _renderers[i].SetPropertyBlock(_blocks[i]);
                }

                yield return null;
            }

            Destroy(gameObject);
        }
    }
}
