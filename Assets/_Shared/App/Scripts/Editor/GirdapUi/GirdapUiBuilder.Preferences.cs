using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using VortexArena.App.Admin;
using VortexArena.Core.Audio;
using VortexArena.Core.UI;

// Factory methods share element names (Image / Button / Text) with the types.
using UiImage = UnityEngine.UI.Image;
using UiButton = UnityEngine.UI.Button;

namespace VortexArena.App.Editor
{
    /// <summary>
    /// Builds <c>AdminPreferencesPanel.prefab</c> from <c>plan/arayuz-yenileme/tercihler.html</c>
    /// (4 tabs) + <c>tema.css</c>.
    /// <para>⚠️ Edited IN PLACE: the prefab is a NESTED instance inside <c>AdminHud.prefab</c>, and
    /// saving a fresh root over it would break that nesting.</para>
    /// </summary>
    public static partial class GirdapUiBuilder
    {
        // ------------------------------------------------------------- css metrics

        private const string PrefsPrefab = "AdminPreferencesPanel";

        // .panel
        private const float PrefsX = 240f;
        private const float PrefsY = 135f;
        private const float PrefsW = 1440f;
        private const float PrefsH = 810f;
        private const float PrefsChamfer = 26f;
        private const float PrefsHeadH = 72f;

        // .pgrid inside .p-body (padding 12/24 + 16/12)
        private const float PrefsGridX = 36f;
        private const float PrefsGridY = 28f;
        private const float PrefsColW = 655f;
        private const float PrefsColGap = 56f;
        private const float PrefsWideW = PrefsColW * 2f + PrefsColGap;

        // .frow / controls
        private const float PrefsRowH = 68f;
        private const float PrefsCtrlH = 48f;
        private const float PrefsSelectW = 324f;
        private const float PrefsStepBtnW = 48f;
        private const float PrefsStepGap = 4f;
        private const float PrefsValW = 220f;
        private const float PrefsValWideW = 286f;
        private const float PrefsToggleH = 50f;
        private const float PrefsToggleW = 324f;

        // .meter: 10 cells of 15 px + 3 px gap, skewX(-16deg)
        private const int PrefsMeterCells = 10;
        private const float PrefsMeterCell = 15f;
        private const float PrefsMeterGap = 3f;
        private const float PrefsMeterW = PrefsMeterCells * (PrefsMeterCell + PrefsMeterGap)
                                          - PrefsMeterGap;
        private const float PrefsMeterH = 26f;

        // Open dropdown list: no mockup frame, so the list is sized for the longest catalog.
        private const float PrefsListH = 320f;
        private const float PrefsItemH = 44f;

        // rgba(170, 205, 255, x) / rgba(125, 227, 255, x) — alpha variants of palette tokens, so a
        // token change still carries through.
        private static readonly Color PrefsValBd = PrefsFade(Girdap.BtnBdA, 0.18f);
        private static readonly Color PrefsPlateBd = PrefsFade(Girdap.BtnBdA, 0.20f);
        private static readonly Color PrefsRowLine = PrefsFade(Girdap.BtnBdA, 0.08f);
        private static readonly Color PrefsHeadGlow = PrefsFade(Girdap.Acc, 0.10f);
        private static readonly Color PrefsDots = new Color(1f, 1f, 1f, 0.05f);
        private static readonly Color PrefsPanelGlow = PrefsFade(Girdap.Acc, 0.13f);

        private static Color PrefsFade(Color c, float a)
        {
            c.a = a;
            return c;
        }

        [MenuItem("Tools/VortexArena/UI/Girdap/Yalnız Tercihler")]
        public static void BuildPreferencesPanelOnly()
        {
            BuildAssets();
            BuildPreferencesPanel();
            AssetDatabase.SaveAssets();
        }

        static partial void BuildPreferencesPanel()
        {
            GameObject root = LoadPrefabContents(PrefsPrefab) ?? NewRoot(PrefsPrefab, 1920f, 1080f);

            var old = new List<GameObject>();
            foreach (Transform child in root.transform)
            {
                old.Add(child.gameObject);
            }

            for (int i = 0; i < old.Count; i++)
            {
                Object.DestroyImmediate(old[i]);
            }

            var panel = root.GetComponent<AdminPreferencesPanel>();
            if (panel == null)
            {
                panel = root.AddComponent<AdminPreferencesPanel>();
            }

            // Full-screen holder: the card is a child, so the panel can be hidden without losing the
            // component's subscriptions (AdminPreferencesPanel stays active while closed).
            Stretch(root.GetComponent<RectTransform>());

            var so = new SerializedObject(panel);

            RectTransform card = Node(root.transform, "Card");
            Place(card, PrefsX, PrefsY, PrefsW, PrefsH);
            PrefsBind(so, "_root", card.gameObject);

            PrefsCardBackground(card);
            RectTransform body = PrefsHeader(card, so);
            PrefsPages(body, so);

            so.ApplyModifiedPropertiesWithoutUndo();
            SaveAndUnload(root, PrefsPrefab);
        }

