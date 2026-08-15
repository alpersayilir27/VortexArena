using UnityEditor;
using UnityEngine;
using VortexArena.Core.Combat;

namespace VortexArena.Core.Editor
{
    /// <summary>
    /// Kavrama noktalarının <b>Scene View tutamağı</b>: <c>WPN_*</c> prefabını (ya da sahnedeki
    /// örneğini) seçip soketi fareyle sürüklersin, değer doğrudan eşyanın
    /// <see cref="ItemDefinition"/> asset'ine (<c>WD_*.asset</c>) yazılır.
    /// <para>
    /// ⚠️ <b>Prefaba işaretçi düğüm KOYMAZ ve koymamalı.</b> Kavramanın tek yazılı kaynağı
    /// <c>WD_*.asset</c>'teki <see cref="ItemGripCapture"/> kaydıdır — ön kabza noktasını bir de
    /// prefab çocuğu olarak tutmak aynı noktayı iki yerde tarif etmek olurdu ve uzak avatar
    /// (<c>RemoteAvatar</c>) ile kavrama kapısı (<c>ItemGripSockets.Filter</c>) asset'i okuduğu
    /// için ikisi sessizce sapardı. Bu araç, o eski akışın (<c>GripSocket_*</c> işaretçileri)
    /// ergonomisini veriyi bölmeden geri getirir: tutamak sahnededir, veri tekildir.
    /// </para>
    /// <para>
    /// ⚠️ <b>Bu araç VR yakalamasının yerine GEÇMEZ, tamamlar.</b> Parmakların kabzaya nasıl
    /// oturduğunu yalnız gözlük söyler (<c>Tools &gt; VortexArena &gt; Development &gt; Dev</c> →
    /// rol <c>weapon</c>). Tutamak, yakalanmış bir kaydı santim mertebesinde düzeltmek ve hiç
    /// yakalanmamış bir silaha makul bir başlangıç vermek içindir; ikisi de aynı dört alanı yazar.
    /// </para>
    /// <para>
    /// ⚠️ Tutamak yalnız <b>konumu</b> sürer, kaydın dönüşü korunur: eşyanın eldeki dönüşü zaten
    /// kimliktir (<see cref="ItemDefinition.PrimaryGripRotation"/>) ve bileğin açısı da
    /// kumandayla serbest döner — sürüklenerek "hangi açı" sorusuna cevap üretmek, olmayan bir
    /// ayarı varmış gibi gösterirdi.
    /// </para>
    /// </summary>
    [CustomEditor(typeof(ItemGripSockets))]
    internal sealed class ItemGripSocketsEditor : UnityEditor.Editor
    {
        // Seçim KİŞİSELDİR ve EditorPrefs'te durur: hangi eli düzenlediğin bir asset değeri değil,
        // o anki çalışma kipin — bileşene serialize edilseydi her tutamak kullanımı prefabı
        // kirletir ve commit'e sızardı (dev penceresindeki rol seçimiyle aynı gerekçe).
        private const string HandPrefKey = "VortexArena.GripHandle.RightHand";

        // ⚠️ Renkler burada TANIMLANMAZ, ItemGripSockets'ten alınır (ReadyColor/HoverColor):
        // sahnedeki halka oyundakiyle aynı görünsün diye ikinci bir palet açmak, biri değişince
        // ötekinin sessizce sapması demektir. Yalnız "yazılmamış" durumunun oyunda karşılığı yok —
        // tek yerel renk odur.

        /// <summary>Hiç yakalanmamış kaydın rengi: nokta eşyanın orijininde durur ve bu bir
        /// KURULUM eksiğidir, ince ayar meselesi değil.</summary>
        private static readonly Color MissingColor = new Color(1f, 0.35f, 0.25f, 0.9f);

        private static bool RightHand
        {
            get => EditorPrefs.GetBool(HandPrefKey, true);
            set => EditorPrefs.SetBool(HandPrefKey, value);
        }

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var sockets = (ItemGripSockets)target;
            ItemDefinition def = sockets.ResolvedDefinition;

            EditorGUILayout.Space();

            if (def == null)
            {
                EditorGUILayout.HelpBox(
                    "Tanım yok (ne Weapon.definition ne yedek alan) — soket çizilmez, kavrama kapısı " +
                    "herkese AÇIK kalır ve düzenlenecek bir kayıt da yoktur.", MessageType.Error);
                return;
            }

