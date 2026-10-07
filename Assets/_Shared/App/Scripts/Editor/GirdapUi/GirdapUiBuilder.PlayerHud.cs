using TMPro;
using UnityEditor;
using UnityEngine;
using VortexArena.Core.UI;

// `Image x = Image(...)` gives CS0119 — simple name lookup finds the member group before the type.
using UiImage = UnityEngine.UI.Image;

namespace VortexArena.App.Editor
{
    /// <summary>
    /// Player's in-match HUD (<c>Docs/Gelistirici/Arayuz/oyuncu-hud.html</c>): the head-locked strip
    /// <c>HealthHud</c> (<c>.vhud</c>: clock · health bar · score band · status line · round banner)
    /// and the death card <c>DeathHud</c> (<c>.vdead</c> + <c>.dcard</c>).
    /// <para>
    /// ⚠️ <b>Edited IN PLACE, two nodes kept.</b> The five mode HUD prefabs nest these two as
    /// instances; mode controllers point at <c>RoundScore</c> (<see cref="TeamScorePanel"/>) and
    /// <c>RoundResult</c> (<see cref="RoundResultBanner"/>), and the kids' modes add their own texts
    /// under <c>RoundScore/Panel</c>. Those nodes survive, everything else is rebuilt, and every mode
    /// HUD's <see cref="ModeHudBase"/> fields are re-bound by <see cref="BindModeHuds"/>.
    /// </para>
    /// </summary>
    public static partial class GirdapUiBuilder
    {
        private const string StripPrefab = "HealthHud";
        private const string DeathPrefab = "DeathHud";

        // .vhud — 560 wide in HUD canvas units; y measured from the health bar's centre, which is
        // the strip's pivot (HeadLockedHud aims at the prefab root).
        private const float StripW = 560f;
        private const float StripGap = 12f;
        private const float ClockW = 150f;
        private const float ClockH = 40f;
        private const float HpBarH = 64f;
        private const float ScoreH = 52f;
        private const float ScoreTeamW = 170f;
        private const float ScoreCellW = 110f;
        private const float ScoreSlant = 18f;
        private const float RoundChipH = 22f;
        private const float RoundChipW = 74f;
        private const float BannerH = 60f;
        private const float StatusH = 36f;

        private const float ClockY = -(HpBarH * 0.5f + StripGap + ClockH * 0.5f); // PlaceCenter: +y is down
        private const float ScoreY = HpBarH * 0.5f + StripGap + ScoreH * 0.5f;
        private const float StatusY = ScoreY + ScoreH * 0.5f + StripGap + StatusH * 0.5f;
        private const float BannerY = StatusY + StatusH * 0.5f + StripGap + BannerH * 0.5f;

        // .dcard — 640 wide inside the 900x520 mode canvas
        private const float DeathCardW = 640f;
        private const float DeathCardH = 302f;
        private const float DeathChamfer = 20f;

        private static readonly Color PhBdA = Girdap.Rgba(125, 227, 255, 0.55f);
        private static readonly Color PhBdB = Girdap.Rgba(170, 205, 255, 0.14f);
        private static readonly Color PhBannerA = Girdap.Hex(0x0C1828);
        private static readonly Color PhBannerB = Girdap.Hex(0x0A1422);
        private static readonly Color PhStatusBd = Girdap.Rgba(255, 197, 61, 0.45f);
        private static readonly Color PhDeathA = Girdap.Hex(0x141424);
        private static readonly Color PhDeathB = Girdap.Hex(0x080A1A);
        private static readonly Color PhDots = new Color(1f, 1f, 1f, 0.045f);
        private static readonly Color PhVignetteEdge = Girdap.Rgba(40, 0, 0, 0.80f);
        private static readonly Color PhVignetteMid = Girdap.Rgba(120, 20, 20, 0.08f);

        [MenuItem("Tools/VortexArena/UI/Girdap/Yalnız Oyuncu HUD")]
        public static void BuildPlayerHudOnly()
        {
            BuildAssets();
            BuildPlayerHud();
            AssetDatabase.SaveAssets();
        }

        static partial void BuildPlayerHud()
        {
            BuildHealthStrip();
            BuildDeathCard();
            BindModeHuds();
        }

        // ------------------------------------------------------------ HealthHud (.vhud)