        // --------------------------------------------------------------- card shell

        private static void PrefsCardBackground(RectTransform card)
        {
            UiShape bg = Shape(card, "Bg");
            Stretch(bg.rectTransform);
            bg.Chamfer(PrefsChamfer)
                .Outline(1f, Girdap.PanelBdA, Girdap.PanelBdB)
                .Fill(Girdap.PanelA, Girdap.PanelB, UiGradientMode.Vertical);
            bg.raycastTarget = true; // the card eats clicks so they do not reach the arena behind

            // ⚠️ No RectMask2D on the card: it would also clip the dropdown lists, which are
            // instantiated under their selector row. The glow is therefore sized to stay inside.
            UiImage glow = Image(card, "Glow", LoadSprite("Radial_256.png"), PrefsPanelGlow);
            PlaceTopCenter(glow.rectTransform, 0f, PrefsW, 420f);

            UiImage dots = Image(card, "Dots", LoadSprite("Dots_16.png"), PrefsDots,
                UiImage.Type.Tiled);
            Stretch(dots.rectTransform, 1f, 1f, 1f, 1f);
        }

        /// <summary>Header (title + tab bar + close) and the body root it hands back.</summary>
        private static RectTransform PrefsHeader(RectTransform card, SerializedObject so)
        {
            RectTransform head = Node(card, "Head");
            Place(head, 1f, 1f, PrefsW - 2f, PrefsHeadH);

            UiShape headBg = Shape(head, "Bg");
            Stretch(headBg.rectTransform);
            headBg.Fill(PrefsHeadGlow, PrefsFade(Girdap.Acc, 0f), UiGradientMode.Vertical)
                .Antialias(false);

            UiShape headLine = Shape(head, "Line");
            StretchBottom(headLine.rectTransform, 0f, 0f, 0f, 1f);
            headLine.Fill(Girdap.Line).Antialias(false);

            // .p-title::before — three leaning accent bands. Striped mesh rather than three slanted
            // boxes: a 24 px box cannot carry a 10 px skew per band without self-crossing.
            UiStripes mark = Stripes(head, "TitleMark");
            Place(mark.rectTransform, 28f, (PrefsHeadH - 28f) * 0.5f, 24f, 28f);
            mark.Stripes(70f, 5.5f, 9f, Girdap.Acc);

            TextMeshProUGUI title = Text(head, "Title", "TERCİHLER", GirdapFont.ChakraBold, 29f,
                Girdap.Text, TextAlignmentOptions.MidlineLeft, 0.14f);
            float titleW = Mathf.Ceil(title.GetPreferredValues(title.text).x);
            Place(title.rectTransform, 68f, 0f, titleW, PrefsHeadH);

            PrefsTabs(head, so, 68f + titleW + 20f + 28f);

            // .p-head's close button: Esc is really bound (AdminSpectator → AdminSession.ClosePanel).
            UiButtonStyle close = Button(head, "Close", "KAPAT", UiButtonKind.Normal, 0f, 0f, 158f,
                PrefsCtrlH, "X", "Esc");
            PlaceRight(close.GetComponent<RectTransform>(), 16f,
                (PrefsHeadH - PrefsCtrlH) * 0.5f, 158f, PrefsCtrlH);
            PrefsBind(so, "_closeButton", close.TargetButton);

            RectTransform body = Node(card, "Body");
            Place(body, 1f, 1f + PrefsHeadH, PrefsW - 2f, PrefsH - 2f - PrefsHeadH);
            return body;
        }