            EditorGUILayout.LabelField("Kavrama tutamağı", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.ObjectField("Yazılacak kayıt", def, typeof(ItemDefinition), false);
            }

            if (!def.IsTwoHanded)
            {
                EditorGUILayout.HelpBox(
                    "Eşya tek elli (HoldMode = OneHand): ön kabza soketi hiç açılmaz, bu yüzden " +
                    "düzenlenecek bir şey yok. Ana kabza gözlükle yakalanır (Dev penceresi → rol 'weapon').",
                    MessageType.Info);
                return;
            }

            RightHand = GUILayout.Toolbar(RightHand ? 0 : 1, new[] { "SAĞ el", "SOL el" }) == 0;

            DrawStatus(def);
            DrawPointField(sockets, def);
            DrawClearButton(def);

            EditorGUILayout.HelpBox(
                "Noktayı Scene View'daki yeşil halkanın tutamağından sürükle — değer anında " +
                def.name + " asset'ine yazılır (Ctrl+Z geri alır, Ctrl+S kaydeder). " +
                "Parmak duruşunu bu araç ölçmez: onun yeri gözlüktür (Dev penceresi → rol 'weapon').",
                MessageType.Info);
        }

        /// <summary>
        /// Dört kaydın yazılıp yazılmadığı. ⚠️ <see cref="ItemDefinition.HasGrip"/> sorulur,
        /// okuma yolu DEĞİL: okuma öteki elin kaydına düşer, yani "yazılmış mı" sorusuna
        /// okuma yoluyla bakmak eksik eli sessizce dolu gösterirdi.
        /// </summary>
        private static void DrawStatus(ItemDefinition def)
        {
            EditorGUILayout.LabelField(
                "Ana:  sağ " + Mark(def.HasGrip(GripSocketKind.Primary, true)) +
                "   sol " + Mark(def.HasGrip(GripSocketKind.Primary, false)) +
                "        Ön:  sağ " + Mark(def.HasGrip(GripSocketKind.Secondary, true)) +
                "   sol " + Mark(def.HasGrip(GripSocketKind.Secondary, false)));
        }

        private static string Mark(bool captured)
        {
            return captured ? "✔" : "—";
        }

        private void DrawPointField(ItemGripSockets sockets, ItemDefinition def)
        {
            GripSocketKind kind = ActiveKind(def);
            bool right = RightHand;

            if (!def.HasGrip(kind, right))
            {
                EditorGUILayout.HelpBox(
                    def.HasGrip(kind, !right)
                        ? "Bu el için kayıt YOK — gösterilen nokta ÖTEKİ elin kaydıdır. Sürüklersen " +
                          "bu ele ait kayıt olarak yazılır."
                        : "Bu kavrama hiç yakalanmamış — nokta eşyanın orijininde duruyor. Sürükleyerek " +
                          "kabzanın üstüne getir ya da gözlükle yakala.",
                    MessageType.Warning);
            }

            Vector3 current = LocalPoint(def, kind, right);

            EditorGUI.BeginChangeCheck();
            Vector3 next = EditorGUILayout.Vector3Field("Nokta (eşyaya göre, m)", current);
            if (EditorGUI.EndChangeCheck())
            {
                WritePoint(def, kind, right, next);
            }

            EditorGUILayout.LabelField("Dünya konumu",
                ItemGripSockets.ItemLocalToWorld(sockets.transform, current).ToString("F3"));
        }

        private void DrawClearButton(ItemDefinition def)
        {
            GripSocketKind kind = ActiveKind(def);
            bool right = RightHand;

            using (new EditorGUI.DisabledScope(!def.HasGrip(kind, right)))
            {
                if (GUILayout.Button("Seçili kaydı sil (yakalanmamış yap)"))
                {
                    Undo.RecordObject(def, "Kavrama kaydı silindi");
                    def.EditorClearGrip(kind, right);
                    EditorUtility.SetDirty(def);
                }
            }
        }

        // ⚠️ Çizim OnSceneGUI'de DEĞİL, SceneView.duringSceneGui'de yapılır ve bu bilinçlidir:
        // OnSceneGUI editör takipçisine bağlıdır (yanlış obje seçiliyken, Inspector kilitliyken ya
        // da başka bir bileşenin editörü araya girdiğinde sessizce hiç koşmaz) — belirtisi "tutamak
        // hiç çıkmıyor" olur ve hata vermez. duringSceneGui sahne penceresinin kendi olayıdır,
        // bileşen seçili olduğu sürece koşar.
        private void OnEnable()
        {
            SceneView.duringSceneGui -= DrawSceneHandles;
            SceneView.duringSceneGui += DrawSceneHandles;
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= DrawSceneHandles;
        }