        private static void BuildHealthStrip()
        {
            GameObject root = LoadPrefabContents(StripPrefab);
            if (root == null)
            {
                Debug.LogWarning($"[Girdap] {StripPrefab}.prefab yok — elle kurulu kök (Canvas + HeadLockedHud) gerekir.");
                return;
            }

            Transform t = root.transform;
            VhudKeepOnly(t, "RoundScore", "RoundResult");

            // .vclock — above the bar; ModeHudBase shows the frame only while a time is set
            RectTransform clock = Node(t, "Clock");
            PlaceCenter(clock, 0f, ClockY, ClockW, ClockH);
            UiShape clockPlate = Shape(clock, "Plate");
            Stretch(clockPlate.rectTransform);
            clockPlate.Chamfer(7f).Outline(1f, Girdap.EdgeBar)
                .Fill(Girdap.ClockA, Girdap.ClockB, UiGradientMode.Vertical);
            HudEdgeBar(clock, "Topline", false, 2f, Girdap.Acc); // inset 0 2px 0 var(--acc)
            TextMeshProUGUI time = Text(clock, "Time", "00:00", GirdapFont.ChakraBold, 26f,
                Girdap.AccHi, TextAlignmentOptions.Center, 0.04f);
            Stretch(time.rectTransform);
            TextGlow(time, Girdap.Glow); // text-shadow: 0 0 14px var(--glow)
            clock.gameObject.SetActive(false);

            // .hpbar — the strip's pivot
            RectTransform hpBar = Node(t, "HpBar");
            PlaceCenter(hpBar, 0f, 0f, StripW, HpBarH);
            UiShape hpPlate = Shape(hpBar, "Plate");
            Stretch(hpPlate.rectTransform);
            hpPlate.Chamfer(10f).Outline(1f, PhBdA, PhBdB)
                .Fill(Girdap.PlateA, Girdap.PlateB, UiGradientMode.Vertical);
            UiImage heart = Icon(hpBar, "Icon", "Heart", 18f, Girdap.Muted);
            PlaceMiddleLeft(heart.rectTransform, 16f, 18f, 18f);
            TextMeshProUGUI label = Text(hpBar, "Label", "CAN", GirdapFont.ChakraBold, 16f,
                Girdap.Muted, TextAlignmentOptions.MidlineLeft, 0.16f);
            PlaceMiddleLeft(label.rectTransform, 40f, 56f, HpBarH);
            TextMeshProUGUI value = Text(hpBar, "Value", "100", GirdapFont.SairaExtraBold, 44f,
                Girdap.Text, TextAlignmentOptions.MidlineRight);
            PlaceRight(value.rectTransform, 18f, 0f, 64f, HpBarH);
            Color badHalo = Girdap.Bad;
            badHalo.a = 0f; // HealthStrip raises it to .55 under 20 hp
            TextGlow(value, badHalo);
            UiSegmentBar bar = SegmentBar(hpBar, "Bar");
            const float barX = 100f; // 16 pad + 18 icon + 6 gap + "CAN" + 14 gap
            PlaceMiddleLeft(bar.rectTransform, barX, StripW - barX - 14f - 64f - 18f, 22f);
            bar.SetMetrics(14f, 4f, 24f); // .hpbar .hp mask: 14 px tick + 4 px gap
            bar.SetTrack(Girdap.Track);
            bar.SetFillColors(Girdap.HpA, Girdap.Good);
            bar.SetFill(1f);
            var strip = hpBar.gameObject.AddComponent<HealthStrip>();
            var stripSo = new SerializedObject(strip);
            HudSet(stripSo, "bar", bar);
            HudSet(stripSo, "value", value);
            HudSet(stripSo, "label", label);
            HudSet(stripSo, "icon", heart);
            stripSo.ApplyModifiedPropertiesWithoutUndo();

            // .scorebug.hud — kept node (mode controllers point at its TeamScorePanel)
            RectTransform score = VhudClaim(t, "RoundScore");
            PlaceCenter(score, 0f, ScoreY, StripW, ScoreH);
            var scorePanel = score.GetComponent<TeamScorePanel>();
            if (scorePanel == null)
            {
                scorePanel = score.gameObject.AddComponent<TeamScorePanel>();
            }

            RectTransform scoreContent = VhudClaim(score, "Panel");
            Stretch(scoreContent);
            VhudKeepOnly(scoreContent); // kids' modes' own texts live in THEIR prefabs, not here
            var oldPanelImage = scoreContent.GetComponent<UiImage>();
            if (oldPanelImage != null)
            {
                Object.DestroyImmediate(oldPanelImage);
            }

            UiShape shadow = Shape(scoreContent, "Shadow"); // drop-shadow(0 4px 12px rgba(0,0,0,.6))
            Stretch(shadow.rectTransform);
            shadow.Chamfer(0f).Fill(Girdap.Rgba(0, 0, 0, 0f))
                .Glow(12f, Girdap.Rgba(0, 0, 0, 0.6f), new Vector2(0f, -4f)).Antialias(false);

            VhudTeamPlate(scoreContent, "TeamRed", "KIRMIZI", 0f, true);
            TextMeshProUGUI redScore = VhudScoreCell(scoreContent, "ScoreRed", ScoreTeamW, Girdap.Red);
            TextMeshProUGUI blueScore = VhudScoreCell(scoreContent, "ScoreBlue",
                ScoreTeamW + ScoreCellW, Girdap.Blue);
            VhudTeamPlate(scoreContent, "TeamBlue", "MAVİ", StripW - ScoreTeamW, false);

            // .round — "TUR 3" chip riding the band's top edge; TeamScorePanel hides it when empty
            RectTransform chip = Node(scoreContent, "RoundFrame");
            PlaceTopCenter(chip, -RoundChipH * 0.5f, RoundChipW, RoundChipH);
            UiShape chipBg = Shape(chip, "Bg");
            Stretch(chipBg.rectTransform);
            chipBg.Chamfer(4f).Fill(Girdap.OnA, Girdap.OnB, UiGradientMode.Vertical);
            TextMeshProUGUI round = Text(chip, "Round", "TUR 1", GirdapFont.ChakraBold, 12f,
                Girdap.OnFg, TextAlignmentOptions.Center, 0.14f);
            Stretch(round.rectTransform);

            var scoreSo = new SerializedObject(scorePanel);
            HudSet(scoreSo, "content", scoreContent.gameObject);
            HudSet(scoreSo, "roundText", round);
            HudSet(scoreSo, "roundFrame", chip.gameObject);
            HudSet(scoreSo, "redScoreText", redScore);
            HudSet(scoreSo, "blueScoreText", blueScore);
            scoreSo.ApplyModifiedPropertiesWithoutUndo();

            // .vstat — under the band; sized by StatusPlate at runtime
            RectTransform status = VhudStatusPlate(t, "Status");
            PlaceCenter(status, 0f, StatusY, StripW, StatusH);

            // .rres — kept node (own Canvas with override sorting + RoundResultBanner)
            RectTransform banner = VhudClaim(t, "RoundResult");
            PlaceCenter(banner, 0f, BannerY, StripW, BannerH);
            var bannerComp = banner.GetComponent<RoundResultBanner>();
            if (bannerComp == null)
            {
                bannerComp = banner.gameObject.AddComponent<RoundResultBanner>();
            }

            RectTransform bannerContent = VhudClaim(banner, "Panel");
            Stretch(bannerContent);
            VhudKeepOnly(bannerContent);
            var oldBannerImage = bannerContent.GetComponent<UiImage>();
            if (oldBannerImage != null)
            {
                Object.DestroyImmediate(oldBannerImage);
            }

            UiShape bannerPlate = Shape(bannerContent, "Plate");
            Stretch(bannerPlate.rectTransform);
            bannerPlate.Chamfer(8f).Outline(1f, Girdap.Rgba(62, 230, 160, 0.6f))
                .Fill(PhBannerA, PhBannerB, UiGradientMode.Vertical);
            UiStripes bannerStripes = Stripes(bannerContent, "Stripes");
            Stretch(bannerStripes.rectTransform, 1f, 1f, 1f, 1f);
            bannerStripes.Stripes(-55f, 10f, 30f, new Color(1f, 1f, 1f, 0.05f)).Drift(4f);
            VhudChamfer(bannerStripes, 7f);
            UiShape underline = Shape(bannerContent, "Underline"); // inset 0 -3px 0 rgb(--k)
            StretchBottom(underline.rectTransform, 8f, 1f, 8f, 3f);
            underline.Chamfer(0f).Fill(Girdap.Good).Antialias(false);
            TextMeshProUGUI bannerLabel = Text(bannerContent, "Label", "TUR KAZANILDI",
                GirdapFont.SairaExtraBold, 34f, Girdap.Good, TextAlignmentOptions.Center, 0.08f);
            Stretch(bannerLabel.rectTransform);
            Color bannerHalo = Girdap.Good;
            bannerHalo.a = 0.55f;
            TextGlow(bannerLabel, bannerHalo); // text-shadow: 0 0 18px rgba(tone, .55)

            var bannerSo = new SerializedObject(bannerComp);
            HudSet(bannerSo, "content", bannerContent.gameObject);
            HudSet(bannerSo, "label", bannerLabel);
            HudSet(bannerSo, "plate", bannerPlate);
            HudSet(bannerSo, "underline", underline);
            ResultSetColor(bannerSo, "wonColor", Girdap.Good);
            ResultSetColor(bannerSo, "lostColor", Girdap.Bad);
            ResultSetColor(bannerSo, "drawColor", Girdap.Acc);
            bannerSo.ApplyModifiedPropertiesWithoutUndo();

            clock.SetSiblingIndex(0);
            hpBar.SetSiblingIndex(1);
            score.SetSiblingIndex(2);
            status.SetSiblingIndex(3);
            banner.SetAsLastSibling();

            SaveAndUnload(root, StripPrefab);
        }