        private static void PrefsTabs(RectTransform head, SerializedObject so, float x)
        {
            string[] names = { "MAÇ", "GÖRÜNÜM", "BAĞLANTI", "SES" };
            var buttons = new Object[names.Length];
            float cursor = x;

            for (int i = 0; i < names.Length; i++)
            {
                // .tab: 0 32 padding, parallelogram (13 px top-left / bottom-right inset).
                UiButtonStyle tab = Button(head, "Tab" + i, names[i],
                    i == 0 ? UiButtonKind.SegOn : UiButtonKind.Tab, cursor,
                    (PrefsHeadH - PrefsCtrlH) * 0.5f, 100f, PrefsCtrlH, null, null, 18f,
                    GirdapFont.ChakraSemiBold, 0f);
                float w = Mathf.Ceil(tab.Label.GetPreferredValues(names[i]).x) + 64f;
                RectTransform rt = tab.GetComponent<RectTransform>();
                Place(rt, cursor, (PrefsHeadH - PrefsCtrlH) * 0.5f, w, PrefsCtrlH);
                tab.Shape.Slant(-13f, 13f);
                tab.Label.characterSpacing = Girdap.Spacing(0.14f);

                buttons[i] = tab.TargetButton;
                cursor += w + 2f;
            }

            PrefsBindArray(so, "_tabButtons", buttons);
        }

        // ------------------------------------------------------------------- pages

        private static void PrefsPages(RectTransform body, SerializedObject so)
        {
            var pages = new Object[4];
            for (int i = 0; i < pages.Length; i++)
            {
                RectTransform page = Node(body, "Page" + i);
                Stretch(page);
                page.gameObject.SetActive(i == 0);
                pages[i] = page.gameObject;
            }

            PrefsBindArray(so, "_tabPages", pages);

            PrefsMatchPage((RectTransform)body.GetChild(0), so);
            PrefsViewPage((RectTransform)body.GetChild(1), so);
            PrefsConnectionPage((RectTransform)body.GetChild(2), so);
            PrefsAudioPage((RectTransform)body.GetChild(3), so);
        }

        private static void PrefsMatchPage(RectTransform page, SerializedObject so)
        {
            RectTransform left = PrefsColumn(page, 0);
            float y = 0f;
            PrefsSection(left, ref y, "OYUN", null);

            RectTransform row = PrefsRow(left, ref y, "Oyun tipi");
            PrefsBind(so, "_gameTypeRow", row.gameObject);
            PrefsBind(so, "_gameTypeDropdown", PrefsSelect(row, "GameType"));
            PrefsBind(so, "_modeDropdown", PrefsSelect(PrefsRow(left, ref y, "Mod"), "Mode"));
            PrefsBind(so, "_mapDropdown", PrefsSelect(PrefsRow(left, ref y, "Harita"), "Map"));
            float leftH = y;

            RectTransform right = PrefsColumn(page, 1);
            y = 0f;
            PrefsSection(right, ref y, "KURALLAR", null);
            PrefsStepper(PrefsRow(right, ref y, "Süre"), so, "_durationPrev", "_durationValue",
                "_durationNext", false);
            PrefsStepper(PrefsRow(right, ref y, "Skor limiti"), so, "_scoreLimitPrev",
                "_scoreLimitValue", "_scoreLimitNext", false);
            PrefsStepper(PrefsRow(right, ref y, "Geri sayım"), so, "_countdownPrev",
                "_countdownValue", "_countdownNext", false);
            PrefsToggle(PrefsRow(right, ref y, "Dost ateşi"), so, "_friendlyFireOff",
                "_friendlyFireOn");
            float rightH = y;

            // .pgrid's `.wide` cell starts after the TALLER column (row-gap is 0).
            RectTransform wide = PrefsWide(page, Mathf.Max(leftH, rightH));
            y = 0f;
            PrefsSection(wide, ref y, "KALİBRASYON VE BOY", "tüm adminlerde ortak");

            string[] calib = { "2 ÇAPA", "ESKİ KALİBRE", "ÇAPA BULUTU" };
            string[] calibFields =
                { "_calibModeTwoButton", "_calibModeSavedButton", "_calibModeCloudButton" };
            UiButtonStyle[] calibButtons = PrefsBigSeg(wide, ref y, calib, PrefsWideW, null);
            for (int i = 0; i < calibButtons.Length; i++)
            {
                PrefsBind(so, calibFields[i], calibButtons[i].TargetButton);
            }
        }

        private static void PrefsViewPage(RectTransform page, SerializedObject so)
        {
            RectTransform left = PrefsColumn(page, 0);
            float y = 0f;
            PrefsSection(left, ref y, "SAHNE", null);
            PrefsStepper(PrefsRow(left, ref y, "Halkalar"), so, "_markersPrev", "_markersValue",
                "_markersNext", false);
            PrefsToggle(PrefsRow(left, ref y, "Ad etiketleri"), so, "_nameplatesOff",
                "_nameplatesOn");
            PrefsStepper(PrefsRow(left, ref y, "Çatı"), so, "_roofPrev", "_roofValue", "_roofNext",
                false);

            RectTransform right = PrefsColumn(page, 1);
            y = 0f;
            PrefsSection(right, ref y, "KAMERA VE UYARI", null);
            PrefsStepper(PrefsRow(right, ref y, "Kamera hızı"), so, "_speedPrev", "_speedValue",
                "_speedNext", false);
            PrefsToggle(PrefsRow(right, ref y, "İhlal sesi"), so, "_violationSoundOff",
                "_violationSoundOn");
        }

