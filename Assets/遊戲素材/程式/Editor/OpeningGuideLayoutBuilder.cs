using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;
using G = PilgrimOfSin.OpeningGuidePanel;

namespace PilgrimOfSin.EditorTools
{
    /// <summary>
    /// 在 PauseCanvas.prefab 產生「開場操作引導」（OpeningGuide_Panel）。
    /// 版面照已定案的模擬頁 v6（1280×720），這裡 ×1.5 換成 1920×1080；風格沿用武器介紹（字型、底圖、天地線、雙線框、提示列、翻頁箭頭）。
    /// 可重複執行：每次先刪掉舊的 OpeningGuide_Panel 再重建（各行的示範影片 demoClip 會保留）。
    /// 選單：PilgrimOfSin/UI/重建開場操作引導
    /// </summary>
    public static class OpeningGuideLayoutBuilder
    {
        private const string PrefabPath = "Assets/遊戲素材/程式/畫面切換/PauseCanvas.prefab";
        private const string UiDir = "Assets/遊戲素材/UI圖檔/武器介紹/";
        private const string GlyphDir = "Assets/遊戲素材/UI圖檔/操作/";
        private const string RingPath = "Assets/遊戲素材/UI圖檔/操作引導/圓環.png";
        private const string BgPath = "Assets/遊戲素材/UI圖檔/玩家狀態_底圖.png";
        private const string FontPath = "Assets/遊戲素材/字型/京華老宋体v2.002 SDF.asset";
        private const string HoldPromptPath = "Assets/遊戲素材/SO/Prompts/長按開始戰鬥提示.asset";
        private const string SkipPromptPath = "Assets/遊戲素材/SO/Prompts/跳過機制說明提示.asset";
        private const string PanelName = "OpeningGuide_Panel";

        private static readonly Color Gold = new Color32(0xD9, 0xB3, 0x6A, 0xFF);
        private static readonly Color Cream = new Color32(0xEF, 0xE6, 0xD2, 0xFF);
        private static readonly Color Soft = new Color32(0xCF, 0xC4, 0xAD, 0xFF);
        private static readonly Color Muted = new Color32(0xA8, 0x9D, 0x86, 0xFF);

        private static TMP_FontAsset _font;