        /// <summary>CSS <c>.scorebug.hud .sb-team</c>: slanted team gradient with drifting stripes.</summary>
        private static void VhudTeamPlate(Transform parent, string name, string text, float x, bool red)
        {
            UiShape plate = Shape(parent, name);
            Place(plate.rectTransform, x, 0f, ScoreTeamW, ScoreH);
            // clip-path: red cuts its bottom-LEFT corner, blue its bottom-RIGHT one.
            plate.Chamfer(0f).Slant(red ? ScoreSlant : 0f, red ? 0f : ScoreSlant)
                .Fill(red ? Girdap.RedHi : Girdap.BlueHi, red ? Girdap.RedLo : Girdap.BlueLo,
                    UiGradientMode.Vertical).Antialias(false);

            UiStripes stripes = Stripes(plate.transform, "Stripes");
            Stretch(stripes.rectTransform);
            stripes.SlantLeft = red ? ScoreSlant : 0f;
            stripes.SlantRight = red ? 0f : ScoreSlant;
            stripes.Stripes(-55f, 6f, 17f, Girdap.StripeInk).Drift(2.4f); // CSS: animation drift 2.4s

            // padding-left (red) / padding-right (blue) 14 px
            TextMeshProUGUI label = Text(plate.transform, "Label", text, GirdapFont.ChakraBold, 20f,
                Color.white, TextAlignmentOptions.Center, 0.12f);
            Stretch(label.rectTransform, red ? 14f : 0f, 0f, red ? 0f : 14f, 0f);
        }