        private static void PrefsConnectionPage(RectTransform page, SerializedObject so)
        {
            RectTransform left = PrefsColumn(page, 0);
            float y = 0f;
            PrefsSection(left, ref y, "SUNUCU", null);

            // .conn
            UiShape conn = PrefsPlate(left, ref y, 76f);
            UiShape dot = Shape(conn.transform, "Dot");
            PlaceMiddleLeft(dot.rectTransform, 20f, 14f, 14f);
            dot.Chamfer(7f, 7f, 7f, 7f).Fill(Girdap.Good).Glow(12f, PrefsFade(Girdap.Good, 0.55f));
            PrefsBind(so, "_connectionDot", dot);

            TextMeshProUGUI state = Text(conn.transform, "State", "bağlı", GirdapFont.SairaBold,
                30f, Girdap.Text, TextAlignmentOptions.MidlineLeft);
            PlaceMiddleLeft(state.rectTransform, 48f, PrefsColW - 68f, 40f);
            PrefsBind(so, "_connectionText", state);

            UiButtonStyle[] net = PrefsBigSeg(left, ref y,
                new[] { "YENİDEN BAĞLAN", "BAĞLANTIYI KES" }, PrefsColW,
                new[] { "Refresh", "X" });
            PrefsBind(so, "_reconnectButton", net[0].TargetButton);
            PrefsBind(so, "_disconnectButton", net[1].TargetButton);

            RectTransform right = PrefsColumn(page, 1);
            y = 0f;
            PrefsSection(right, ref y, "UYGULAMA", null);
            UiButtonStyle[] quit = PrefsBigSeg(right, ref y, new[] { "OYUNDAN ÇIK" }, PrefsColW,
                new[] { "Power" });
            quit[0].SetKind(UiButtonKind.Danger);
            PrefsBind(so, "_quitButton", quit[0].TargetButton);
        }

        private static void PrefsAudioPage(RectTransform page, SerializedObject so)
        {
            RectTransform left = PrefsColumn(page, 0);
            float y = 0f;
            PrefsSection(left, ref y, "KANALLAR", null);
            PrefsBind(so, "_audioDeviceDropdown",
                PrefsSelect(PrefsRow(left, ref y, "Ses çıkışı"), "AudioDevice"));

            string[] channels = { "Ambiyans", "Silah sesleri", "Seslendirme", "Müzik" };
            var values = new Object[AudioMix.ChannelCount];
            var meters = new Object[AudioMix.ChannelCount];
            var mutes = new Object[AudioMix.ChannelCount];
            var muteIcons = new Object[AudioMix.ChannelCount];
            var down = new Object[AudioMix.ChannelCount];
            var up = new Object[AudioMix.ChannelCount];

            for (int i = 0; i < AudioMix.ChannelCount; i++)
            {
                PrefsLevel(PrefsRow(left, ref y, channels[i]), "Ch" + i, out UiButtonStyle mute,
                    out UiImage icon, out UiSegmentBar meter, out TextMeshProUGUI value,
                    out UiButtonStyle dn, out UiButtonStyle upBtn);
                values[i] = value;
                meters[i] = meter;
                mutes[i] = mute.TargetButton;
                muteIcons[i] = icon;
                down[i] = dn.TargetButton;
                up[i] = upBtn.TargetButton;
            }

            PrefsBindArray(so, "_audioValues", values);
            PrefsBindArray(so, "_audioMeters", meters);
            PrefsBindArray(so, "_audioMuteButtons", mutes);
            PrefsBindArray(so, "_audioMuteIcons", muteIcons);
            PrefsBindArray(so, "_audioPrev", down);
            PrefsBindArray(so, "_audioNext", up);
            PrefsBind(so, "_volSprite", LoadIcon("Vol"));
            PrefsBind(so, "_muteSprite", LoadIcon("Mute"));

            RectTransform right = PrefsColumn(page, 1);
            y = 0f;
            PrefsSection(right, ref y, "MÜZİK ÇALAR", null);

            // .track — one line: the runtime writes name, order and state as a single string.
            UiShape track = PrefsPlate(right, ref y, 76f);
            TextMeshProUGUI trackText = Text(track.transform, "Track", "Action-1 (1/5)",
                GirdapFont.SairaBold, 34f, Girdap.Text, TextAlignmentOptions.MidlineLeft);
            PlaceMiddleLeft(trackText.rectTransform, 20f, PrefsColW - 40f, 44f);
            PrefsBind(so, "_musicTrackValue", trackText);

            // .transport — 4 columns, 60 px tall, 26 px icons.
            y += 8f;
            string[] icons = { "Prev", "Play", "Stop", "Next" };
            string[] transportFields =
                { "_musicPrevTrack", "_musicPlayPauseButton", "_musicStopButton", "_musicNextTrack" };
            float transportW = (PrefsColW - 24f) / 4f;
            for (int i = 0; i < icons.Length; i++)
            {
                UiButtonStyle btn = Button(right, "Transport" + i, "",
                    i == 1 ? UiButtonKind.Go : i == 2 ? UiButtonKind.TextBad : UiButtonKind.Normal,
                    i * (transportW + 8f), y, transportW, 60f, icons[i]);
                UiImage icon = PrefsCenterIcon(btn, 26f, transportW);
                PrefsBind(so, transportFields[i], btn.TargetButton);
                if (i == 1)
                {
                    PrefsBind(so, "_musicPlayPauseIcon", icon);
                }
            }

            PrefsBind(so, "_musicPlaySprite", LoadIcon("Play"));
            PrefsBind(so, "_musicPauseSprite", LoadIcon("Pause"));
            y += 60f + 4f;

            PrefsLevel(PrefsRow(right, ref y, "Müzik çalar sesi"), "Music",
                out UiButtonStyle musicMute, out UiImage musicIcon, out UiSegmentBar musicMeter,
                out TextMeshProUGUI musicValue, out UiButtonStyle musicDown,
                out UiButtonStyle musicUp);
            PrefsBind(so, "_musicMuteButton", musicMute.TargetButton);
            PrefsBind(so, "_musicMuteIcon", musicIcon);
            PrefsBind(so, "_musicLevelMeter", musicMeter);
            PrefsBind(so, "_musicLevelValue", musicValue);
            PrefsBind(so, "_musicLevelPrev", musicDown.TargetButton);
            PrefsBind(so, "_musicLevelNext", musicUp.TargetButton);
        }

