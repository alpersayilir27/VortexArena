using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;
using VortexArena.Core.UI;

// `Image x = Image(...)` gives CS0119 — simple name lookup finds the member group before the type.
using UiImage = UnityEngine.UI.Image;
using UiObject = UnityEngine.Object;

namespace VortexArena.App.Editor
{
    /// <summary>
    /// The customer's order bubble (<c>asci.css</c> <c>.bbub</c>), rebuilt inside the existing
    /// <c>Bubble</c> node of the customer prefab.
    /// <para>
    /// ⚠️ The <c>Bubble</c> node itself is NEVER recreated: it carries the world-space Canvas, the
    /// 0.001 scale and the height the mode was tuned at, and <c>BurgerOrderBubble.root</c> points at
    /// it. Only its CHILDREN are thrown away and redrawn.
    /// </para>
    /// <para>
    /// ⚠️ Slab sizes are NOT written here. The stack is per-order data, so the one table of
    /// <c>.bing.*</c> metrics lives in the runtime component; the builder only pre-creates the pool.
    /// </para>
    /// </summary>
    public static partial class GirdapUiBuilder
    {
        public const string LkBurgerPrefabDir = "Assets/Modes/Burger/Prefabs/";

        private const string LkCustomerPrefab = LkBurgerPrefabDir + "NO_customer.prefab";
        private const string LkBubbleNode = "Bubble";
        private const string LkBubbleType = "BurgerOrderBubble";

        // .bbub — 420x300, padding 10 / 14 / 12.
        private const float LkBubW = 420f;
        private const float LkBubPadX = 14f;
        private const float LkBubPadTop = 10f;
        private const float LkBubPadBottom = 12f;
        private const float LkBubContentW = LkBubW - LkBubPadX * 2f;

        private const float LkHeadH = 44f;
        private const float LkBubRibbonW = 150f;
        private const float LkBubRibbonH = 36f;
        private const float LkFaceSize = 30f;
        private const float LkPatW = 104f;
        private const float LkPatH = 16f;
        private const float LkPatInset = 3f;

        private const float LkBodyY = LkBubPadTop + LkHeadH + 6f;
        private const float LkStackW = 160f;
        private const float LkColGap = 12f;

        private const float LkRowH = 22f;
        private const float LkRowGap = 6f;
        private const float LkDotSize = 18f;

        /// <summary>Row slots / slab slots pre-created in the prefab. ⚠️ Mirror of
        /// <c>BurgerOrderBubble.Capacity</c> — the runtime clips a longer recipe to the pool.</summary>
        private const int LkSlots = 7;

        private const float LkNoticeH = 50f;

        [MenuItem("Tools/VortexArena/UI/Girdap/Yalnız Aşçı sipariş baloncuğu")]
        public static void BuildLokantaBubbleMenu()
        {
            BuildAssets();
            BuildLokantaBubble();
            AssetDatabase.SaveAssets();
        }

        public static void BuildLokantaBubble()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(LkCustomerPrefab) == null)
            {
                Debug.LogWarning($"[Lokanta] {LkCustomerPrefab} yok — sipariş baloncuğu üretilemedi.");
                return;
            }