        private void DrawSceneHandles(SceneView view)
        {
            if (target == null)
            {
                return;
            }

            var sockets = (ItemGripSockets)target;
            ItemDefinition def = sockets.ResolvedDefinition;

            // Tek elli eşyada ön kabza soketi hiç açılmaz, bu araç da yalnız onu düzenler → sahneye
            // hiçbir şey çizilmez.
            if (def == null || !def.IsTwoHanded)
            {
                return;
            }

            // ⚠️ Handles durumu GLOBAL statiktir ve Unity onu editörler arasında sıfırlamaz: aynı
            // objedeki başka bir bileşenin editörü (ISDK'nın HandGrabInteractable'ı sahneye hayalet
            // el çiziyor) matrisi/zTest'i değiştirip bırakırsa tutamak metrelerce ötede ya da
            // geometrinin içinde kalır. Her karede kendi uzayımızı kuruyoruz.
            Handles.matrix = Matrix4x4.identity;
            Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;

            Transform item = sockets.transform;
            GripSocketKind kind = ActiveKind(def);
            bool right = RightHand;

            // Kamera duringSceneGui'de olayın penceresinden alınır — Camera.current bu geri çağrının
            // her olay tipinde dolu değildir ve boşken halkalar keyfi bir düzleme çizilirdi.
            Vector3 viewNormal = view != null && view.camera != null ? view.camera.transform.forward : Vector3.up;

            DrawPassiveSockets(item, def, viewNormal, kind, right);

            Vector3 local = LocalPoint(def, kind, right);
            Vector3 world = ItemGripSockets.ItemLocalToWorld(item, local);
            float radius = kind == GripSocketKind.Secondary ? def.SecondaryGripRadius : def.PrimaryGripRadius;

            // ⚠️ ASIL gösterge oyundaki halkanın AYNISIDIR (aynı yarıçap, aynı renk): ayarlarken
            // gördüğün şey ile oyuncunun gözlükte gördüğü şey aynı olsun. Kavrama YARIÇAPI (12 cm)
            // bunun kat kat üstündedir ve oyunda HİÇ çizilmez — o yüzden aşağıda yalnız soluk bir
            // referans halkası olarak duruyor, göstergenin kendisi olarak değil.
            Handles.color = def.HasGrip(kind, right) ? ItemGripSockets.ReadyColor : MissingColor;
            Handles.DrawWireDisc(world, viewNormal, ItemGripSockets.RingRadius * ItemGripSockets.ReadyScale, 3f);

            Handles.color = new Color(0.5f, 0.5f, 0.5f, 0.25f);
            Handles.DrawWireDisc(world, viewNormal, radius);

            Handles.Label(world + Vector3.up * (ItemGripSockets.RingRadius * ItemGripSockets.ReadyScale + 0.02f),
                Caption(def, kind, right));

            EditorGUI.BeginChangeCheck();

            // Tutamak eşyanın eksenlerinde döner (Local pivot): namlu doğrultusunda santim eklemek
            // dünya eksenlerinde uğraşmaktan çok daha okunaklı.
            Quaternion handleRotation = Tools.pivotRotation == PivotRotation.Local ? item.rotation : Quaternion.identity;
            Handles.color = Color.white;
            Vector3 moved = Handles.PositionHandle(world, handleRotation);

            if (EditorGUI.EndChangeCheck())
            {
                WritePoint(def, kind, right, ItemGripSockets.WorldToItemLocal(item, moved));
                Repaint(); // Inspector'daki sayı alanı sürükleme boyunca canlı kalsın
            }
        }