        // ------------------------------------------------------------------- parts

        private static RectTransform PrefsColumn(RectTransform page, int index)
        {
            RectTransform col = Node(page, index == 0 ? "Left" : "Right");
            Place(col, PrefsGridX + index * (PrefsColW + PrefsColGap), PrefsGridY, PrefsColW, 0f);
            return col;
        }

        private static RectTransform PrefsWide(RectTransform page, float y)
        {
            RectTransform wide = Node(page, "Wide");
            Place(wide, PrefsGridX, PrefsGridY + y, PrefsWideW, 0f);
            return wide;
        }

        /// <summary>CSS <c>.sec</c>: 12 px top margin, caption, 10 px padding, 1 px rule with a
        /// 72 px accent segment.</summary>
        private static void PrefsSection(RectTransform col, ref float y, string title,
            string subtitle)
        {
            y += 12f;

            TextMeshProUGUI caption = Text(col, "Sec_" + title, title, GirdapFont.ChakraSemiBold,
                14f, Girdap.Acc, TextAlignmentOptions.MidlineLeft, 0.22f);
            float captionW = Mathf.Ceil(caption.GetPreferredValues(title).x);
            Place(caption.rectTransform, 0f, y, captionW, 16f);

            if (!string.IsNullOrEmpty(subtitle))
            {
                TextMeshProUGUI small = Text(col, "SecNote", subtitle, GirdapFont.BarlowMedium, 14f,
                    Girdap.Faint, TextAlignmentOptions.MidlineLeft);
                Place(small.rectTransform, captionW + 12f, y, 320f, 16f);
            }

            UiShape rule = Shape(col, "SecLine");
            Place(rule.rectTransform, 0f, y + 24f, col.sizeDelta.x, 1f);
            rule.Fill(Girdap.Line).Antialias(false);

            UiShape accent = Shape(col, "SecAccent");
            Place(accent.rectTransform, 0f, y + 23f, 72f, 2f);
            accent.Fill(Girdap.Acc).Glow(10f, PrefsFade(Girdap.Acc, 0.6f));

            y += 25f;
        }

