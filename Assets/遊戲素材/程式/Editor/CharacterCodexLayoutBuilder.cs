using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace PilgrimOfSin.EditorTools
{
    /// <summary>
    /// 重建 PauseCanvas.prefab 裡「角色資料、心魔圖鑑」（CharData_Content）的版面。
    /// 版面數值來自已定案的模擬頁 V4（1280×720），這裡一律 ×1.5 換成 1920×1080。
    /// 樣式（清單、標題、分隔線、頁碼、提示列）比照武器介紹第 1 頁（WeaponInfoLayoutBuilder）。
    /// 可重複執行：每次都先刪掉上次產生的物件再重建；LeftArrow / RightArrow / PageIndicator 沿用原物件。
    /// 選單：PilgrimOfSin/UI/重建角色圖鑑版面
    /// </summary>
    public static class CharacterCodexLayoutBuilder
    {
        private const string PrefabPath = "Assets/遊戲素材/程式/畫面切換/PauseCanvas.prefab";
        private const string UiDir = "Assets/遊戲素材/UI圖檔/武器介紹/";
        private const string ArtDir = "Assets/遊戲素材/UI圖檔/角色圖鑑/";
        private const string PromptDir = "Assets/遊戲素材/SO/Prompts/";
        private const string FontPath = "Assets/遊戲素材/字型/京華老宋体v2.002 SDF.asset";

        private static readonly Color Gold = new Color32(0xD9, 0xB3, 0x6A, 0xFF);
        private static readonly Color Cream = new Color32(0xEF, 0xE6, 0xD2, 0xFF);
        private static readonly Color Soft = new Color32(0xCF, 0xC4, 0xAD, 0xFF);

        private static TMP_FontAsset _font;

        [MenuItem("PilgrimOfSin/UI/重建角色圖鑑版面")]
        public static void Build()
        {
            if (Application.isPlaying) { Debug.LogError("[CharacterCodexLayoutBuilder] 請先停止 Play Mode 再重建"); return; }

            _font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
            EnsureArtImports();
            var promptPeople = EnsurePrompt("選擇人物提示", "選擇人物");
            var promptDemons = EnsurePrompt("選擇心魔提示", "選擇心魔");

            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var content = FindDeep(root.transform, "CharData_Content");
                if (content == null) { Debug.LogError("找不到 CharData_Content"); return; }

                // 刪掉舊版面（舊的一頁一人）與上次產生的物件
                foreach (var n in new[] { "Portrait", "CharName", "CharDescription", "PageNavHints", "Page", "CodexNavHints" })
                {
                    var t = content.Find(n);
                    if (t != null) Object.DestroyImmediate(t.gameObject);
                }

                var book = content.GetComponent<CharacterCodexPageBook>();
                var page = BuildPage(content, out var rows, out var art, out var nameText, out var subText, out var descText);
                var hints = BuildHints(root.transform, content, promptPeople, out var selectHint);

                // 頁碼（沿用原 PageIndicator，樣式同武器介紹）
                var indicator = content.Find("PageIndicator").GetComponent<TextMeshProUGUI>();
                var irt = indicator.rectTransform;
                irt.anchorMin = irt.anchorMax = irt.pivot = new Vector2(1f, 1f);
                irt.anchoredPosition = new Vector2(-210f, -118f);
                irt.sizeDelta = new Vector2(700f, 50f);
                indicator.font = _font;
                indicator.fontSize = 26f;
                indicator.alignment = TextAlignmentOptions.TopRight;
                indicator.characterSpacing = 6f;
                indicator.richText = true;

                // 圖層順序：頁面 → 提示列 → 頁碼 → 箭頭（箭頭最上面才點得到）
                page.SetSiblingIndex(0);
                hints.SetSiblingIndex(1);
                indicator.transform.SetAsLastSibling();
                content.Find("LeftArrow").SetAsLastSibling();
                content.Find("RightArrow").SetAsLastSibling();

                Wire(book, rows, art, nameText, subText, descText, indicator, selectHint, promptPeople, promptDemons);

                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                Debug.Log("[CharacterCodexLayoutBuilder] 角色資料、心魔圖鑑版面已重建");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // ─────────────────────────── 頁面 ───────────────────────────

        private static Transform BuildPage(Transform content, out CharacterCodexPageBook.CharRow[] rows, out Image art,
            out TextMeshProUGUI nameText, out TextMeshProUGUI subText, out TextMeshProUGUI descText)
        {
            var page = NewStretch("Page", content);

            var title = Text("Title", page, "角色資料、心魔圖鑑", 48f, Gold, TextAlignmentOptions.TopLeft);
            title.fontStyle = FontStyles.Bold;
            title.characterSpacing = 25f;
            At(title.rectTransform, 108, 74, 700, 50);

            var d = Img("Divider", page, Sprite("線_左濃右淡.png"), new Color(Gold.r, Gold.g, Gold.b, 0.7f));
            At(d.rectTransform, 108, 128, 1032, 1);
            d.rectTransform.sizeDelta = new Vector2(1548f, 2f);

            // 清單（3 列，跟武器清單同樣式；模擬頁 top=240、列距 80）
            rows = new CharacterCodexPageBook.CharRow[3];
            for (int i = 0; i < rows.Length; i++)
            {
                var row = New("Row" + i, page);
                At((RectTransform)row, 108, 240 + i * 80, 260, 62);
                var hit = row.gameObject.AddComponent<Image>();
                hit.sprite = Sprite("純白.png");
                hit.color = new Color(0, 0, 0, 0);
                var btn = row.gameObject.AddComponent<Button>();
                btn.targetGraphic = hit;
                btn.transition = Selectable.Transition.None;
                var nav = btn.navigation; nav.mode = Navigation.Mode.None; btn.navigation = nav;

                var band = Img("Band", row, Sprite("帶狀_左濃右淡.png"), new Color(Gold.r, Gold.g, Gold.b, 0.26f));
                StretchFull(band.rectTransform);
                var bar = Img("Bar", row, Sprite("純白.png"), Gold);
                var brt = bar.rectTransform;
                brt.anchorMin = Vector2.zero; brt.anchorMax = new Vector2(0f, 1f); brt.pivot = new Vector2(0f, 0.5f);
                brt.anchoredPosition = Vector2.zero; brt.sizeDelta = new Vector2(4.5f, 0f);

                var marker = Text("Marker", row, "◆", 18f, Gold, TextAlignmentOptions.MidlineLeft);
                LocalLeft(marker.rectTransform, 30f, 30f);
                var label = Text("Label", row, "", 40f, Cream, TextAlignmentOptions.MidlineLeft);
                label.characterSpacing = 22f;
                LocalLeft(label.rectTransform, 72f, 300f);

                rows[i] = new CharacterCodexPageBook.CharRow { button = btn, band = band, bar = bar, marker = marker, label = label };
            }

            // 立繪後方的淡淡暖光（模擬頁 left 390, top 150, 400×470）
            var glow = Img("WarmLight", page, Sprite("放射光.png"), new Color(1f, 214f / 255f, 150f / 255f, 0.13f));
            At(glow.rectTransform, 390, 150, 400, 470);

            // 立繪：不畫框，直接站在背景上；大小與腳底位置由 CharacterCodexPageBook 依頁面設定
            art = Img("Portrait", page, null, Color.white);
            art.preserveAspect = true;
            var art_rt = art.rectTransform;
            art_rt.anchorMin = art_rt.anchorMax = new Vector2(0.5f, 0.5f);
            art_rt.pivot = new Vector2(0.5f, 0f);

            // 右欄：名稱、身份小字、裝飾線、介紹（模擬頁 x=840、寬 300）
            nameText = Text("CharName", page, "", 45f, Gold, TextAlignmentOptions.TopLeft);
            nameText.fontStyle = FontStyles.Bold;
            nameText.characterSpacing = 10f;
            At(nameText.rectTransform, 840, 236, 300, 45);

            subText = Text("CharSubtitle", page, "", 24f, Soft, TextAlignmentOptions.TopLeft);
            subText.characterSpacing = 18f;
            At(subText.rectTransform, 840, 283, 300, 24);

            var orn = New("Ornament", page);
            At((RectTransform)orn, 840, 317, 300, 12);
            var l1 = Img("Fade", orn, Sprite("帶狀_左濃右淡.png"), new Color(Gold.r, Gold.g, Gold.b, 0.6f));
            LocalLeft(l1.rectTransform, 0f, 450f, 1.5f);

            descText = Text("CharDescription", page, "", 30f, Cream, TextAlignmentOptions.TopLeft);
            descText.characterSpacing = 5f;
            descText.lineSpacing = 45f;
            descText.textWrappingMode = TextWrappingModes.Normal;
            At(descText.rectTransform, 840, 342, 300, 270);
            return page;
        }

        // ─────────────────────────── 提示列 ───────────────────────────

        /// <summary>複製武器介紹的 WeaponNavHints（位置、字級、圖示完全一致），拿掉試招，選擇文字由 PageBook 換頁時切換。</summary>
        private static Transform BuildHints(Transform root, Transform content, InputPromptData selectPrompt, out InputPromptUI selectHint)
        {
            var src = FindDeep(root, "WeaponNavHints");
            var copy = Object.Instantiate(src.gameObject, content, false);
            copy.name = "CodexNavHints";
            copy.SetActive(true);

            var trial = copy.transform.Find("TrialHint");
            if (trial != null) Object.DestroyImmediate(trial.gameObject);

            var select = copy.transform.Find("SelectHint");
            select.gameObject.SetActive(true);
            selectHint = select.GetComponent<InputPromptUI>();
            var so = new SerializedObject(selectHint);
            so.FindProperty("_data").objectReferenceValue = selectPrompt;
            so.ApplyModifiedPropertiesWithoutUndo();
            return copy.transform;
        }

        // ─────────────────────────── 接線與資料 ───────────────────────────

        private static void Wire(CharacterCodexPageBook book, CharacterCodexPageBook.CharRow[] rows, Image art,
            TextMeshProUGUI nameText, TextMeshProUGUI subText, TextMeshProUGUI descText, TextMeshProUGUI indicator,
            InputPromptUI selectHint, InputPromptData promptPeople, InputPromptData promptDemons)
        {
            var so = new SerializedObject(book);
            so.FindProperty("_artImage").objectReferenceValue = art;
            so.FindProperty("_nameText").objectReferenceValue = nameText;
            so.FindProperty("_subtitleText").objectReferenceValue = subText;
            so.FindProperty("_descriptionText").objectReferenceValue = descText;
            so.FindProperty("_pageIndicatorText").objectReferenceValue = indicator;
            so.FindProperty("_selectHint").objectReferenceValue = selectHint;

            var rp = so.FindProperty("_rows");
            rp.arraySize = rows.Length;
            for (int i = 0; i < rows.Length; i++)
            {
                var e = rp.GetArrayElementAtIndex(i);
                e.FindPropertyRelative("button").objectReferenceValue = rows[i].button;
                e.FindPropertyRelative("band").objectReferenceValue = rows[i].band;
                e.FindPropertyRelative("bar").objectReferenceValue = rows[i].bar;
                e.FindPropertyRelative("marker").objectReferenceValue = rows[i].marker;
                e.FindPropertyRelative("label").objectReferenceValue = rows[i].label;
            }

            // 模擬頁 V4 定案數值：立繪中心 x=590、寬上限 400；人物 高上限 500、腳底距下 96；心魔 高上限 580、腳底距下 62
            var pp = so.FindProperty("_pages");
            pp.arraySize = 2;
            FillPage(pp.GetArrayElementAtIndex(0), "人物", promptPeople, 500f, 400f, 590f, 96f, new[]
            {
                ("繆爾", "畫家", "深陷慾望囹圄的畫家\n最終自食惡果\n幡然醒悟後\n踏上克服妄念的道路"),
                ("萊恩", "繆爾的學徒", "師承繆爾\n因才華耀眼受師傅妒忌\n最終發覺繆爾的罪行\n而心灰意冷"),
                ("凡斯", "神秘老者", "在湖邊現身的神秘老者\n指引繆爾\n直面心中的貪嗔痴"),
            });
            FillPage(pp.GetArrayElementAtIndex(1), "心魔", promptDemons, 580f, 400f, 590f, 62f, new[]
            {
                ("阿貪", "心魔・貪", "自極致汲營的慾望中降生\n喜愛財寶與權力\n誘人屈從貪念而墮落\n永不饜足"),
                ("阿嗔", "心魔・嗔", "因憤怒與忌妒而生\n喜愛吞噬理智\n並將其化為狂暴之力摧毀一切\n以無盡怒火驅使眾生\n最終墮入瘋狂"),
                ("阿痴", "心魔・痴", "自執念的深淵而生\n使人迷心　蒙蔽思考\n困於囚籠\n使眾生成為迷途羔羊\n以迷惘茫然為樂"),
            });
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void FillPage(SerializedProperty p, string label, InputPromptData prompt,
            float maxH, float maxW, float mockCenterX, float mockBottom, (string name, string sub, string desc)[] data)
        {
            p.FindPropertyRelative("label").stringValue = label;
            p.FindPropertyRelative("selectPrompt").objectReferenceValue = prompt;
            p.FindPropertyRelative("artMaxHeight").floatValue = maxH * 1.5f;
            p.FindPropertyRelative("artMaxWidth").floatValue = maxW * 1.5f;
            p.FindPropertyRelative("artFootPosition").vector2Value =
                new Vector2(mockCenterX * 1.5f - 960f, 540f - (720f - mockBottom) * 1.5f);
            var es = p.FindPropertyRelative("entries");
            es.arraySize = data.Length;
            for (int i = 0; i < data.Length; i++)
            {
                var e = es.GetArrayElementAtIndex(i);
                e.FindPropertyRelative("art").objectReferenceValue =
                    AssetDatabase.LoadAssetAtPath<Sprite>($"{ArtDir}圖鑑立繪_{data[i].name}.png");
                e.FindPropertyRelative("shortName").stringValue = data[i].name;
                e.FindPropertyRelative("charName").stringValue = data[i].name;
                e.FindPropertyRelative("subtitle").stringValue = data[i].sub;
                e.FindPropertyRelative("description").stringValue = data[i].desc;
                // 個別微調（2026-11-27 使用者：繆爾往右、大一點；凡斯大一點）
                float scale = 1f, offset = 0f;
                if (data[i].name == "繆爾") { scale = 1.06f; offset = 40f; }
                else if (data[i].name == "凡斯") { scale = 1.06f; }
                e.FindPropertyRelative("artScale").floatValue = scale;
                e.FindPropertyRelative("artOffsetX").floatValue = offset;
            }
        }

        /// <summary>圖鑑立繪（裁好透明邊、腳底已淡出的副本）設成 Sprite。</summary>
        private static void EnsureArtImports()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { ArtDir.TrimEnd('/') }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!(AssetImporter.GetAtPath(path) is TextureImporter ti)) continue;
                if (ti.textureType == TextureImporterType.Sprite && ti.alphaIsTransparency && ti.maxTextureSize == 2048) continue;
                ti.textureType = TextureImporterType.Sprite;
                ti.spriteImportMode = SpriteImportMode.Single;
                ti.alphaIsTransparency = true;
                ti.mipmapEnabled = false;
                ti.maxTextureSize = 2048;
                ti.SaveAndReimport();
            }
        }

        /// <summary>沒有就從「選擇武器提示」複製一份（同一個上下鍵 Action），只改文字。</summary>
        private static InputPromptData EnsurePrompt(string asset, string text)
        {
            var path = $"{PromptDir}{asset}.asset";
            var data = AssetDatabase.LoadAssetAtPath<InputPromptData>(path);
            if (data == null)
            {
                AssetDatabase.CopyAsset($"{PromptDir}選擇武器提示.asset", path);
                data = AssetDatabase.LoadAssetAtPath<InputPromptData>(path);
            }
            var so = new SerializedObject(data);
            so.FindProperty("_promptText").stringValue = text;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssetIfDirty(data);
            return data;
        }

        // ─────────────────────────── 小工具（同 WeaponInfoLayoutBuilder） ───────────────────────────

        private static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform c in t) { var r = FindDeep(c, name); if (r != null) return r; }
            return null;
        }

        private static Sprite Sprite(string file) => AssetDatabase.LoadAssetAtPath<Sprite>(UiDir + file);

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

        private static void LocalLeft(RectTransform rt, float x, float w, float h = -1f)
        {
            rt.anchorMin = new Vector2(0f, 0f); rt.anchorMax = new Vector2(0f, 1f); rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = new Vector2(x, 0f);
            rt.sizeDelta = new Vector2(w, 0f);
            if (h > 0f)
            {
                rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f);
                rt.sizeDelta = new Vector2(w, h);
            }
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
    }
}