            GameObject root = PrefabUtility.LoadPrefabContents(LkCustomerPrefab);
            try
            {
                Transform bubble = LkFindDeep(root.transform, LkBubbleNode);
                if (bubble == null)
                {
                    Debug.LogError($"[Lokanta] {LkCustomerPrefab} içinde '{LkBubbleNode}' yok.");
                    return;
                }

                var bubbleRect = bubble as RectTransform;
                if (bubbleRect == null)
                {
                    Debug.LogError($"[Lokanta] '{LkBubbleNode}' bir RectTransform değil.");
                    return;
                }

                LkClearChildren(bubble);

                float h = bubbleRect.rect.height;
                LkBuildBubbleCard(bubble);
                LkBubbleHeader header = LkBuildBubbleHeader(bubble);
                LkBubbleBody body = LkBuildBubbleBody(bubble, h);
                LkBubbleNotice notice = LkBuildBubbleNotice(bubble);

                LkBindBubble(bubble.gameObject, header, body, notice, bubbleRect);
                PrefabUtility.SaveAsPrefabAsset(root, LkCustomerPrefab);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // ------------------------------------------------------------------- card

        private static void LkBuildBubbleCard(Transform parent)
        {
            UiShape plate = LkCard(parent, "Card", Lokanta.CardRadius, Lokanta.Cream);
            Stretch((RectTransform)plate.transform.parent);

            // Speech pointer: a down-pointing Ink triangle (slants meet at the apex) hanging out of the
            // bottom edge, with a Cream triangle inset by the outline so the card's own outline is cut
            // where the two meet and the pointer reads as part of the card.
            const float tailW = 44f;
            const float tailH = 22f;
            const float inset = Lokanta.Outline * 1.41421f; // 45° edges: horizontal inset per side
            RectTransform tail = Node(parent, "Tail");
            PlaceBottomCenter(tail, -(tailH - Lokanta.Outline), tailW, tailH);

            UiShape dark = Shape(tail, "Ink");
            Stretch(dark.rectTransform);
            dark.Slant(tailW * 0.5f, tailW * 0.5f).Fill(Lokanta.Ink);

            UiShape cream = Shape(tail, "Cream");
            float creamW = tailW - inset * 2f;
            Place(cream.rectTransform, inset, 0f, creamW, creamW * 0.5f);
            cream.Slant(creamW * 0.5f, creamW * 0.5f).Fill(Lokanta.Cream);
        }

        // ----------------------------------------------------------------- header

        private struct LkBubbleHeader
        {
            public UiImage face;
            public RectTransform patienceFill;
            public UiShape patienceShape;
        }

        /// <summary>CSS <c>.oh</c>: "SİPARİŞ" ribbon · spacer · mood face · patience bar.</summary>
        private static LkBubbleHeader LkBuildBubbleHeader(Transform parent)
        {
            RectTransform head = Node(parent, "Header");
            Place(head, LkBubPadX, LkBubPadTop, LkBubContentW, LkHeadH);

            RectTransform ribbon = LkRibbon(head, "Ribbon", "SİPARİŞ", LkBubRibbonW, LkBubRibbonH,
                GirdapFont.FredokaBold, 17f);
            PlaceMiddleLeft(ribbon, 0f, LkBubRibbonW, LkBubRibbonH);

            // The ribbon's own label is centred over the whole pill; re-inset it so the icon fits.
            UiImage mark = Image(ribbon, "Burger", LkSprite("DinerBurger"), Lokanta.Light);
            mark.preserveAspect = true;
            PlaceMiddleLeft(mark.rectTransform, 14f, 20f, 20f);
            Transform label = ribbon.Find("Label");
            if (label != null)
            {
                Stretch((RectTransform)label, 42f, 0f, 14f, 0f);
            }

            float patX = LkBubContentW - LkPatW;
            float faceX = patX - 8f - LkFaceSize;

            UiImage face = LkFace(head, "MoodFace", LkMood.Happy, LkFaceSize);
            PlaceMiddleLeft(face.rectTransform, faceX, LkFaceSize, LkFaceSize);

            UiShape track = Shape(head, "Patience");
            PlaceMiddleLeft(track.rectTransform, patX, LkPatW, LkPatH);
            track.Radius(8f).Outline(LkPatInset, Lokanta.Ink).Fill(Lokanta.Light);

            float innerW = LkPatW - LkPatInset * 2f;
            UiShape fill = Shape(track.transform, "Fill");
            PlaceMiddleLeft(fill.rectTransform, LkPatInset, innerW, LkPatH - LkPatInset * 2f);
            fill.Radius(5f).Fill(Lokanta.Green);

            return new LkBubbleHeader
            {
                face = face,
                patienceFill = fill.rectTransform,
                patienceShape = fill
            };
        }

        // ------------------------------------------------------------------- body

        private struct LkBubbleBody
        {
            public UiShape[] slabs;
            public UiStripes[] stripes;
            public GameObject[] sesame;
            public RectTransform[] rows;
            public UiShape[] dots;
            public TextMeshProUGUI[] labels;
        }

        /// <summary>CSS <c>.ob</c>: 160-wide picture column, then the caption list.</summary>
        private static LkBubbleBody LkBuildBubbleBody(Transform parent, float bubbleHeight)
        {
            float bodyH = bubbleHeight - LkBodyY - LkBubPadBottom;
            RectTransform body = Node(parent, "Body");
            Place(body, LkBubPadX, LkBodyY, LkBubContentW, bodyH);

            RectTransform stack = Node(body, "Stack");
            Place(stack, 0f, 0f, LkStackW, bodyH);

            float listX = LkStackW + LkColGap;
            float listW = LkBubContentW - listX;
            RectTransform list = Node(body, "List");
            Place(list, listX, 0f, listW, bodyH);

            var result = new LkBubbleBody
            {
                slabs = new UiShape[LkSlots],
                stripes = new UiStripes[LkSlots],
                sesame = new GameObject[LkSlots],
                rows = new RectTransform[LkSlots],
                dots = new UiShape[LkSlots],
                labels = new TextMeshProUGUI[LkSlots]
            };

            for (int i = 0; i < LkSlots; i++)
            {
                LkBuildSlab(stack, i, ref result);
                LkBuildRow(list, i, listW, ref result);
            }

            return result;
        }

        /// <summary>
        /// One pooled ingredient slab. Size, radius and gradient are written by the runtime from the
        /// recipe; the two optional layers (bacon stripes, sesame) are pre-created and switched.
        /// </summary>
        private static void LkBuildSlab(Transform parent, int index, ref LkBubbleBody body)
        {
            UiShape slab = Shape(parent, "Slab" + index);
            RectTransform rt = slab.rectTransform;

            // Centre-anchored: the runtime stacks the slabs around the column's middle.
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(140f, 14f);
            slab.Radius(5f).Outline(3f, Lokanta.Ink).Fill(Lokanta.Cream2, Lokanta.Must2,
                UiGradientMode.Vertical);

            // CSS: repeating-linear-gradient(90deg, Bacon 0 14px, BaconStripe 14px 20px).
            UiStripes stripes = Stripes(slab.transform, "Stripes");
            Stretch(stripes.rectTransform, 3f, 3f, 3f, 3f);
            stripes.RadiusTopLeft = 3f;
            stripes.RadiusTopRight = 3f;
            stripes.RadiusBottomRight = 3f;
            stripes.RadiusBottomLeft = 3f;
            stripes.Stripes(90f, 14f, 20f, Lokanta.Bacon);
            stripes.gameObject.SetActive(false);

            RectTransform seeds = Node(slab.transform, "Sesame");
            Stretch(seeds);
            for (int i = 0; i < 3; i++)
            {
                UiShape seed = Shape(seeds, "Seed" + i);
                PlaceCenter(seed.rectTransform, (i - 1) * 26f, -7f, 4f, 4f);
                seed.Radius(2f).Fill(Lokanta.Sesame);
            }

            seeds.gameObject.SetActive(false);
            slab.gameObject.SetActive(false);

            body.slabs[index] = slab;
            body.stripes[index] = stripes;
            body.sesame[index] = seeds.gameObject;
        }

        /// <summary>One caption row: colour dot + ingredient name. The runtime only moves it on the
        /// fixed row pitch and switches it off.</summary>
        private static void LkBuildRow(Transform parent, int index, float width,
            ref LkBubbleBody body)
        {
            RectTransform row = Node(parent, "Row" + index);
            row.anchorMin = new Vector2(0f, 0.5f);
            row.anchorMax = new Vector2(0f, 0.5f);
            row.pivot = new Vector2(0f, 0.5f);
            row.anchoredPosition = new Vector2(0f, index * (LkRowH + LkRowGap));
            row.sizeDelta = new Vector2(width, LkRowH);

            UiShape dot = Shape(row, "Dot");
            PlaceMiddleLeft(dot.rectTransform, 0f, LkDotSize, LkDotSize);
            dot.Radius(5f).Outline(2.5f, Lokanta.Ink).Fill(Lokanta.Cream2);

            float labelX = LkDotSize + 10f;
            TextMeshProUGUI label = LkText(row, "Label", "", GirdapFont.FredokaSemiBold, 22f,
                Lokanta.Ink, TextAlignmentOptions.MidlineLeft);
            PlaceMiddleLeft(label.rectTransform, labelX, width - labelX, LkRowH);

            row.gameObject.SetActive(false);

            body.rows[index] = row;
            body.dots[index] = dot;
            body.labels[index] = label;
        }

        // ----------------------------------------------------------------- notice

        private struct LkBubbleNotice
        {
            public GameObject root;
            public TextMeshProUGUI text;
        }

        /// <summary>CSS <c>.bstick</c>: a red sticker slapped crooked over the top of the bubble. The
        /// order stays readable underneath — the sticker only says why a serve bounced.</summary>
        private static LkBubbleNotice LkBuildBubbleNotice(Transform parent)
        {
            RectTransform notice = Node(parent, "Notice");
            StretchTop(notice, 10f, 4f, 10f, LkNoticeH);
            notice.localEulerAngles = new Vector3(0f, 0f, -2f);

            UiShape plate = LkCard(notice, "Card", 16f, Lokanta.Red, Lokanta.Outline, 5f);
            Stretch((RectTransform)plate.transform.parent);

            UiImage warn = Icon(notice, "Warn", "Warn", 26f, Lokanta.Light);
            PlaceMiddleLeft(warn.rectTransform, 16f, 26f, 26f);

            TextMeshProUGUI text = LkText(notice, "Text", "", GirdapFont.FredokaBold, 24f,
                Lokanta.Light, TextAlignmentOptions.MidlineLeft);
            Stretch(text.rectTransform, 52f, 0f, 16f, 0f);
            TextShadow(text, Lokanta.Ink);

            notice.gameObject.SetActive(false);
            return new LkBubbleNotice { root = notice.gameObject, text = text };
        }

        // ----------------------------------------------------------------- wiring

        private static void LkBindBubble(GameObject bubble, LkBubbleHeader header,
            LkBubbleBody body, LkBubbleNotice notice, RectTransform rect)
        {
            Component comp = LkComponent(bubble, LkBubbleType);
            if (comp == null)
            {
                Debug.LogError($"[Lokanta] '{LkBubbleNode}' üzerinde {LkBubbleType} yok — "
                               + "bağlama yapılamadı.");
                return;
            }

            var so = new SerializedObject(comp);
            HudSet(so, "root", bubble);
            HudSet(so, "text", null); // the row slots replace the single caption block
            HudSet(so, "noticeRoot", notice.root);
            HudSet(so, "noticeText", notice.text);
            HudSet(so, "moodFace", header.face);
            HudSet(so, "moodHappy", LkSprite("Lk_FaceHappy"));
            HudSet(so, "moodMeh", LkSprite("Lk_FaceMeh"));
            HudSet(so, "moodSad", LkSprite("Lk_FaceSad"));
            HudSet(so, "patienceFill", header.patienceFill);
            HudSet(so, "patienceShape", header.patienceShape);
            HudSetFloat(so, "patienceWidth", LkPatW - LkPatInset * 2f);
            HudSetFloat(so, "slabGap", 2f);
            HudSetFloat(so, "rowPitch", LkRowH + LkRowGap);
            HudSetArray(so, "slabs", body.slabs);
            HudSetArray(so, "slabStripes", body.stripes);
            HudSetArray(so, "slabSesame", body.sesame);
            HudSetArray(so, "listRows", body.rows);
            HudSetArray(so, "listDots", body.dots);
            HudSetArray(so, "listLabels", body.labels);
            so.ApplyModifiedPropertiesWithoutUndo();

            Debug.Log("[Lokanta] Sipariş baloncuğu üretildi: Bubble/{Card/{Shadow,Plate}, Tail/"
                      + "{Ink,Cream}, Header/{Ribbon/{Bg,Line,DotsTop,DotsBottom,Label,Burger},"
                      + " MoodFace, Patience/Fill}, Body/{Stack/Slab0-6/{Stripes,Sesame/Seed0-2},"
                      + " List/Row0-6/{Dot,Label}}, Notice/{Card,Warn,Text}}"
                      + $" · baloncuk {rect.rect.width}x{rect.rect.height}"
                      + " · bağlanan: root, noticeRoot, noticeText, moodFace, moodHappy/Meh/Sad,"
                      + " patienceFill, patienceShape, patienceWidth, slabGap, rowPitch, slabs,"
                      + " slabStripes, slabSesame, listRows, listDots, listLabels (text boşaltıldı).");
        }

        // ---------------------------------------------------------------- helpers

        /// <summary>Breadth-first child lookup by name (the node's depth is prefab data, not ours).</summary>
        private static Transform LkFindDeep(Transform root, string name)
        {
            var queue = new Queue<Transform>();
            queue.Enqueue(root);

            while (queue.Count > 0)
            {
                Transform current = queue.Dequeue();
                for (int i = 0; i < current.childCount; i++)
                {
                    Transform child = current.GetChild(i);
                    if (child.name == name)
                    {
                        return child;
                    }

                    queue.Enqueue(child);
                }
            }

            return null;
        }

        private static void LkClearChildren(Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                UiObject.DestroyImmediate(parent.GetChild(i).gameObject);
            }
        }

        /// <summary>
        /// Component matched by TYPE NAME. ⚠️ The editor assembly cannot reference
        /// <c>VortexArena.Modes.Burger</c>, so mode components are reached by name and written through
        /// <see cref="SerializedObject"/>.
        /// </summary>
        private static Component LkComponent(GameObject go, string typeName)
        {
            Component[] parts = go.GetComponents<Component>();
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i] != null && parts[i].GetType().Name == typeName)
                {
                    return parts[i];
                }
            }

            return null;
        }

        private static void HudSetArray(SerializedObject so, string field, UiObject[] values)
        {
            SerializedProperty property = so.FindProperty(field);
            if (property == null)
            {
                Debug.LogError($"[Girdap] {so.targetObject.GetType().Name}.{field} alanı yok.");
                return;
            }

            property.arraySize = values != null ? values.Length : 0;
            for (int i = 0; i < property.arraySize; i++)
            {
                property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            }
        }
    }
}