        /// <summary>CSS <c>.frow</c>: 68 px tall, label on the left, controls flush right.</summary>
        private static RectTransform PrefsRow(RectTransform col, ref float y, string label)
        {
            RectTransform row = Node(col, "Row_" + label);
            Place(row, 0f, y, col.sizeDelta.x, PrefsRowH);
            y += PrefsRowH;
            PrefsGrow(col, y);

            UiShape line = Shape(row, "Line");
            StretchBottom(line.rectTransform, 0f, 0f, 0f, 1f);
            line.Fill(PrefsRowLine).Antialias(false);

            TextMeshProUGUI text = Text(row, "Label", label, GirdapFont.SairaBold, 24f, Girdap.Text,
                TextAlignmentOptions.MidlineLeft);
            Stretch(text.rectTransform, 0f, 0f, 340f, 0f);
            return row;
        }

        // The column rect only exists so children can be placed relative to it; its height is grown
        // as rows are added (nothing reads it, but a zero-height rect is confusing in the Inspector).
        private static void PrefsGrow(RectTransform col, float h)
        {
            col.sizeDelta = new Vector2(col.sizeDelta.x, h);
        }

        /// <summary>CSS <c>.select</c> — a real <see cref="TMP_Dropdown"/>, so the runtime's option
        /// filling and cursor syncing are untouched; only the graphics are themed.</summary>
        private static TMP_Dropdown PrefsSelect(RectTransform row, string name)
        {
            RectTransform rt = Node(row, "Select_" + name);
            PlaceRight(rt, 0f, (PrefsRowH - PrefsCtrlH) * 0.5f, PrefsSelectW, PrefsCtrlH);

            UiShape bg = Shape(rt, "Bg");
            Stretch(bg.rectTransform);
            bg.Chamfer(8f).Outline(1f, Girdap.SelectBd)
                .Fill(Girdap.SelectBgA, Girdap.SelectBgB, UiGradientMode.Vertical);
            bg.raycastTarget = true;

            TextMeshProUGUI caption = Text(rt, "Caption", "", GirdapFont.SairaBold, 24f,
                Girdap.Text, TextAlignmentOptions.MidlineLeft);
            Stretch(caption.rectTransform, 16f, 0f, 40f, 0f);

            UiImage arrow = Icon(rt, "Arrow", "Down", 20f, Girdap.Muted);
            PlaceRight(arrow.rectTransform, 12f, (PrefsCtrlH - 20f) * 0.5f, 20f, 20f);

            // ---- list template (inactive; TMP clones it on open)
            RectTransform template = Node(rt, "Template");
            template.anchorMin = new Vector2(0f, 0f);
            template.anchorMax = new Vector2(1f, 0f);
            template.pivot = new Vector2(0.5f, 1f);
            template.anchoredPosition = new Vector2(0f, 2f);
            template.sizeDelta = new Vector2(0f, PrefsListH);

            UiShape listBg = Shape(template, "Bg");
            Stretch(listBg.rectTransform);
            listBg.Chamfer(8f).Outline(1f, Girdap.SelectBd)
                .Fill(Girdap.SelectBgA, Girdap.SelectBgB, UiGradientMode.Vertical);
            listBg.raycastTarget = true;

            var scroll = template.gameObject.AddComponent<ScrollRect>();

            RectTransform viewport = Node(template, "Viewport");
            Stretch(viewport, 1f, 1f, 1f, 1f);
            viewport.pivot = new Vector2(0f, 1f);
            viewport.gameObject.AddComponent<RectMask2D>(); // cheaper than Mask: needs no graphic

            RectTransform content = Node(viewport, "Content");
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = new Vector2(0f, PrefsItemH);

            RectTransform item = Node(content, "Item");
            item.anchorMin = new Vector2(0f, 0.5f);
            item.anchorMax = new Vector2(1f, 0.5f);
            item.sizeDelta = new Vector2(0f, PrefsItemH);

            // White fill: the Toggle's ColorBlock multiplies Graphic.color, so the hover tint has to
            // come out of white.
            UiShape itemBg = Shape(item, "Item Background");
            Stretch(itemBg.rectTransform);
            itemBg.Fill(Color.white).Antialias(false);
            itemBg.raycastTarget = true;

            UiShape check = Shape(item, "Item Checkmark");
            Stretch(check.rectTransform);
            check.Fill(PrefsFade(Girdap.Acc, 0.16f)).Antialias(false);

            TextMeshProUGUI itemLabel = Text(item, "Item Label", "", GirdapFont.SairaBold, 22f,
                Girdap.Text, TextAlignmentOptions.MidlineLeft);
            Stretch(itemLabel.rectTransform, 16f, 0f, 12f, 0f);

            var toggle = item.gameObject.AddComponent<Toggle>();
            toggle.targetGraphic = itemBg;
            toggle.graphic = check;
            toggle.isOn = true;
            ColorBlock itemColors = toggle.colors;
            itemColors.normalColor = Color.clear;
            itemColors.highlightedColor = Girdap.SegHover;
            itemColors.pressedColor = PrefsFade(Girdap.Acc, 0.2f);
            itemColors.selectedColor = Color.clear;
            itemColors.disabledColor = Color.clear;
            toggle.colors = itemColors;

            scroll.content = content;
            scroll.viewport = viewport;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;

            var dropdown = rt.gameObject.AddComponent<TMP_Dropdown>();
            dropdown.targetGraphic = bg;
            dropdown.template = template;
            dropdown.captionText = caption;
            dropdown.itemText = itemLabel;
            template.gameObject.SetActive(false);
            return dropdown;
        }

