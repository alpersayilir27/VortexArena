using System.Collections.Generic;
using Oculus.Interaction.HandGrab;
using Oculus.Interaction.HandGrab.Visuals;
using Oculus.Interaction.Input;
using UnityEngine;
using VortexArena.Core.Combat;
using VortexArena.Core.Player;

namespace VortexArena.Core.Editor
{
    /// <summary>
    /// Kavrama Pozu Stüdyosu'nun sahneye koyduğu <b>tek elin</b> ayar yüzeyi: hangi kavrama
    /// noktasına ait olduğu, parmak kıvrımı/açıklığı ve parmak serbestliği burada durur.
    /// <para>
    /// ⚠️ <b>Bu bileşen bilerek EDİTÖR asmdef'indedir</b> (<c>VortexArena.Core.Editor</c>). Normalde
    /// bir <see cref="MonoBehaviour"/>'ın editör derlemesinde olması tehlikelidir (sahneye/prefaba
    /// girerse build'de "missing script" olur); burada tehlike yok çünkü bu obje hiçbir zaman
    /// kaydedilmez: stüdyo elleri <see cref="HideFlags.DontSave"/> ile ve prefab içeriğinin DIŞINDA,
    /// prefab stage sahnesinin ayrı bir kökü olarak kurar. Runtime asmdef'ine konsaydı, yalnız
    /// editörde anlamı olan bir bileşen oyunun derlemesine sızmış olurdu.
    /// </para>
    /// <para>
    /// ⚠️ <b>Slider'lar KOLAYLIKTIR, doğruluk kaynağı DEĞİLDİR.</b> Kaydet her zaman kemiklerin o
    /// andaki gerçek transformunu okur (<see cref="HandPuppet.CopyCachedJoints"/>); kullanıcı
    /// parmakları Hierarchy'den elle bükerse o duruş aynen kaydedilir. Slider'ların kaydedilen bir
    /// karşılığı yoktur — olsaydı aynı duruşun iki tarifi olurdu ve ikisi sessizce sapardı.
    /// </para>
    /// <para>
    /// ⚠️ <b>Bükülme ekseni SABİT YAZILMAZ, ölçülür</b> (<see cref="HandFingerRig"/> ile aynı
    /// gerekçe): eksen = avuç normali × kemik yönü ve ikisi de elin <b>bind</b> duruşunda okunur.
    /// ISDK el modeli değiştiğinde burada tek satır değişmesin diye.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    internal sealed class GripHandAuthoring : MonoBehaviour
    {
        private const string LOG = "[GripPoseStudio]";

        /// <summary>Parmak sayısı — ISDK sabiti (<c>HandFinger</c> enum'u beş elemanlıdır).</summary>
        internal const int FingerCount = 5;

        /// <summary>Tam kıvrımda eklem başına derece; kaynak <see cref="HandFingerRig"/>'in aynı
        /// tablosudur (başparmak anatomik olarak daha az kıvrılır).</summary>
        private static readonly float[] FingerMaxAngles = { 50f, 60f, 40f };
        private static readonly float[] ThumbMaxAngles = { 25f, 35f, 30f };

        /// <summary>Tam açıklıkta derece (avuç normali etrafında).</summary>
        private const float SpreadMaxAngle = 12f;

        /// <summary>Bir parmağın kullanıcıya açılan ayarları.</summary>
        [System.Serializable]
        internal sealed class Finger
        {
            [Range(0f, 1f)] public float Curl;
            [Range(-1f, 1f)] public float Spread;

            /// <summary>Eklem başına ince ayar (derece), kıvrım ekseninde. Dizi uzunluğu o parmağın
            /// eklem sayısıdır ve <see cref="Resolve"/>'da kurulur.</summary>
            public float[] FineDegrees = new float[0];

            /// <summary>Kaydedilecek serbestlik (<see cref="HandPose.FingersFreedom"/>).</summary>
            public JointFreedom Freedom = JointFreedom.Locked;
        }

        [SerializeField] private GripSocketKind _kind;
        [SerializeField] private bool _rightHand = true;
        [SerializeField] private HandPuppet _puppet;

        [SerializeField] private Finger[] _fingers = CreateDefaultFingers();

        // ⚠️ Aşağıdaki diziler [SerializeField]'dir ve bilerek öyle: obje DontSave olduğu için diske
        // hiç yazılmaz ama DOMAIN RELOAD'ı (script derlemesi) yaşar. Alanlar serialize edilmeseydi
        // her derlemeden sonra baz/eksen kaybolur, slider'lar sessizce hiçbir şey yapmazdı.
        [SerializeField, HideInInspector] private Transform[] _joints;
        [SerializeField, HideInInspector] private Quaternion[] _baseline;
        [SerializeField, HideInInspector] private Vector3[] _bendAxis;
        [SerializeField, HideInInspector] private Vector3[] _spreadAxis;

        /// <summary>
        /// Elin <b>bind</b> duruşunda ölçülmüş anatomik bazı — <c>WD_*</c> kavrama alanlarını
        /// türetirken kullanılır (<see cref="HandGripConvention.Correction"/>).
        /// <para>⚠️ Kayıt anında YENİDEN ÖLÇÜLMEZ: o an el poza sokulmuş durumdadır ve bükülmüş bir
        /// elden ölçülen baz o duruşu içerir, yani kavrama alanları kalıcı olarak yanlış çıkardı.
        /// Ölçüm bir kez, el kurulurken yapılır.</para>
        /// </summary>
        [SerializeField, HideInInspector] private Quaternion _bindBoneBasis = Quaternion.identity;
        [SerializeField, HideInInspector] private bool _hasBindBoneBasis;

        internal GripSocketKind Kind => _kind;
        internal bool RightHand => _rightHand;
        internal HandPuppet Puppet => _puppet;
        internal Handedness Handedness => _rightHand ? Handedness.Right : Handedness.Left;
        internal Finger[] Fingers => _fingers;
        internal bool HasBindBoneBasis => _hasBindBoneBasis;
        internal Quaternion BindBoneBasis => _bindBoneBasis;

        // ------------------------------------------------------------------------- kurulum

        /// <summary>
        /// Eli tanıtır ve iskeletini çözer. ⚠️ <b>El HENÜZ POZA SOKULMADAN, bind duruşundayken</b>
        /// çağrılmalıdır: hem anatomik baz hem bükülme eksenleri buradan ölçülüyor.
        /// </summary>
        internal void Resolve(HandPuppet puppet, GripSocketKind kind, bool rightHand)
        {
            _puppet = puppet;
            _kind = kind;
            _rightHand = rightHand;
            _fingers = CreateDefaultFingers();

            HandJointId[] ids = FingersMetadata.HAND_JOINT_IDS;
            _joints = new Transform[ids.Length];
            _baseline = new Quaternion[ids.Length];
            _bendAxis = new Vector3[ids.Length];
            _spreadAxis = new Vector3[ids.Length];

            if (puppet == null || puppet.JointMaps == null)
            {
                return;
            }

            List<HandJointMap> maps = puppet.JointMaps;
            for (int i = 0; i < ids.Length; i++)
            {
                HandJointId wanted = ids[i];
                HandJointMap map = maps.Find(m => m != null && m.id == wanted);
                _joints[i] = map != null ? map.transform : null;
                _baseline[i] = _joints[i] != null ? _joints[i].localRotation : Quaternion.identity;
            }

            MeasureAxes();
            SizeFineArrays();
        }

        /// <summary>
        /// Anatomik bazı ve parmak eksenlerini bind duruşundan ölçer.
        /// <para>Ölçülemezse eksenler sıfır kalır: slider'lar hiçbir şey yapmaz ama kullanıcı
        /// kemikleri elle bükebilir — yarım çalışan bir slider'dan iyidir. Kaydet tarafı bazı
        /// bulamayınca <c>WD_*</c> alanlarını yazmaz ve bunu açıkça söyler.</para>
        /// </summary>
        private void MeasureAxes()
        {
            Transform wrist = transform;
            Transform middle = Joint(HandJointId.HandMiddle1);
            Transform thumb = Joint(HandJointId.HandThumb1);

            _hasBindBoneBasis = HandGripConvention.TryMeasureBoneBasis(
                wrist, middle, thumb, _rightHand, out _bindBoneBasis);

            if (!_hasBindBoneBasis)
            {
                Debug.LogWarning($"{LOG} {name}: el modelinin anatomik bazı ölçülemedi (orta/baş " +
                                 "parmak boğumu yok) — parmak slider'ları ve WD kavrama alanları " +
                                 "bu el için çalışmaz.", this);
                return;
            }

            Vector3 palmNormalWorld = wrist.TransformDirection(_bindBoneBasis * Vector3.up);
            if (palmNormalWorld.sqrMagnitude < 1e-8f)
            {
                return;
            }

            palmNormalWorld = palmNormalWorld.normalized;

            for (int finger = 0; finger < FingerCount; finger++)
            {
                HandJointId[] chain = FingersMetadata.FINGER_TO_JOINTS[finger];
                for (int j = 0; j < chain.Length; j++)
                {
                    int index = FingersMetadata.HandJointIdToIndex(chain[j]);
                    if (index < 0 || _joints[index] == null || _joints[index].parent == null)
                    {
                        continue;
                    }

                    Transform bone = _joints[index];
                    Transform next = NextInChain(chain, j, index);
                    if (next == null)
                    {
                        continue;
                    }

                    Vector3 direction = next.position - bone.position;
                    if (direction.sqrMagnitude < 1e-8f)
                    {
                        continue;
                    }

                    // ⚠️ Sıra Cross(avuç normali, kemik yönü)'dür ve TERSİ DEĞİLDİR: bu eksen
                    // etrafında pozitif dönüş parmak ucunu avucun İÇİNE taşır. Ters yazılırsa
                    // parmaklar el sırtına doğru kırılır (HandFingerRig'deki aynı kural).
                    Vector3 axisWorld = Vector3.Cross(palmNormalWorld, direction.normalized);
                    if (axisWorld.sqrMagnitude < 1e-8f)
                    {
                        continue;
                    }

                    // Eksenler EBEVEYN çerçevesinde saklanır: menteşe ekseni eklemde sabittir ve
                    // ebeveyn döndükçe onunla birlikte döner — tam olarak bir parmak eklemi gibi.
                    _bendAxis[index] = bone.parent.InverseTransformDirection(axisWorld).normalized;
                    _spreadAxis[index] = bone.parent.InverseTransformDirection(palmNormalWorld).normalized;
                }
            }
        }

        /// <summary>Zincirdeki bir sonraki eklem; son eklemde yön ölçmek için ilk çocuğa düşülür
        /// (parmak UCU <see cref="FingersMetadata.HAND_JOINT_IDS"/>'de yoktur).</summary>
        private Transform NextInChain(HandJointId[] chain, int position, int currentIndex)
        {
            if (position + 1 < chain.Length)
            {
                int nextIndex = FingersMetadata.HandJointIdToIndex(chain[position + 1]);
                if (nextIndex >= 0 && _joints[nextIndex] != null)
                {
                    return _joints[nextIndex];
                }
            }

            Transform bone = _joints[currentIndex];
            return bone != null && bone.childCount > 0 ? bone.GetChild(0) : null;
        }

        private Transform Joint(HandJointId id)
        {
            int index = FingersMetadata.HandJointIdToIndex(id);
            return index >= 0 && _joints != null && index < _joints.Length ? _joints[index] : null;
        }

        /// <summary>
        /// Elin O ANKİ duruşunu slider'ların sıfır noktası yapar (poz yüklendikten ya da aynalandıktan
        /// sonra çağrılır) ve slider'ları sıfırlar.
        /// </summary>
        internal void CaptureBaseline()
        {
            if (_joints == null)
            {
                return;
            }

            for (int i = 0; i < _joints.Length; i++)
            {
                if (_joints[i] != null)
                {
                    _baseline[i] = _joints[i].localRotation;
                }
            }

            for (int f = 0; f < _fingers.Length; f++)
            {
                _fingers[f].Curl = 0f;
                _fingers[f].Spread = 0f;
                for (int j = 0; j < _fingers[f].FineDegrees.Length; j++)
                {
                    _fingers[f].FineDegrees[j] = 0f;
                }
            }
        }

        // ------------------------------------------------------------------------- uygulama

        /// <summary>Slider'ların değerini kemiklere yazar (baz duruşun ÜZERİNE).</summary>
        internal void ApplyFingers()
        {
            if (_joints == null)
            {
                return;
            }

            SizeFineArrays();

            for (int finger = 0; finger < FingerCount && finger < _fingers.Length; finger++)
            {
                Finger settings = _fingers[finger];
                HandJointId[] chain = FingersMetadata.FINGER_TO_JOINTS[finger];
                float[] maxAngles = finger == 0 ? ThumbMaxAngles : FingerMaxAngles;
                int drivable = 0;
                bool spreadApplied = false;

                for (int j = 0; j < chain.Length; j++)
                {
                    int index = FingersMetadata.HandJointIdToIndex(chain[j]);
                    if (index < 0 || _joints[index] == null)
                    {
                        continue;
                    }

                    // Ekseni ölçülememiş eklem SÜRÜLMEZ: sıfır eksenli bir AngleAxis sessizce
                    // kimlik döndürüp bazı parmakları donuk bırakırdı ve sebebi görünmezdi. Kullanıcı
                    // o eklemi Hierarchy'den yine elle bükebilir.
                    if (_bendAxis[index].sqrMagnitude < 1e-8f)
                    {
                        continue;
                    }

                    float angle = 0f;

                    // ⚠️ Metakarp eklemleri (OpenXR dalında Index0/Middle0/Ring0) kıvrılmaz: ISDK
                    // onları bileğe sabit sayıyor (HAND_JOINT_CAN_MOVE) ve döndürülürse avuç
                    // kendi içine çöker. Kapı SABİT YAZILMAZ, tablodan okunur — el dalı değişince
                    // burada satır değişmesin.
                    if (FingersMetadata.HAND_JOINT_CAN_MOVE[index])
                    {
                        int slot = Mathf.Min(drivable, maxAngles.Length - 1);
                        angle = settings.Curl * maxAngles[slot];
                        drivable++;
                    }

                    if (j < settings.FineDegrees.Length)
                    {
                        angle += settings.FineDegrees[j];
                    }

                    Quaternion rotation = Quaternion.AngleAxis(angle, _bendAxis[index]);

                    if (!spreadApplied && FingersMetadata.HAND_JOINT_CAN_SPREAD[index] &&
                        _spreadAxis[index].sqrMagnitude > 1e-8f)
                    {
                        rotation = Quaternion.AngleAxis(settings.Spread * SpreadMaxAngle,
                                       _spreadAxis[index]) * rotation;
                        spreadApplied = true;
                    }

                    _joints[index].localRotation = rotation * _baseline[index];
                }
            }
        }

        /// <summary>Slider'ları sıfırlar ve eli baz duruşuna geri koyar.</summary>
        internal void ResetFingers()
        {
            for (int f = 0; f < _fingers.Length; f++)
            {
                _fingers[f].Curl = 0f;
                _fingers[f].Spread = 0f;
                for (int j = 0; j < _fingers[f].FineDegrees.Length; j++)
                {
                    _fingers[f].FineDegrees[j] = 0f;
                }
            }

            ApplyFingers();
        }

        /// <summary>Aynalama sonrası karşı elin serbestlik ayarını kaynaktan kopyalar.</summary>
        internal void CopyFreedomFrom(GripHandAuthoring other)
        {
            if (other == null)
            {
                return;
            }

            for (int f = 0; f < FingerCount && f < _fingers.Length && f < other._fingers.Length; f++)
            {
                _fingers[f].Freedom = other._fingers[f].Freedom;
            }
        }

        /// <summary>Kaydedilecek serbestlik dizisi (<see cref="HandPose.FingersFreedom"/> sırasında).</summary>
        internal void WriteFreedom(JointFreedom[] target)
        {
            for (int f = 0; f < target.Length && f < _fingers.Length; f++)
            {
                target[f] = _fingers[f].Freedom;
            }
        }

        /// <summary>
        /// Varsayılan serbestlik: ISDK'nın kendi varsayılanı, <b>işaret parmağı hariç</b>.
        /// <para>⚠️ İşaret <see cref="JointFreedom.Free"/>'dir çünkü kilitli bir işaret parmağı
        /// ateş ederken kıpırdamaz, yani oyuncu tetiği çektiğini elinde göremez
        /// (<c>HandGripPoser</c>'daki aynı gerekçe). Tüfek dışı bir eşyada elle değiştirilir.</para>
        /// </summary>
        private static Finger[] CreateDefaultFingers()
        {
            JointFreedom[] defaults = FingersMetadata.DefaultFingersFreedom();
            var fingers = new Finger[FingerCount];

            for (int f = 0; f < FingerCount; f++)
            {
                fingers[f] = new Finger
                {
                    Freedom = f == (int)HandFinger.Index ? JointFreedom.Free : defaults[f]
                };
            }

            return fingers;
        }

        /// <summary>İnce ayar dizilerini parmağın eklem sayısına büyütür (kısaltmaz — kullanıcının
        /// girdiği değer eklem sayısı değişmedikçe korunur).</summary>
        private void SizeFineArrays()
        {
            for (int f = 0; f < FingerCount && f < _fingers.Length; f++)
            {
                int wanted = FingersMetadata.FINGER_TO_JOINTS[f].Length;
                if (_fingers[f].FineDegrees == null || _fingers[f].FineDegrees.Length != wanted)
                {
                    var resized = new float[wanted];
                    if (_fingers[f].FineDegrees != null)
                    {
                        for (int j = 0; j < resized.Length && j < _fingers[f].FineDegrees.Length; j++)
                        {
                            resized[j] = _fingers[f].FineDegrees[j];
                        }
                    }

                    _fingers[f].FineDegrees = resized;
                }
            }
        }
    }
}