        [MenuItem("PilgrimOfSin/UI/重建開場操作引導")]
        public static void Build()
        {
            if (Application.isPlaying) { Debug.LogError("[OpeningGuideLayoutBuilder] 請先停止 Play Mode 再重建"); return; }
            _font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
            EnsureRingSprite();
            var holdPrompt = EnsureHoldPrompt();

            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                // 保留舊版各行已放的示範影片
                var oldClips = new System.Collections.Generic.Dictionary<string, VideoClip>();
                var old = root.transform.Find(PanelName);
                if (old != null)
                {
                    var oldPanel = old.GetComponent<G>();
                    if (oldPanel != null)
                    {
                        var oso = new SerializedObject(oldPanel);
                        var op = oso.FindProperty("_pages");
                        for (int p = 0; p < op.arraySize; p++)
                        {
                            var rows = op.GetArrayElementAtIndex(p).FindPropertyRelative("rows");
                            for (int r = 0; r < rows.arraySize; r++)
                            {
                                var e = rows.GetArrayElementAtIndex(r);
                                var clip = e.FindPropertyRelative("demoClip").objectReferenceValue as VideoClip;
                                if (clip != null) oldClips[e.FindPropertyRelative("name").stringValue] = clip;
                            }
                        }
                    }
                    Object.DestroyImmediate(old.gameObject);
                }

                // 根：自己一層 Canvas（排序 60），蓋過戰鬥 HUD 等其他畫布
                var panelT = NewStretch(PanelName, root.transform);
                panelT.SetAsLastSibling();
                var canvas = panelT.gameObject.AddComponent<Canvas>();
                canvas.overrideSorting = true;
                canvas.sortingOrder = 60;
                panelT.gameObject.AddComponent<GraphicRaycaster>();
                var panel = panelT.gameObject.AddComponent<G>();

                var content = NewStretch("Content", panelT);
                var group = content.gameObject.AddComponent<CanvasGroup>();

                var bg = Img("Background", content, AssetDatabase.LoadAssetAtPath<Sprite>(BgPath), Color.white);
                bg.raycastTarget = true; // 擋住後面的點擊
                StretchFull(bg.rectTransform);

                var title = Text("Title", content, "", 48f, Gold, TextAlignmentOptions.TopLeft);
                title.characterSpacing = 8f;
                title.richText = true;
                At(title.rectTransform, 108, 74, 800, 50);

                var indicator = Text("PageIndicator", content, "", 26f, Gold, TextAlignmentOptions.TopRight);
                indicator.richText = true;
                indicator.characterSpacing = 6f;
                var irt = indicator.rectTransform;
                irt.anchorMin = irt.anchorMax = irt.pivot = new Vector2(1f, 1f);
                irt.anchoredPosition = new Vector2(-210f, -118f);
                irt.sizeDelta = new Vector2(700f, 50f);

                Divider(content, 128);

                // 左：天地線
                var lines = New("ListLines", content);
                At((RectTransform)lines, 108, 190, 552, 360);
                var fade = Sprite("線_兩端淡出.png");
                HLine(lines, fade, 0f, 0.65f, true);
                HLine(lines, fade, 7.5f, 0.22f, true);
                HLine(lines, fade, 7.5f, 0.22f, false);
                HLine(lines, fade, 0f, 0.65f, false);

                var rowUIs = new G.RowUI[2];
                for (int i = 0; i < rowUIs.Length; i++) rowUIs[i] = BuildRow(content, i);

                var combosRoot = BuildCombos(content, out var comboUIs);

                var note = Text("Note", content, "", 22.5f, Gold, TextAlignmentOptions.TopLeft);
                note.characterSpacing = 3f;
                At(note.rectTransform, 136, 516, 700, 26);

                // 右：示範格
                var demo = New("DemoFrame", content);
                At((RectTransform)demo, 692, 190, 448, 360);
                var dbg = Img("Background", demo, Sprite("純白.png"), new Color32(0x1E, 0x16, 0x0E, 0xE6));
                StretchFull(dbg.rectTransform);
                var warm = Img("WarmLight", demo, Sprite("放射光.png"), new Color(1f, 0.84f, 0.59f, 0.12f));
                StretchFull(warm.rectTransform);
                var raw = New("DemoVideo", demo).gameObject.AddComponent<RawImage>();
                raw.raycastTarget = false;
                raw.enabled = false;
                StretchFull(raw.rectTransform);
                var vignette = Img("Vignette", demo, Sprite("展示框暗角.png"), Color.white);
                StretchFull(vignette.rectTransform);
                DoubleFrame(demo, 0.5f, 0.15f, 9f);
                var vp = demo.gameObject.AddComponent<VideoPlayer>();
                vp.playOnAwake = false;
                vp.renderMode = VideoRenderMode.RenderTexture;
                vp.audioOutputMode = VideoAudioOutputMode.None;
                vp.isLooping = true;

                var caption = Text("Caption", demo, "", 21f, Muted, TextAlignmentOptions.TopLeft);
                caption.characterSpacing = 6f;
                var crt = caption.rectTransform;
                crt.anchorMin = crt.anchorMax = crt.pivot = new Vector2(0f, 1f);
                crt.anchoredPosition = new Vector2(27f, -21f);
                crt.sizeDelta = new Vector2(500f, 32f);

                var placeholder = Text("Placeholder", demo, "（會放示意用影片）", 33f, Soft, TextAlignmentOptions.Center);
                placeholder.characterSpacing = 12f;
                StretchFull(placeholder.rectTransform);

                // 底部提示列（複製音量頁 VolumeNavHints：位置、字級、圖示完全一致）
                BuildHints(root.transform, content, holdPrompt, out var ring);

                // 翻頁箭頭（複製武器介紹的箭頭，位置一致）
                var weapon = FindDeep(root.transform, "WeaponInfo_Content");
                var leftArrow = CopyArrow(weapon, "LeftArrow", content);
                var rightArrow = CopyArrow(weapon, "RightArrow", content);

                Wire(panel, content.gameObject, group, title, indicator, leftArrow, rightArrow, rowUIs, combosRoot, comboUIs, note,
                    caption, placeholder.gameObject, raw, vp, ring, oldClips);

                content.gameObject.SetActive(false);
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                Debug.Log("[OpeningGuideLayoutBuilder] 開場操作引導已重建");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // ─────────────────────────── 清單列 ───────────────────────────

        private static G.RowUI BuildRow(Transform content, int index)
        {
            var row = New("Row" + index, content);
            var rt = (RectTransform)row;
            At(rt, 122, 190 + index * 108, 520, 100); // y 與高度由執行期依頁面重排

            var band = Img("Band", row, Sprite("帶狀_左濃右淡.png"), new Color(Gold.r, Gold.g, Gold.b, 0.24f));
            StretchFull(band.rectTransform);
            var bar = Img("Bar", row, Sprite("純白.png"), Gold);
            var brt = bar.rectTransform;
            brt.anchorMin = Vector2.zero; brt.anchorMax = new Vector2(0f, 1f); brt.pivot = new Vector2(0f, 0.5f);
            brt.anchoredPosition = Vector2.zero; brt.sizeDelta = new Vector2(4.5f, 0f);

            var glyphBox = New("GlyphBox", row);
            LocalLeft((RectTransform)glyphBox, 27f, 96f, 78f);
            var glyph = Img("Glyph", glyphBox, null, Color.white);
            glyph.preserveAspect = true;
            var grt = glyph.rectTransform;
            grt.anchorMin = grt.anchorMax = grt.pivot = new Vector2(0.5f, 0.5f);
            grt.anchoredPosition = Vector2.zero;

            var col = New("TextColumn", row);
            var colRt = (RectTransform)col;
            colRt.anchorMin = new Vector2(0f, 0f); colRt.anchorMax = new Vector2(0f, 1f); colRt.pivot = new Vector2(0f, 0.5f);
            colRt.anchoredPosition = new Vector2(150f, 0f);
            colRt.sizeDelta = new Vector2(603f, 0f);
            var vlg = col.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.childAlignment = TextAnchor.MiddleLeft;
            vlg.spacing = 6f;
            vlg.childControlWidth = true; vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;

            var name = Text("Name", col, "", 32f, Gold, TextAlignmentOptions.MidlineLeft);
            name.characterSpacing = 12f;
            var desc = Text("Desc", col, "", 22.5f, Soft, TextAlignmentOptions.MidlineLeft);
            desc.characterSpacing = 3f;
            var extra = Text("Extra", col, "", 19.5f, Muted, TextAlignmentOptions.MidlineLeft);
            extra.characterSpacing = 3f;

            // 第三行的「圖示＋文字」排列（切換武器：十字鍵四個方向對應的武器）
            var iconLine = New("IconLine", col);
            var le = iconLine.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = 40f;
            var hlg = iconLine.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.spacing = 6f;
            hlg.childControlWidth = true; hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false; hlg.childForceExpandHeight = false;
            var icons = new Image[4];
            var overlays = new Image[4];
            var labels = new TMP_Text[4];
            for (int k = 0; k < 4; k++)
            {
                icons[k] = Img("Icon" + k, iconLine, null, Color.white);
                icons[k].preserveAspect = true;
                var ile = icons[k].gameObject.AddComponent<LayoutElement>();
                ile.preferredWidth = 38f; ile.preferredHeight = 38f;
                overlays[k] = Img("Overlay", icons[k].transform, null, Color.white);
                overlays[k].preserveAspect = true;
                StretchFull(overlays[k].rectTransform);
                labels[k] = Text("Label" + k, iconLine, "", 19.5f, Muted, TextAlignmentOptions.MidlineLeft);
                labels[k].characterSpacing = 3f;
            }

            return new G.RowUI
            {
                root = rt, band = band, bar = bar, glyph = glyph, nameText = name, descText = desc, extraText = extra,
                iconLine = (RectTransform)iconLine, iconLineIcons = icons, iconLineOverlays = overlays, iconLineLabels = labels,
            };
        }

        // ─────────────────────────── 連段範例 ───────────────────────────

        private static RectTransform BuildCombos(Transform content, out G.ComboLineUI[] uis)
        {
            var combos = New("Combos", content);
            var rt = (RectTransform)combos;
            At(rt, 136, 394, 480, 110); // y 由執行期接在清單下方
            var vlg = combos.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.childAlignment = TextAnchor.UpperLeft;
            vlg.spacing = 10f;
            vlg.childControlWidth = true; vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;

            var label = Text("Label", combos, "連段範例", 21f, Muted, TextAlignmentOptions.MidlineLeft);
            label.characterSpacing = 6f;

            uis = new G.ComboLineUI[2];
            for (int i = 0; i < uis.Length; i++)
            {
                var line = New("Line" + i, combos);
                var le = line.gameObject.AddComponent<LayoutElement>();
                le.preferredHeight = 45f;
                var hlg = line.gameObject.AddComponent<HorizontalLayoutGroup>();
                hlg.childAlignment = TextAnchor.MiddleLeft;
                hlg.spacing = 9f;
                hlg.padding = new RectOffset(21, 0, 0, 0);
                hlg.childControlWidth = true; hlg.childControlHeight = true;
                hlg.childForceExpandWidth = false; hlg.childForceExpandHeight = false;
                var keys = new Image[4];
                for (int k = 0; k < keys.Length; k++)
                {
                    keys[k] = Img("Key" + k, line, null, Color.white);
                    keys[k].preserveAspect = true;
                    var kle = keys[k].gameObject.AddComponent<LayoutElement>();
                    kle.preferredWidth = 39f; kle.preferredHeight = 39f;
                }
                var gap = New("Gap", line).gameObject.AddComponent<LayoutElement>();
                gap.preferredWidth = 9f;
                var text = Text("Text", line, "", 22.5f, Soft, TextAlignmentOptions.MidlineLeft);
                text.characterSpacing = 3f;
                uis[i] = new G.ComboLineUI { root = (RectTransform)line, keys = keys, text = text };
            }
            return rt;
        }

        // ─────────────────────────── 提示列 ───────────────────────────

        private static void BuildHints(Transform root, Transform content, InputPromptData holdPrompt, out Image ring)
        {
            var src = FindDeep(root, "VolumeNavHints");
            var copy = Object.Instantiate(src.gameObject, content, false);
            copy.name = "GuideNavHints";
            copy.SetActive(true);

            // 左：↑↓ 選擇項目（同音量頁／武器介紹的 SelectHint，原樣沿用）
            var select = copy.transform.Find("SelectHint");
            SetPrompt(select.GetComponent<InputPromptUI>(), "選擇項目提示");
            SetPrompt(select.Find("Icon_First")?.GetComponent<InputPromptUI>(), "上鍵提示");

            // 中：←→ 翻頁（同武器介紹）
            var adjust = copy.transform.Find("AdjustHint");
            adjust.name = "FlipHint";
            SetPrompt(adjust.GetComponent<InputPromptUI>(), "翻頁提示");
            SetPrompt(adjust.Find("Icon_First")?.GetComponent<InputPromptUI>(), "左翻提示");

            // 右：Options 圖示外圍一圈進度環＋「長按 開始戰鬥」
            var ret = copy.transform.Find("ReturnHint");
            ret.name = "HoldHint";
            var ui = ret.GetComponent<InputPromptUI>();
            var so = new SerializedObject(ui);
            so.FindProperty("_data").objectReferenceValue = holdPrompt;
            so.ApplyModifiedPropertiesWithoutUndo();
            // 「長按 開始戰鬥」比原本的「返回」長：文字框要加寬，不然字會超出右邊（版面組不控制子物件寬度）
            var holdText = ret.Find("Text").GetComponent<TextMeshProUGUI>();
            holdText.text = holdPrompt.PromptText;
            holdText.rectTransform.sizeDelta = new Vector2(Mathf.Ceil(holdText.GetPreferredValues().x) + 4f, holdText.rectTransform.sizeDelta.y);

            var icon = ret.Find("Icon");
            var ringSprite = AssetDatabase.LoadAssetAtPath<Sprite>(RingPath);
            var track = Img("RingTrack", icon, ringSprite, new Color(1f, 1f, 1f, 0.18f));
            CenterSquare(track.rectTransform, 60f);
            ring = Img("RingFill", icon, ringSprite, new Color32(0xE2, 0xC2, 0x7D, 0xFF));
            ring.type = Image.Type.Filled;
            ring.fillMethod = Image.FillMethod.Radial360;
            ring.fillOrigin = (int)Image.Origin360.Top;
            ring.fillClockwise = true;
            ring.fillAmount = 0f;
            CenterSquare(ring.rectTransform, 60f);
        }

        private static void CenterSquare(RectTransform rt, float size)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(size, size);
        }