        /// <summary>CSS <c>.stepper</c>: ◀ · value box · ▶, flush right in the row.</summary>
        private static void PrefsStepper(RectTransform row, SerializedObject so, string prevField,
            string valueField, string nextField, bool wide)
        {
            float valW = wide ? PrefsValWideW : PrefsValW;
            float top = (PrefsRowH - PrefsCtrlH) * 0.5f;

            UiButtonStyle next = PrefsArrow(row, "Next", "Right", 0f, top);
            UiShape box = PrefsValueBox(row, valW, PrefsStepBtnW + PrefsStepGap, top);
            UiButtonStyle prev = PrefsArrow(row, "Prev", "Left",
                PrefsStepBtnW + PrefsStepGap + valW + PrefsStepGap, top);

            TextMeshProUGUI value = Text(box.transform, "Value", "—", GirdapFont.SairaBold, 24f,
                Girdap.Text, TextAlignmentOptions.Center);
            Stretch(value.rectTransform, 8f, 0f, 8f, 0f);

            PrefsBind(so, prevField, prev.TargetButton);
            PrefsBind(so, nextField, next.TargetButton);
            PrefsBind(so, valueField, value);
        }

        /// <summary>CSS <c>.seg.in</c>: two halves, the live one lit (kind written at runtime).</summary>
        private static void PrefsToggle(RectTransform row, SerializedObject so, string offField,
            string onField)
        {
            float top = (PrefsRowH - PrefsToggleH) * 0.5f;

            RectTransform seg = Node(row, "Seg");
            PlaceRight(seg, 0f, top, PrefsToggleW, PrefsToggleH);

            UiShape bg = Shape(seg, "Bg");
            Stretch(bg.rectTransform);
            bg.Chamfer(8f).Outline(1f, Girdap.SegBd).Fill(Girdap.SegInBg);

            float itemW = (PrefsToggleW - 10f - 4f) * 0.5f;
            UiButtonStyle off = Button(seg, "Off", "KAPALI", UiButtonKind.SegOn, 5f, 5f, itemW, 40f,
                null, null, 20f, GirdapFont.SairaBold, 8f);
            UiButtonStyle on = Button(seg, "On", "AÇIK", UiButtonKind.Seg, 5f + itemW + 4f, 5f,
                itemW, 40f, null, null, 20f, GirdapFont.SairaBold, 8f);

            PrefsBind(so, offField, off.TargetButton);
            PrefsBind(so, onField, on.TargetButton);
        }

        /// <summary>CSS <c>level()</c> row: mute square + wide stepper whose value box carries the
        /// 10-cell meter and the percentage.</summary>
        private static void PrefsLevel(RectTransform row, string name, out UiButtonStyle mute,
            out UiImage muteIcon, out UiSegmentBar meter, out TextMeshProUGUI value,
            out UiButtonStyle down, out UiButtonStyle up)
        {
            float top = (PrefsRowH - PrefsCtrlH) * 0.5f;
            float stepperW = PrefsStepBtnW * 2f + PrefsStepGap * 2f + PrefsValWideW;

            mute = Button(row, "Mute_" + name, "", UiButtonKind.Normal, 0f, 0f, PrefsCtrlH,
                PrefsCtrlH, "Vol");
            PlaceRight(mute.GetComponent<RectTransform>(), stepperW + 12f, top, PrefsCtrlH,
                PrefsCtrlH);
            muteIcon = PrefsCenterIcon(mute, 20f, PrefsCtrlH);

            up = PrefsArrow(row, "Up_" + name, "Right", 0f, top);
            UiShape box = PrefsValueBox(row, PrefsValWideW, PrefsStepBtnW + PrefsStepGap, top);
            down = PrefsArrow(row, "Down_" + name, "Left",
                PrefsStepBtnW + PrefsStepGap + PrefsValWideW + PrefsStepGap, top);

            // Meter + text are one centered group inside the value box (CSS gap 12).
            const float textW = 72f;
            float groupX = (PrefsValWideW - (PrefsMeterW + 12f + textW)) * 0.5f;

            meter = SegmentBar(box.transform, "Meter");
            PlaceMiddleLeft(meter.rectTransform, groupX, PrefsMeterW, PrefsMeterH);
            meter.SetMetrics(PrefsMeterCell, PrefsMeterGap, 16f);
            meter.SetTrack(Girdap.Track);
            meter.SetFillColors(Girdap.OnA, Girdap.OnB);
            meter.SetFill(0.7f);

            value = Text(box.transform, "Value", "%70", GirdapFont.SairaBold, 24f, Girdap.Text,
                TextAlignmentOptions.MidlineLeft);
            PlaceMiddleLeft(value.rectTransform, groupX + PrefsMeterW + 12f, textW, PrefsCtrlH);
        }