        /// <summary>CSS <c>.scorebug.hud .sb-score</c> + its 4 px team underline.</summary>
        private static TextMeshProUGUI VhudScoreCell(Transform parent, string name, float x, Color team)
        {
            UiShape cell = Shape(parent, name);
            Place(cell.rectTransform, x, 0f, ScoreCellW, ScoreH);
            cell.Chamfer(0f).Fill(Girdap.ScoreA, Girdap.ScoreB, UiGradientMode.Vertical)
                .Antialias(false);
            HudEdgeBar(cell.transform, "Underline", true, 4f, team);
            TextMeshProUGUI value = Text(cell.transform, "Value", "0", GirdapFont.ChakraBold, 40f,
                Girdap.Text, TextAlignmentOptions.Center);
            Stretch(value.rectTransform);
            return value;
        }

        /// <summary>CSS <c>.vstat</c>: StatusPlate root + Body (plate · icon · text), Body off until a line is set.</summary>
        private static RectTransform VhudStatusPlate(Transform parent, string name)
        {
            RectTransform rootRt = Node(parent, name);
            PlaceCenter(rootRt, 0f, 0f, StripW, StatusH);

            RectTransform body = Node(rootRt, "Body");
            PlaceCenter(body, 0f, 0f, StripW, StatusH);
            UiShape plate = Shape(body, "Plate");
            Stretch(plate.rectTransform);
            plate.Chamfer(6f).Outline(1f, PhStatusBd)
                .Fill(Girdap.PlateA, Girdap.PlateB, UiGradientMode.Vertical);
            UiImage icon = Icon(body, "Icon", "Warn", 18f, Girdap.Warn);
            PlaceMiddleLeft(icon.rectTransform, 12f, 18f, 18f);
            TextMeshProUGUI text = Text(body, "Text", "", GirdapFont.ChakraSemiBold, 16f,
                Girdap.Warn, TextAlignmentOptions.MidlineLeft, 0.02f);
            Stretch(text.rectTransform, 38f, 0f, 14f, 0f);

            var comp = rootRt.gameObject.AddComponent<StatusPlate>();
            var so = new SerializedObject(comp);
            HudSet(so, "body", body.gameObject);
            HudSet(so, "plate", body);
            HudSet(so, "icon", icon);
            HudSet(so, "text", text);
            so.ApplyModifiedPropertiesWithoutUndo();
            body.gameObject.SetActive(false);
            return rootRt;
        }