        private static Button CopyArrow(Transform weapon, string name, Transform content)
        {
            var src = weapon != null ? weapon.Find(name) : null;
            if (src == null) { Debug.LogWarning($"[OpeningGuideLayoutBuilder] 找不到武器介紹的 {name}"); return null; }
            var copy = Object.Instantiate(src.gameObject, content, false);
            copy.name = name;
            copy.SetActive(true);
            copy.transform.SetAsLastSibling();
            var btn = copy.GetComponent<Button>();
            if (btn != null) btn.onClick = new Button.ButtonClickedEvent();
            return btn;
        }

        // ─────────────────────────── 接線＋頁面資料 ───────────────────────────

        private static void Wire(G panel, GameObject content, CanvasGroup group, TMP_Text title, TMP_Text indicator, Button left, Button right,
            G.RowUI[] rows, RectTransform combosRoot, G.ComboLineUI[] combos, TMP_Text note,
            TMP_Text caption, GameObject placeholder, RawImage raw, VideoPlayer vp, Image ring,
            System.Collections.Generic.Dictionary<string, VideoClip> oldClips)
        {
            var so = new SerializedObject(panel);
            so.FindProperty("_content").objectReferenceValue = content;
            so.FindProperty("_contentGroup").objectReferenceValue = group;
            so.FindProperty("_titleText").objectReferenceValue = title;
            so.FindProperty("_pageIndicator").objectReferenceValue = indicator;
            so.FindProperty("_leftArrow").objectReferenceValue = left;
            so.FindProperty("_rightArrow").objectReferenceValue = right;
            so.FindProperty("_combosRoot").objectReferenceValue = combosRoot;
            so.FindProperty("_noteText").objectReferenceValue = note;
            so.FindProperty("_demoCaption").objectReferenceValue = caption;
            so.FindProperty("_demoPlaceholder").objectReferenceValue = placeholder;
            so.FindProperty("_demoVideo").objectReferenceValue = raw;
            so.FindProperty("_videoPlayer").objectReferenceValue = vp;
            so.FindProperty("_holdRing").objectReferenceValue = ring;

            var rp = so.FindProperty("_rowUIs");
            rp.arraySize = rows.Length;
            for (int i = 0; i < rows.Length; i++)
            {
                var e = rp.GetArrayElementAtIndex(i);
                var r = rows[i];
                e.FindPropertyRelative("root").objectReferenceValue = r.root;
                e.FindPropertyRelative("band").objectReferenceValue = r.band;
                e.FindPropertyRelative("bar").objectReferenceValue = r.bar;
                e.FindPropertyRelative("glyph").objectReferenceValue = r.glyph;
                e.FindPropertyRelative("nameText").objectReferenceValue = r.nameText;
                e.FindPropertyRelative("descText").objectReferenceValue = r.descText;
                e.FindPropertyRelative("extraText").objectReferenceValue = r.extraText;
                e.FindPropertyRelative("iconLine").objectReferenceValue = r.iconLine;
                SetObjArray(e.FindPropertyRelative("iconLineIcons"), r.iconLineIcons);
                SetObjArray(e.FindPropertyRelative("iconLineOverlays"), r.iconLineOverlays);
                SetObjArray(e.FindPropertyRelative("iconLineLabels"), r.iconLineLabels);
            }

            var cp = so.FindProperty("_comboUIs");
            cp.arraySize = combos.Length;
            for (int i = 0; i < combos.Length; i++)
            {
                var e = cp.GetArrayElementAtIndex(i);
                e.FindPropertyRelative("root").objectReferenceValue = combos[i].root;
                e.FindPropertyRelative("text").objectReferenceValue = combos[i].text;
                SetObjArray(e.FindPropertyRelative("keys"), combos[i].keys);
            }

            WritePages(so.FindProperty("_pages"), oldClips);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private struct K
        {
            public string normal, lit; public float w, h;
            public K(string n, string l, float w, float h) { normal = n; lit = l; this.w = w; this.h = h; }
        }

        // 按鍵圖大小照模擬頁（×1.5）
        private static readonly K StickL = new K("搖桿L.png", "搖桿L-紅色.png", 44, 44);
        private static readonly K StickR = new K("搖桿R.png", "搖桿R-紅色.png", 44, 44);
        private static readonly K L1 = new K("L1.png", "L1-紅色.png", 56, 23);
        private static readonly K L2 = new K("L2.png", "L2-紅色.png", 52, 27);
        private static readonly K Circle = new K("右邊圖形鍵-圓形.png", "右邊圖形鍵-圓形-紅色.png", 42, 42);
        private static readonly K Cross = new K("右邊圖形鍵-叉叉.png", "右邊圖形鍵-叉叉-紅色.png", 42, 42);
        private static readonly K Square = new K("右邊圖形鍵-正方形.png", "右邊圖形鍵-正方形-紅色.png", 42, 42);
        private static readonly K Triangle = new K("右邊圖形鍵-三角形.png", "右邊圖形鍵-三角形-紅色.png", 42, 42);
        private static readonly K R1 = new K("R1.png", "R1-紅色.png", 56, 23);
        private static readonly K Dpad = new K("左邊方向鍵.png", "左邊方向鍵-紅色.png", 44, 44);
        private static readonly K Opt = new K("OPTIONS.png", null, 40, 40); // 沒有紅色版：選中時靠底色帶與金線表示

        private class RowDef
        {
            public string name, desc, extra; public G.GuideKey key; public K glyph;
            public (string icon, string label)[] icons;
        }

        private static void WritePages(SerializedProperty pp, System.Collections.Generic.Dictionary<string, VideoClip> oldClips)
        {
            var pages = new (string title, RowDef[] rows, string note, bool combos)[]
            {
                ("移動", new[] {
                    new RowDef { name = "移動", desc = "推動左搖桿，往那個方向移動。", key = G.GuideKey.Move, glyph = StickL },
                }, null, false),
                ("視角", new[] {
                    new RowDef { name = "鏡頭旋轉", desc = "推動右搖桿，轉動鏡頭觀察四周。", key = G.GuideKey.Camera, glyph = StickR },
                    new RowDef { name = "鏡頭重置", desc = "鏡頭立刻轉回角色正後方。", key = G.GuideKey.ResetCamera, glyph = L2 },
                }, null, false),
                ("跳躍與翻滾", new[] {
                    new RowDef { name = "跳躍", desc = "跳起來，越過地面上的攻擊。", key = G.GuideKey.Jump, glyph = Cross },
                    new RowDef { name = "翻滾", desc = "朝搖桿方向翻滾，躲開敵人的攻擊。", key = G.GuideKey.Roll, glyph = Circle },
                }, null, false),
                ("鎖定", new[] {
                    new RowDef { name = "鎖定", desc = "鎖定附近的敵人，鏡頭會跟著對方。", extra = "再按一次解除鎖定。", key = G.GuideKey.LockOn, glyph = L1 },
                }, null, false),
                ("攻擊連段", new[] {
                    new RowDef { name = "一般攻擊", desc = "連按可以打出連段。", key = G.GuideKey.LightAttack, glyph = Square },
                    new RowDef { name = "強攻擊", desc = "威力較大，接在一般攻擊後會變成不同招式。", key = G.GuideKey.HeavyAttack, glyph = Triangle },
                }, "每把武器的完整連段，可以暫停後到「玩家狀態 → 武器介紹」查看與試招。", true),
                ("特殊技", new[] {
                    new RowDef { name = "秘技", desc = "強力的特殊招式，用完需要冷卻。", key = G.GuideKey.Special, glyph = R1 },
                }, null, false),
                ("切換武器", new[] {
                    new RowDef { name = "切換武器", desc = "十字鍵的四個方向各對應一把武器。", key = G.GuideKey.WeaponSwitch, glyph = Dpad,
                        // 完整十字鍵上疊紅色的那個方向（單一方向圖縮小後只剩一小截，看不出方向）
                        icons = new[] { ("左邊方向鍵-上-紅色.png", "鉛筆　"), ("左邊方向鍵-左-紅色.png", "畫筆　"), ("左邊方向鍵-下-紅色.png", "畫刀　"), ("左邊方向鍵-右-紅色.png", "調色盤") } },
                }, null, false),
                ("暫停", new[] {
                    new RowDef { name = "暫停選單", desc = "暫停遊戲，查看操作說明、武器介紹與設定。", extra = "忘記操作時，可以在這裡重看這份引導。", key = G.GuideKey.Pause, glyph = Opt },
                }, null, false),
            };

            pp.arraySize = pages.Length;
            for (int p = 0; p < pages.Length; p++)
            {
                var pe = pp.GetArrayElementAtIndex(p);
                pe.FindPropertyRelative("title").stringValue = pages[p].title;
                // 字型沒有「→」：指路文字裡的箭頭改成全形破折號以外的寫法
                pe.FindPropertyRelative("note").stringValue = pages[p].note != null ? pages[p].note.Replace(" → ", "｜") : "";

                var rows = pe.FindPropertyRelative("rows");
                rows.arraySize = pages[p].rows.Length;
                for (int r = 0; r < pages[p].rows.Length; r++)
                {
                    var d = pages[p].rows[r];
                    var e = rows.GetArrayElementAtIndex(r);
                    e.FindPropertyRelative("name").stringValue = d.name;
                    e.FindPropertyRelative("desc").stringValue = d.desc;
                    e.FindPropertyRelative("extra").stringValue = d.extra ?? "";
                    e.FindPropertyRelative("key").enumValueIndex = (int)d.key;
                    e.FindPropertyRelative("glyph").objectReferenceValue = Glyph(d.glyph.normal);
                    e.FindPropertyRelative("glyphLit").objectReferenceValue = d.glyph.lit != null ? Glyph(d.glyph.lit) : null;
                    e.FindPropertyRelative("glyphSize").vector2Value = new Vector2(d.glyph.w * 1.5f, d.glyph.h * 1.5f);
                    e.FindPropertyRelative("demoClip").objectReferenceValue = oldClips.TryGetValue(d.name, out var clip) ? clip : null;

                    var ic = e.FindPropertyRelative("extraIcons");
                    ic.arraySize = d.icons != null ? d.icons.Length : 0;
                    for (int k = 0; k < ic.arraySize; k++)
                    {
                        var ie = ic.GetArrayElementAtIndex(k);
                        ie.FindPropertyRelative("icon").objectReferenceValue = Glyph("左邊方向鍵.png");
                        ie.FindPropertyRelative("overlay").objectReferenceValue = Glyph(d.icons[k].icon);
                        ie.FindPropertyRelative("label").stringValue = d.icons[k].label;
                    }
                }

                var cb = pe.FindPropertyRelative("combos");
                if (pages[p].combos)
                {
                    // 已定案：鉛筆 C3（□□△）與 N4（□□□□）；字型沒有□△符號，說明改用文字
                    cb.arraySize = 2;
                    SetCombo(cb.GetArrayElementAtIndex(0), new[] { Square, Square, Triangle }, "一般攻擊兩下後接強攻擊");
                    SetCombo(cb.GetArrayElementAtIndex(1), new[] { Square, Square, Square, Square }, "連按一般攻擊，打出一整套連段");
                }
                else cb.arraySize = 0;
            }
        }

        private static void SetCombo(SerializedProperty e, K[] keys, string text)
        {
            var kp = e.FindPropertyRelative("keys");
            kp.arraySize = keys.Length;
            for (int i = 0; i < keys.Length; i++) kp.GetArrayElementAtIndex(i).objectReferenceValue = Glyph(keys[i].normal);
            e.FindPropertyRelative("text").stringValue = text;
        }

        private static void SetObjArray(SerializedProperty p, Object[] items)
        {
            p.arraySize = items.Length;
            for (int i = 0; i < items.Length; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = items[i];
        }

        // ─────────────────────────── 資產 ───────────────────────────

        private static void EnsureRingSprite()
        {
            AssetDatabase.ImportAsset(RingPath);
            var imp = AssetImporter.GetAtPath(RingPath) as TextureImporter;
            if (imp == null) { Debug.LogError("[OpeningGuideLayoutBuilder] 找不到 " + RingPath); return; }
            if (imp.textureType == TextureImporterType.Sprite && !imp.mipmapEnabled) return;
            imp.textureType = TextureImporterType.Sprite;
            imp.spriteImportMode = SpriteImportMode.Single;
            imp.alphaIsTransparency = true;
            imp.mipmapEnabled = false;
            imp.SaveAndReimport();
        }

        private static InputPromptData EnsureHoldPrompt()
        {
            var data = AssetDatabase.LoadAssetAtPath<InputPromptData>(HoldPromptPath);
            if (data == null)
            {
                data = ScriptableObject.CreateInstance<InputPromptData>();
                AssetDatabase.CreateAsset(data, HoldPromptPath);
            }
            var skip = AssetDatabase.LoadAssetAtPath<InputPromptData>(SkipPromptPath);
            var so = new SerializedObject(data);
            so.FindProperty("_promptText").stringValue = "長按 開始戰鬥";
            if (skip != null) so.FindProperty("_action").objectReferenceValue = new SerializedObject(skip).FindProperty("_action").objectReferenceValue;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();
            return data;
        }

        // ─────────────────────────── 小工具（同武器介紹）───────────────────────────

        private static void SetPrompt(InputPromptUI ui, string asset)
        {
            if (ui == null) return;
            var so = new SerializedObject(ui);
            so.FindProperty("_data").objectReferenceValue = AssetDatabase.LoadAssetAtPath<InputPromptData>($"Assets/遊戲素材/SO/Prompts/{asset}.asset");
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform c in t) { var r = FindDeep(c, name); if (r != null) return r; }
            return null;
        }

        private static Sprite Sprite(string file) => AssetDatabase.LoadAssetAtPath<Sprite>(UiDir + file);
        private static Sprite Glyph(string file) => AssetDatabase.LoadAssetAtPath<Sprite>(GlyphDir + file);

        private static Transform New(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        private static Transform NewStretch(string name, Transform parent)
        {
            var t = New(name, parent);
            StretchFull((RectTransform)t);
            return t;
        }

        private static void StretchFull(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        }

        /// <summary>用模擬頁座標（1280×720，左上為原點）擺放：中心錨點、左上角 pivot，數值 ×1.5。</summary>
        private static void At(RectTransform rt, float mx, float my, float mw, float mh)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(mx * 1.5f - 960f, 540f - my * 1.5f);
            rt.sizeDelta = new Vector2(mw * 1.5f, mh * 1.5f);
        }

        private static void LocalLeft(RectTransform rt, float x, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = new Vector2(x, 0f);
            rt.sizeDelta = new Vector2(w, h);
        }

        private static Image Img(string name, Transform parent, Sprite sprite, Color color)
        {
            var img = New(name, parent).gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        private static TextMeshProUGUI Text(string name, Transform parent, string text, float size, Color color, TextAlignmentOptions align)
        {
            var t = New(name, parent).gameObject.AddComponent<TextMeshProUGUI>();
            t.font = _font;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.raycastTarget = false;
            t.text = text;
            return t;
        }

        private static void Divider(Transform page, float my)
        {
            var d = Img("Divider", page, Sprite("線_左濃右淡.png"), new Color(Gold.r, Gold.g, Gold.b, 0.7f));
            At(d.rectTransform, 108, my, 1032, 1);
            d.rectTransform.sizeDelta = new Vector2(1548f, 2f);
        }

        private static void DoubleFrame(Transform parent, float outerAlpha, float innerAlpha, float inset)
        {
            var outer = Img("FrameOuter", parent, Sprite("細框.png"), new Color(Gold.r, Gold.g, Gold.b, outerAlpha));
            outer.type = Image.Type.Sliced;
            StretchFull(outer.rectTransform);
            var inner = Img("FrameInner", parent, Sprite("細框.png"), new Color(Gold.r, Gold.g, Gold.b, innerAlpha));
            inner.type = Image.Type.Sliced;
            var irt = inner.rectTransform;
            irt.anchorMin = Vector2.zero; irt.anchorMax = Vector2.one;
            irt.offsetMin = new Vector2(inset, inset); irt.offsetMax = new Vector2(-inset, -inset);
        }

        private static void HLine(Transform parent, Sprite sprite, float offset, float alpha, bool top)
        {
            var l = Img(top ? "LineTop" : "LineBottom", parent, sprite, new Color(Gold.r, Gold.g, Gold.b, alpha));
            var rt = l.rectTransform;
            rt.anchorMin = new Vector2(0f, top ? 1f : 0f);
            rt.anchorMax = new Vector2(1f, top ? 1f : 0f);
            rt.pivot = new Vector2(0.5f, top ? 1f : 0f);
            rt.anchoredPosition = new Vector2(0f, top ? -offset : offset);
            rt.sizeDelta = new Vector2(0f, 2f);
        }
    }
}