        /// <summary>Düzenlenmeyen üç kaydı soluk çizer — düzenlenen noktanın ötekilere göre nerede
        /// durduğu görünmezse "sağ el kabzada, sol el havada" gibi sapmalar fark edilmez.</summary>
        private static void DrawPassiveSockets(Transform item, ItemDefinition def, Vector3 normal,
            GripSocketKind activeKind, bool activeRight)
        {
            // ⚠️ ANA KABZA HİÇ ÇİZİLMEZ (bkz. ActiveKind): düzenlenemeyen bir noktayı göstermek,
            // sahnede sürüklenebilir sanılan ama sürüklenmeyen bir işaret bırakırdı.
            DrawPassive(item, def, normal, GripSocketKind.Secondary, true, activeKind, activeRight);
            DrawPassive(item, def, normal, GripSocketKind.Secondary, false, activeKind, activeRight);
        }

        private static void DrawPassive(Transform item, ItemDefinition def, Vector3 normal, GripSocketKind kind,
            bool right, GripSocketKind activeKind, bool activeRight)
        {
            if (kind == activeKind && right == activeRight)
            {
                return; // düzenlenen nokta ayrıca çiziliyor
            }

            if (!def.HasGrip(kind, right))
            {
                return; // yazılmamış kayıt orijinde yığılır; dördünü birden çizmek çöp gösterir
            }

            Vector3 world = ItemGripSockets.ItemLocalToWorld(item, LocalPoint(def, kind, right));

            // Düzenlenmeyen noktalar oyundaki HOVER halkasıdır (mavi, normal boy) — düzenlenen
            // nokta ise READY halkası (yeşil, %35 büyük). Yani sahnedeki ayrım, oyuncunun gözlükte
            // gördüğü ayrımın aynısı: ikinci bir görsel dil öğrenmek gerekmiyor.
            Handles.color = new Color(ItemGripSockets.HoverColor.r, ItemGripSockets.HoverColor.g,
                ItemGripSockets.HoverColor.b, 0.55f);
            Handles.DrawWireDisc(world, normal, ItemGripSockets.RingRadius, 2f);
            Handles.Label(world + Vector3.up * (ItemGripSockets.RingRadius + 0.015f), Caption(def, kind, right));
        }

        private static string Caption(ItemDefinition def, GripSocketKind kind, bool right)
        {
            string name = kind == GripSocketKind.Secondary ? "Ön kabza" : "Ana kabza";
            string hand = right ? "sağ" : "sol";
            return def.HasGrip(kind, right) ? name + " (" + hand + ")" : name + " (" + hand + ") — YAZILMAMIŞ";
        }

        /// <summary>
        /// Bu araç <b>yalnız ÖN KABZAYI</b> düzenler ve ana kabzayı sahnede hiç çizmez.
        /// <para>Gerekçe: ana kabza silahın elde nasıl duracağını belirler (eşyanın pozu ondan
        /// türetilir, §6.6) — orada bir kaç santimlik "gözle iyi duruyor" düzeltmesi silahı elden
        /// koparır; o kaydın yeri gözlüktür. Ön kabza ise yalnız ikinci elin nereye konacağını
        /// söyler, yani gözle ayarlanmaya elverişli olan tek kavramadır.</para>
        /// </summary>
        private static GripSocketKind ActiveKind(ItemDefinition def)
        {
            return GripSocketKind.Secondary;
        }

        /// <summary>
        /// Kavrama noktasının EŞYAYA göre yerel konumu — soketin/gizmonun okuduğu değerin aynısı.
        /// ⚠️ Ana kabzada <c>PrimaryGripPointOnItem</c> okunur, <c>PrimaryGripPosition</c> DEĞİL:
        /// ikincisi eşyanın ELE göre konumudur (işareti ters) ve tutamağı aynaya düşürürdü.
        /// </summary>
        private static Vector3 LocalPoint(ItemDefinition def, GripSocketKind kind, bool right)
        {
            return kind == GripSocketKind.Secondary
                ? def.SecondaryGripPosition(right)
                : def.PrimaryGripPointOnItem(right);
        }

        /// <summary>
        /// Noktayı asset'e yazar. ⚠️ Kaydın DÖNÜŞÜ korunur (yakalamadan gelen bilek açısı), yalnız
        /// konum değişir — tutamağın cevaplayabildiği soru "el eşyanın neresinde" sorusudur.
        /// </summary>
        private static void WritePoint(ItemDefinition def, GripSocketKind kind, bool right, Vector3 itemLocal)
        {
            Quaternion rotation = def.GetGrip(kind, right).Rotation;

            Undo.RecordObject(def, "Kavrama noktası");
            def.EditorSetGrip(kind, right, new Pose(itemLocal, rotation));
            EditorUtility.SetDirty(def);
        }
    }
}