        // ------------------------------------------------------------ DeathHud (.vdead + .dcard)

        private static void BuildDeathCard()
        {
            GameObject root = LoadPrefabContents(DeathPrefab);
            if (root == null)
            {
                Debug.LogWarning($"[Girdap] {DeathPrefab}.prefab yok — elle kurulu kök (Canvas) gerekir.");
                return;
            }

            Transform t = root.transform;
            VhudKeepOnly(t);

            // .vdead — red vignette over the whole mode canvas (inner glow = darker rim)
            UiShape vignette = Shape(t, "Vignette");
            Stretch(vignette.rectTransform);
            vignette.Chamfer(0f).Fill(PhVignetteMid).InnerGlow(260f, PhVignetteEdge).Antialias(false);

            // .dcard
            RectTransform card = Node(t, "Card");
            PlaceCenter(card, 0f, -20f, DeathCardW, DeathCardH);
            UiShape cardBd = Shape(card, "Bd");
            Stretch(cardBd.rectTransform);
            cardBd.Chamfer(DeathChamfer).Outline(1f, Girdap.Rgba(255, 87, 71, 0.65f), PhBdB)
                .Fill(PhDeathA, PhDeathB, UiGradientMode.Vertical);
            UiImage dots = Image(card, "Dots", LoadSprite("Dots_16.png"), PhDots, UiImage.Type.Tiled);
            Stretch(dots.rectTransform, DeathChamfer, DeathChamfer, DeathChamfer, DeathChamfer);
            UiStripes stripes = Stripes(card, "Stripes");
            Stretch(stripes.rectTransform, 1f, 1f, 1f, 1f);
            stripes.Stripes(-55f, 10f, 30f, new Color(1f, 1f, 1f, 0.035f)).Drift(4f); // CSS: animation drift 4s
            VhudChamfer(stripes, DeathChamfer - 1f);
            UiShape underline = Shape(card, "Underline"); // inset 0 -3px 0 rgb(--k)
            StretchBottom(underline.rectTransform, DeathChamfer, 1f, DeathChamfer, 3f);
            underline.Chamfer(0f).Fill(Girdap.Bad).Antialias(false);

            // .dcard small — "MAÇ SÜRÜYOR" between two 90x2 fading rules
            Color smallColor = Girdap.Text;
            smallColor.a = 0.8f;
            TextMeshProUGUI small = Text(card, "Small", "MAÇ SÜRÜYOR", GirdapFont.ChakraSemiBold, 14f,
                smallColor, TextAlignmentOptions.Center, 0.5f);
            float smallW = Mathf.Ceil(small.GetPreferredValues(small.text).x) + 8f;
            PlaceTopCenter(small.rectTransform, 22f, smallW, 16f);
            Color fade = Girdap.Bad;
            fade.a = 0f;
            float ruleOffset = smallW * 0.5f + 14f + 45f;
            UiShape ruleLeft = Shape(card, "RuleLeft");
            PlaceTopCenter(ruleLeft.rectTransform, 29f, 90f, 2f);
            ruleLeft.rectTransform.anchoredPosition += new Vector2(-ruleOffset, 0f);
            ruleLeft.Chamfer(0f).Fill(fade, Girdap.Bad, UiGradientMode.Horizontal).Antialias(false);
            UiShape ruleRight = Shape(card, "RuleRight");
            PlaceTopCenter(ruleRight.rectTransform, 29f, 90f, 2f);
            ruleRight.rectTransform.anchoredPosition += new Vector2(ruleOffset, 0f);
            ruleRight.Chamfer(0f).Fill(Girdap.Bad, fade, UiGradientMode.Horizontal).Antialias(false);

            // .dcard b — 120 px word, white→red vertex gradient, red halo (CSS skewX(-7deg) skipped:
            // TMP italic shear comes from the shared font asset, not per text)
            TextMeshProUGUI word = Text(card, "Title", "ÖLDÜN", GirdapFont.SairaExtraBold, 120f,
                Color.white, TextAlignmentOptions.Center, 0.01f);
            PlaceTopCenter(word.rectTransform, 50f, DeathCardW - 40f, 132f);
            word.enableVertexGradient = true;
            word.colorGradient = new VertexGradient(Color.white, Color.white, Girdap.Bad, Girdap.Bad);
            Color wordHalo = Girdap.Bad;
            wordHalo.a = 0.55f;
            TextGlow(word, wordHalo); // drop-shadow(0 0 24px rgba(tone, .55))

            // .dline — killer line; rich text ON for the team-inked name (ModeHudBase wraps it in <noparse>)
            TextMeshProUGUI killer = Text(card, "KillerLine", "", GirdapFont.ChakraSemiBold, 22f,
                Girdap.Text, TextAlignmentOptions.Center, 0.02f);
            PlaceTopCenter(killer.rectTransform, 196f, DeathCardW - 40f, 26f);
            killer.richText = true;

            // .vstat inside the card
            RectTransform status = VhudStatusPlate(card, "StatusFrame");
            PlaceTopCenter(status, 236f, DeathCardW - 80f, StatusH);

            SaveAndUnload(root, DeathPrefab);
        }