        /// <summary>CSS <c>.bigseg</c>: equal columns of 64 px plates below a section rule.</summary>
        private static UiButtonStyle[] PrefsBigSeg(RectTransform col, ref float y, string[] labels,
            float width, string[] icons)
        {
            y += 16f;
            var result = new UiButtonStyle[labels.Length];
            float itemW = (width - 8f * (labels.Length - 1)) / labels.Length;

            for (int i = 0; i < labels.Length; i++)
            {
                result[i] = Button(col, "Big" + i, labels[i], UiButtonKind.Normal,
                    i * (itemW + 8f), y, itemW, 64f, icons != null ? icons[i] : null, null, 21f,
                    GirdapFont.ChakraBold, 12f);
                result[i].Label.characterSpacing = Girdap.Spacing(0.12f);
            }

            y += 64f;
            PrefsGrow(col, y);
            return result;
        }

        /// <summary>CSS <c>.track</c> / <c>.conn</c>: chamfered plate with a 16 px top margin.</summary>
        private static UiShape PrefsPlate(RectTransform col, ref float y, float h)
        {
            y += 16f;
            UiShape plate = Shape(col, "Plate");
            Place(plate.rectTransform, 0f, y, col.sizeDelta.x, h);
            plate.Chamfer(10f).Outline(1f, PrefsPlateBd).Fill(Girdap.PanelB);
            y += h;
            PrefsGrow(col, y);
            return plate;
        }

        private static UiShape PrefsValueBox(RectTransform row, float w, float right, float top)
        {
            UiShape box = Shape(row, "Val");
            PlaceRight(box.rectTransform, right, top, w, PrefsCtrlH);
            box.Chamfer(8f).Outline(1f, PrefsValBd).Fill(Girdap.PanelB);
            return box;
        }

        private static UiButtonStyle PrefsArrow(RectTransform row, string name, string icon,
            float right, float top)
        {
            UiButtonStyle button = Button(row, name, "", UiButtonKind.Normal, 0f, 0f,
                PrefsStepBtnW, PrefsCtrlH, icon);
            PlaceRight(button.GetComponent<RectTransform>(), right, top, PrefsStepBtnW, PrefsCtrlH);
            PrefsCenterIcon(button, 20f, PrefsStepBtnW);
            return button;
        }

        /// <summary>Re-centers an icon-only button's glyph: <see cref="Button"/> centers the
        /// icon+label GROUP, and an empty label leaves the glyph off-center by half a gap.</summary>
        private static UiImage PrefsCenterIcon(UiButtonStyle button, float size, float w)
        {
            Transform icon = button.transform.Find("Icon");
            if (icon == null)
            {
                return null;
            }

            var image = icon.GetComponent<UiImage>();
            PlaceMiddleLeft(image.rectTransform, (w - size) * 0.5f, size, size);
            return image;
        }

        // ------------------------------------------------------------------ wiring

        private static void PrefsBind(SerializedObject so, string field, Object value)
        {
            SerializedProperty property = so.FindProperty(field);
            if (property == null)
            {
                Debug.LogError($"[Girdap] AdminPreferencesPanel.{field} alanı yok.");
                return;
            }

            property.objectReferenceValue = value;
        }

        private static void PrefsBindArray(SerializedObject so, string field, Object[] values)
        {
            SerializedProperty property = so.FindProperty(field);
            if (property == null)
            {
                Debug.LogError($"[Girdap] AdminPreferencesPanel.{field} dizisi yok.");
                return;
            }

            property.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
            {
                property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            }
        }
    }
}