        // ------------------------------------------------------------ mode HUD wiring

        /// <summary>
        /// Re-binds every mode HUD prefab (<c>Assets/Modes/**</c>, root <see cref="ModeHudBase"/>) to
        /// the rebuilt strip/card nodes. The kids' modes (no health) get the bar switched off and their
        /// own added texts restyled — the overrides they carried pointed at nodes that no longer exist.
        /// </summary>
        private static void BindModeHuds()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Modes" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (asset == null || asset.GetComponent<ModeHudBase>() == null)
                {
                    continue;
                }

                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    BindModeHud(root);
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }
        }

        private static void BindModeHud(GameObject root)
        {
            var hud = root.GetComponent<ModeHudBase>();
            var so = new SerializedObject(hud);
            Transform strip = root.transform.Find(StripPrefab);
            Transform death = root.transform.Find(DeathPrefab);
            string mode = hud.GetType().Name; // mode assemblies are not referenced from here

            if (strip != null)
            {
                HudSet(so, "timeText", VhudFind<TextMeshProUGUI>(strip, "Clock/Time"));
                HudSet(so, "timeFrame", VhudFind<Transform>(strip, "Clock")?.gameObject);
                HudSet(so, "healthText", null); // HealthStrip writes the bare number itself
                HudSet(so, "healthFill", null);
                HudSet(so, "healthStrip", VhudFind<HealthStrip>(strip, "HpBar"));
                HudSet(so, "statusText", null);
                HudSet(so, "statusPlate", VhudFind<StatusPlate>(strip, "Status"));

                if (mode == "MoleClientController")
                {
                    BindMoleStrip(strip);
                }
                else if (mode == "BurgerClientController")
                {
                    BindBurgerStrip(strip);
                }
            }

            if (death != null)
            {
                HudSet(so, "deathKillerNameText", VhudFind<TextMeshProUGUI>(death, "Card/KillerLine"));
                HudSet(so, "deathStatusText", null);
                HudSet(so, "deathStatusPlate", VhudFind<StatusPlate>(death, "Card/StatusFrame"));
            }

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>Mole: no health — the band takes the bar's slot, the hit counters sit under it.</summary>
        private static void BindMoleStrip(Transform strip)
        {
            VhudSetActive(strip, "HpBar", false);
            RectTransform score = VhudFind<RectTransform>(strip, "RoundScore");
            if (score != null)
            {
                PlaceCenter(score, 0f, 0f, StripW, ScoreH);
            }

            const float hitsY = ScoreH * 0.5f + 8f + 12f;
            VhudRestyleModeText(strip, "RoundScore/Panel/RedHits", -96f, hitsY, TextAlignmentOptions.MidlineRight);
            VhudRestyleModeText(strip, "RoundScore/Panel/BlueHits", 96f, hitsY, TextAlignmentOptions.MidlineLeft);
            RectTransform status = VhudFind<RectTransform>(strip, "Status");
            if (status != null)
            {
                PlaceCenter(status, 0f, hitsY + 12f + StripGap + StatusH * 0.5f, StripW, StatusH);
            }
        }

        /// <summary>Burger: no health, no teams — the mode's own score plate takes the bar's slot.</summary>
        private static void BindBurgerStrip(Transform strip)
        {
            VhudSetActive(strip, "HpBar", false);
            VhudSetActive(strip, "RoundScore", false);
            RectTransform plate = VhudFind<RectTransform>(strip, "Score");
            if (plate != null)
            {
                PlaceCenter(plate, 0f, 0f, StripW, HpBarH);
                var image = plate.GetComponent<UiImage>();
                if (image != null)
                {
                    Object.DestroyImmediate(image);
                }

                var shape = plate.GetComponent<UiShape>();
                if (shape == null)
                {
                    shape = plate.gameObject.AddComponent<UiShape>();
                    shape.raycastTarget = false;
                }

                shape.Chamfer(10f).Outline(1f, PhBdA, PhBdB)
                    .Fill(Girdap.PlateA, Girdap.PlateB, UiGradientMode.Vertical);
                var value = VhudFind<TextMeshProUGUI>(plate, "Value");
                if (value != null)
                {
                    VhudRestyle(value, GirdapFont.ChakraBold, 26f, Girdap.Text, TextAlignmentOptions.Center, 0.06f);
                    Stretch(value.rectTransform);
                }
            }

            RectTransform status = VhudFind<RectTransform>(strip, "Status");
            if (status != null)
            {
                PlaceCenter(status, 0f, HpBarH * 0.5f + StripGap + StatusH * 0.5f, StripW, StatusH);
            }
        }

        private static void VhudRestyleModeText(Transform strip, string path, float dx, float dy,
            TextAlignmentOptions align)
        {
            var text = VhudFind<TextMeshProUGUI>(strip, path);
            if (text == null)
            {
                return;
            }

            VhudRestyle(text, GirdapFont.ChakraBold, 18f, Girdap.Text, align, 0.04f);
            PlaceCenter(text.rectTransform, dx, dy, 180f, 24f);
        }

        private static void VhudRestyle(TextMeshProUGUI text, GirdapFont font, float size, Color color,
            TextAlignmentOptions align, float letterSpacingEm)
        {
            TMP_FontAsset asset = LoadFontAsset(font);
            if (asset != null)
            {
                text.font = asset;
                text.fontSharedMaterial = asset.material;
            }

            text.fontSize = size;
            text.color = color;
            text.alignment = align;
            text.characterSpacing = Girdap.Spacing(letterSpacingEm);
            text.enableAutoSizing = false;
        }

        // ------------------------------------------------------------ helpers

        /// <summary>Destroys every child except the named ones (kept for the references into them).</summary>
        private static void VhudKeepOnly(Transform parent, params string[] keep)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                Transform child = parent.GetChild(i);
                if (System.Array.IndexOf(keep, child.name) < 0)
                {
                    Object.DestroyImmediate(child.gameObject);
                }
            }
        }

        /// <summary>Existing child by name, or a fresh node — a kept node's identity must survive.</summary>
        private static RectTransform VhudClaim(Transform parent, string name)
        {
            Transform existing = parent.Find(name);
            return existing != null ? (RectTransform)existing : Node(parent, name);
        }

        private static T VhudFind<T>(Transform parent, string path) where T : Component
        {
            Transform t = parent.Find(path);
            return t != null ? t.GetComponent<T>() : null;
        }

        private static void VhudSetActive(Transform parent, string path, bool active)
        {
            Transform t = parent.Find(path);
            if (t != null)
            {
                t.gameObject.SetActive(active);
            }
        }

        private static void VhudChamfer(UiStripes stripes, float c)
        {
            stripes.ChamferTopLeft = c;
            stripes.ChamferTopRight = c;
            stripes.ChamferBottomRight = c;
            stripes.ChamferBottomLeft = c;
        }
    }
}
