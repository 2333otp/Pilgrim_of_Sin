using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using PilgrimOfSin.StateMachine;

namespace PilgrimOfSin.EditorTools
{
    /// <summary>
    /// 重建 PauseCanvas.prefab 裡「武器介紹」（WeaponInfo_Content）的版面。
    /// 版面數值來自已定案的模擬頁（1280×720），這裡一律 ×1.5 換成 1920×1080。
    /// 可重複執行：每次都先刪掉上次產生的物件再重建；LeftArrow / RightArrow / PageIndicator 沿用原物件。
    /// 選單：PilgrimOfSin/UI/重建武器介紹版面
    /// </summary>
    public static class WeaponInfoLayoutBuilder
    {
        private const string PrefabPath = "Assets/遊戲素材/程式/畫面切換/PauseCanvas.prefab";
        private const string UiDir = "Assets/遊戲素材/UI圖檔/武器介紹/";
        private const string GlyphDir = "Assets/遊戲素材/UI圖檔/操作/";
        private const string FontPath = "Assets/遊戲素材/字型/京華老宋体v2.002 SDF.asset";

        private static readonly Color Gold = new Color32(0xD9, 0xB3, 0x6A, 0xFF);
        private static readonly Color Cream = new Color32(0xEF, 0xE6, 0xD2, 0xFF);
        private static readonly Color Muted = new Color32(0xA8, 0x9D, 0x86, 0xFF);

        private static TMP_FontAsset _font;

        [MenuItem("PilgrimOfSin/UI/重建武器介紹版面")]
        public static void Build()
        {
            _font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
            EnsureLayer(PreviewLayerName);
            var previewMuir = BuildPreviewMuir();
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var content = FindDeep(root.transform, "WeaponInfo_Content");
                if (content == null) { Debug.LogError("找不到 WeaponInfo_Content"); return; }

                // 刪掉舊版面（舊的一頁一把武器）與上次產生的物件
                foreach (var n in new[] { "WeaponArt", "WeaponName", "WeaponDescription", "PageNavHints", "Page1", "Page2", "WeaponNavHints" })
                {
                    var t = content.Find(n);
                    if (t != null) Object.DestroyImmediate(t.gameObject);
                }

                var book = content.GetComponent<WeaponInfoPageBook>();
                var page1 = BuildPage1(content, out var rows, out var artA, out var artB, out var nameText, out var descText);
                var page2 = BuildPage2(content, previewMuir, out var moveTitle, out var table, out var trial);
                var hints = BuildHints(root.transform, content, out var selectHint, out var trialHint);

                // 頁碼（沿用原 PageIndicator）
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
                page1.SetSiblingIndex(0);
                page2.SetSiblingIndex(1);
                hints.SetSiblingIndex(2);
                indicator.transform.SetAsLastSibling();
                content.Find("LeftArrow").SetAsLastSibling();
                content.Find("RightArrow").SetAsLastSibling();

                Wire(book, page1.gameObject, page2.gameObject, rows, artA, artB, nameText, descText, moveTitle, table, trial, selectHint, trialHint);

                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                Debug.Log("[WeaponInfoLayoutBuilder] 武器介紹版面已重建");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // ─────────────────────────── 第 1 頁 ───────────────────────────

        private static Transform BuildPage1(Transform content, out List<WeaponInfoPageBook.WeaponRow> rows,
            out Image artA, out Image artB, out TextMeshProUGUI nameText, out TextMeshProUGUI descText)
        {
            var page = NewStretch("Page1", content);

            var title = Text("Title", page, "武器介紹", 48f, Gold, TextAlignmentOptions.TopLeft);
            title.fontStyle = FontStyles.Bold;
            title.characterSpacing = 25f;
            At(title.rectTransform, 108, 74, 400, 50);

            Divider(page, 128);

            // 武器清單
            rows = new List<WeaponInfoPageBook.WeaponRow>();
            for (int i = 0; i < 4; i++)
            {
                var row = New("Row" + i, page);
                At((RectTransform)row, 108, 228 + i * 80, 260, 62);
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

                rows.Add(new WeaponInfoPageBook.WeaponRow { button = btn, band = band, bar = bar, marker = marker, label = label });
            }

            // 原畫舞台（雙線框＋中心暖光，超出框的部分裁掉）
            var stage = New("ArtStage", page);
            At((RectTransform)stage, 400, 160, 360, 430);
            var stageBg = stage.gameObject.AddComponent<Image>();
            stageBg.sprite = Sprite("純白.png");
            stageBg.color = new Color(0f, 0f, 0f, 0.3f);
            stageBg.raycastTarget = false;
            stage.gameObject.AddComponent<RectMask2D>();
            var warm = Img("WarmLight", stage, Sprite("放射光.png"), new Color(1f, 214f / 255f, 150f / 255f, 0.12f));
            StretchFull(warm.rectTransform);
            artA = Img("ArtA", stage, null, Color.white);
            artB = Img("ArtB", stage, null, Color.white);
            foreach (var a in new[] { artA, artB })
            {
                a.preserveAspect = true;
                var art = a.rectTransform;
                art.anchorMin = art.anchorMax = art.pivot = new Vector2(0.5f, 0.5f);
                art.anchoredPosition = Vector2.zero;
            }
            DoubleFrame(stage, 0.45f, 0.18f, 10.5f);

            // 右欄：名稱、裝飾線、介紹
            nameText = Text("WeaponName", page, "", 45f, Gold, TextAlignmentOptions.TopLeft);
            nameText.fontStyle = FontStyles.Bold;
            nameText.characterSpacing = 10f;
            At(nameText.rectTransform, 800, 232, 340, 45);

            var orn = New("Ornament", page);
            At((RectTransform)orn, 800, 293, 340, 12);
            // 名稱與介紹之間：一條由左往右淡出的細線（不加菱形裝飾）
            var l1 = Img("Fade", orn, Sprite("帶狀_左濃右淡.png"), new Color(Gold.r, Gold.g, Gold.b, 0.6f));
            LocalLeft(l1.rectTransform, 0f, 510f, 1.5f);

            descText = Text("WeaponDescription", page, "", 30f, Cream, TextAlignmentOptions.TopLeft);
            descText.characterSpacing = 5f;
            descText.lineSpacing = 45f;
            descText.textWrappingMode = TextWrappingModes.Normal;
            At(descText.rectTransform, 800, 318, 340, 200);
            return page;
        }

        // ─────────────────────────── 第 2 頁 ───────────────────────────

        private static Transform BuildPage2(Transform content, GameObject previewMuir, out TextMeshProUGUI moveTitle, out MoveListTableUI table, out MoveTrialController trial)
        {
            var page = NewStretch("Page2", content);

            // 跟第 1 頁「武器介紹」同一個位置與對齊方式，翻頁時標題不會上下跳
            moveTitle = Text("Title", page, "", 48f, Gold, TextAlignmentOptions.TopLeft);
            moveTitle.characterSpacing = 8f;
            moveTitle.richText = true;
            At(moveTitle.rectTransform, 108, 74, 800, 50);

            Divider(page, 128);

            // 左：天地線（上下各兩條細金線，兩端淡出，左右不封口）
            var lines = New("TableLines", page);
            At((RectTransform)lines, 108, 190, 552, 360);
            var fade = Sprite("線_兩端淡出.png");
            HLine(lines, fade, 0f, 0.65f, true);
            HLine(lines, fade, 7.5f, 0.22f, true);
            HLine(lines, fade, 7.5f, 0.22f, false);
            HLine(lines, fade, 0f, 0.65f, false);

            // 出招表本體（內容執行期產生）
            var tableRt = New("MoveTable", page);
            At((RectTransform)tableRt, 108, 190, 552, 360);
            table = tableRt.gameObject.AddComponent<MoveListTableUI>();
            var so = new SerializedObject(table);
            so.FindProperty("_font").objectReferenceValue = _font;
            so.FindProperty("_square").objectReferenceValue = Glyph("右邊圖形鍵-正方形.png");
            so.FindProperty("_squareLit").objectReferenceValue = Glyph("右邊圖形鍵-正方形-紅色.png");
            so.FindProperty("_triangle").objectReferenceValue = Glyph("右邊圖形鍵-三角形.png");
            so.FindProperty("_triangleLit").objectReferenceValue = Glyph("右邊圖形鍵-三角形-紅色.png");
            so.FindProperty("_r1").objectReferenceValue = Glyph("R1.png");
            so.FindProperty("_r1Lit").objectReferenceValue = Glyph("R1-紅色.png");
            so.FindProperty("_white").objectReferenceValue = Sprite("純白.png");
            so.FindProperty("_thinFrame").objectReferenceValue = Sprite("細框.png");
            so.FindProperty("_thickFrame").objectReferenceValue = Sprite("粗框.png");
            so.FindProperty("_glow").objectReferenceValue = Sprite("方形光暈.png");
            so.FindProperty("_arrowHead").objectReferenceValue = Sprite("箭頭頭部.png");
            so.ApplyModifiedPropertiesWithoutUndo();

            // 右：動作展示（雙線框）。小 Muir 的畫面之後放進 PreviewImage。
            var demo = New("MovePreviewFrame", page);
            At((RectTransform)demo, 692, 190, 448, 360);
            // 底色沒開展示時才看得到（展示畫面本身不透明，底色畫在攝影機上）
            var bg = Img("Background", demo, Sprite("純白.png"), new Color32(0x3A, 0x2C, 0x1F, 0xFF));
            StretchFull(bg.rectTransform);
            var raw = New("PreviewImage", demo).gameObject.AddComponent<RawImage>();
            raw.raycastTarget = false;
            raw.enabled = false;
            var rrt = raw.rectTransform;
            rrt.anchorMin = Vector2.zero; rrt.anchorMax = Vector2.one;
            rrt.offsetMin = Vector2.zero; rrt.offsetMax = Vector2.zero;
            // 疊在展示畫面上的暗角：四周與底部變暗，讓展示框融進頁面
            var vignette = Img("Vignette", demo, Sprite("展示框暗角.png"), Color.white);
            StretchFull(vignette.rectTransform);
            DoubleFrame(demo, 0.5f, 0.15f, 9f);
            var stage = demo.gameObject.AddComponent<MovePreviewStage>();
            var sso = new SerializedObject(stage);
            sso.FindProperty("_muirPrefab").objectReferenceValue = previewMuir;
            sso.FindProperty("_target").objectReferenceValue = raw;
            sso.FindProperty("_layerName").stringValue = PreviewLayerName;
            sso.FindProperty("_groundShadow").objectReferenceValue = Sprite("地面影子.png");
            sso.FindProperty("_floorGlow").objectReferenceValue = Sprite("放射光.png");
            sso.FindProperty("_textureSize").vector2IntValue = new Vector2Int(1344, 1080);
            sso.ApplyModifiedPropertiesWithoutUndo();

            // 出招表上方：剛剛按了什麼（像格鬥遊戲的訓練模式）
            var histLabel = Text("InputHistoryLabel", page, "輸入", 22f, Muted, TextAlignmentOptions.MidlineLeft);
            At(histLabel.rectTransform, 108, 146, 50, 28);
            var hist = New("InputHistory", page);
            At((RectTransform)hist, 146, 146, 380, 28);
            // 說明灰色按鍵的意思（靠右對齊出招表的右緣）
            var histNote = Text("InputHistoryNote", page, "灰色＝按太早，沒接上", 20f, Muted, TextAlignmentOptions.MidlineRight);
            At(histNote.rectTransform, 430, 146, 230, 28);

            trial = page.gameObject.AddComponent<MoveTrialController>();
            var tso = new SerializedObject(trial);
            tso.FindProperty("_table").objectReferenceValue = table;
            tso.FindProperty("_preview").objectReferenceValue = stage;
            tso.FindProperty("_gameplayController").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/遊戲素材/角色/謬爾/Muir_Gameplay.controller");
            tso.FindProperty("_historyRoot").objectReferenceValue = hist;
            tso.FindProperty("_font").objectReferenceValue = _font;
            tso.FindProperty("_square").objectReferenceValue = Glyph("右邊圖形鍵-正方形.png");
            tso.FindProperty("_triangle").objectReferenceValue = Glyph("右邊圖形鍵-三角形.png");
            tso.FindProperty("_r1").objectReferenceValue = Glyph("R1.png");
            tso.ApplyModifiedPropertiesWithoutUndo();
            return page;
        }

        // ─────────────────────────── 提示列 ───────────────────────────

        /// <summary>複製音量頁的 VolumeNavHints（位置、字級、圖示完全一致），改成「選擇武器／翻頁／返回」。</summary>
        private static Transform BuildHints(Transform root, Transform content, out GameObject selectHint, out GameObject trialHint)
        {
            var src = FindDeep(root, "VolumeNavHints");
            var copy = Object.Instantiate(src.gameObject, content, false);
            copy.name = "WeaponNavHints";
            copy.SetActive(true);

            var select = copy.transform.Find("SelectHint");
            var adjust = copy.transform.Find("AdjustHint");
            selectHint = select.gameObject;
            SetPrompt(select.GetComponent<InputPromptUI>(), "選擇武器提示");
            SetPrompt(select.Find("Icon_First")?.GetComponent<InputPromptUI>(), "上鍵提示");
            SetPrompt(adjust.GetComponent<InputPromptUI>(), "翻頁提示");
            SetPrompt(adjust.Find("Icon_First")?.GetComponent<InputPromptUI>(), "左翻提示");
            adjust.name = "FlipHint";

            // 第 2 頁：同一個位置換成「□ △ R1 試招」（只做手把圖示）
            var selRt = (RectTransform)select;
            var trialT = New("TrialHint", copy.transform);
            var trt = (RectTransform)trialT;
            trt.anchorMin = selRt.anchorMin; trt.anchorMax = selRt.anchorMax; trt.pivot = selRt.pivot;
            trt.anchoredPosition = selRt.anchoredPosition;
            trt.sizeDelta = new Vector2(360f, selRt.sizeDelta.y);
            float cy = -selRt.sizeDelta.y * 0.5f;
            // 寬度要收在「翻頁」那組（x≈395 起）左邊，不能壓到
            HintIcon(trialT, Glyph("右邊圖形鍵-正方形.png"), 0f, cy, 36f, 36f);
            HintIcon(trialT, Glyph("右邊圖形鍵-三角形.png"), 42f, cy, 36f, 36f);
            HintIcon(trialT, Glyph("R1.png"), 86f, cy, 52f, 22f);
            var srcText = select.Find("Text").GetComponent<TextMeshProUGUI>();
            var label = Object.Instantiate(srcText.gameObject, trialT, false).GetComponent<TextMeshProUGUI>();
            label.name = "Text";
            label.text = "試招";
            var lrt = label.rectTransform;
            lrt.anchorMin = lrt.anchorMax = new Vector2(0f, 1f);
            lrt.pivot = new Vector2(0f, 0.5f);
            lrt.anchoredPosition = new Vector2(150f, cy);
            lrt.sizeDelta = new Vector2(120f, 50f);
            label.alignment = TextAlignmentOptions.MidlineLeft;
            trialT.gameObject.SetActive(false);
            trialHint = trialT.gameObject;
            return copy.transform;
        }

        private static void SetPrompt(InputPromptUI ui, string asset)
        {
            if (ui == null) return;
            var so = new SerializedObject(ui);
            so.FindProperty("_data").objectReferenceValue = Prompt(asset);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ─────────────────────────── 接線 ───────────────────────────

        private static void Wire(WeaponInfoPageBook book, GameObject page1, GameObject page2, List<WeaponInfoPageBook.WeaponRow> rows,
            Image artA, Image artB, TextMeshProUGUI nameText, TextMeshProUGUI descText, TextMeshProUGUI moveTitle, MoveListTableUI table,
            MoveTrialController trial, GameObject selectHint, GameObject trialHint)
        {
            var so = new SerializedObject(book);
            so.FindProperty("_page1Root").objectReferenceValue = page1;
            so.FindProperty("_page2Root").objectReferenceValue = page2;
            so.FindProperty("_artImage").objectReferenceValue = artA;
            so.FindProperty("_artImageB").objectReferenceValue = artB;
            so.FindProperty("_nameText").objectReferenceValue = nameText;
            so.FindProperty("_descriptionText").objectReferenceValue = descText;
            so.FindProperty("_movePageTitleText").objectReferenceValue = moveTitle;
            so.FindProperty("_moveTable").objectReferenceValue = table;
            so.FindProperty("_trial").objectReferenceValue = trial;
            so.FindProperty("_selectHint").objectReferenceValue = selectHint;
            so.FindProperty("_trialHint").objectReferenceValue = trialHint;

            var rp = so.FindProperty("_rows");
            rp.arraySize = rows.Count;
            for (int i = 0; i < rows.Count; i++)
            {
                var e = rp.GetArrayElementAtIndex(i);
                e.FindPropertyRelative("button").objectReferenceValue = rows[i].button;
                e.FindPropertyRelative("band").objectReferenceValue = rows[i].band;
                e.FindPropertyRelative("bar").objectReferenceValue = rows[i].bar;
                e.FindPropertyRelative("marker").objectReferenceValue = rows[i].marker;
                e.FindPropertyRelative("label").objectReferenceValue = rows[i].label;
            }

            string[] shortNames = { "鉛筆", "畫筆", "畫刀", "調色盤" };
            string[] desc =
            {
                "陪著繆爾走過\n默默無名的畫家時期\n直到現在",
                "彷彿回應繆爾的覺悟一般\n趁手又好用的畫筆",
                "鋒利又耐用\n陪著繆爾畫了一張又一張禁畫",
                "乾硬的油畫顏料\n跟蟲子一樣惹人厭",
            };
            string[] sets = { "Pencil", "Brush", "PaintKnife", "Palette" };
            var pp = so.FindProperty("_pages");
            for (int i = 0; i < 4 && i < pp.arraySize; i++)
            {
                var e = pp.GetArrayElementAtIndex(i);
                e.FindPropertyRelative("shortName").stringValue = shortNames[i];
                e.FindPropertyRelative("description").stringValue = desc[i];
                e.FindPropertyRelative("actionSet").objectReferenceValue =
                    AssetDatabase.LoadAssetAtPath<WeaponActionSet>($"Assets/遊戲素材/SO/主角/WeaponActions_{sets[i]}.asset");
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ─────────────────────────── 小 Muir ───────────────────────────

        private const string PreviewLayerName = "MenuPreview";
        private const string PreviewMuirPath = "Assets/遊戲素材/預製物/MovePreviewMuir.prefab";

        /// <summary>專案圖層加上 MenuPreview（已經有就不動；從第 9 格開始找空位）。</summary>
        private static void EnsureLayer(string name)
        {
            var tm = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layers = tm.FindProperty("layers");
            for (int i = 0; i < layers.arraySize; i++)
                if (layers.GetArrayElementAtIndex(i).stringValue == name) return;
            for (int i = 9; i < layers.arraySize; i++)
            {
                var e = layers.GetArrayElementAtIndex(i);
                if (!string.IsNullOrEmpty(e.stringValue)) continue;
                e.stringValue = name;
                tm.ApplyModifiedPropertiesWithoutUndo();
                Debug.Log($"[WeaponInfoLayoutBuilder] 新增圖層 {i}: {name}");
                return;
            }
            Debug.LogError("[WeaponInfoLayoutBuilder] 沒有空的圖層位置");
        }

        /// <summary>
        /// 複製出展示用 Muir：只留模型、Animator、武器掛點（PlayerWeaponVisual 的角度/握把設定一起帶過來），
        /// 其他遊戲用元件（控制器、輸入、碰撞、攻擊判定…）全部拿掉。
        /// 來源 = 目前開著的 GreedBossScene 裡的 Muir（阿貪關卡的 Muir 是最新、最準的版本；
        /// Player.prefab 是舊設定：Y 軸拉高 1.7 倍、根物件多一個方塊）。
        /// 沒開 GreedBossScene 時不重新複製，沿用現有的 MovePreviewMuir.prefab。
        /// 之後阿貪關卡的 Muir 有改，開著 GreedBossScene 重跑這個選單就會重新複製。
        /// </summary>
        private static GameObject BuildPreviewMuir()
        {
            PlayerController source = null;
            foreach (var pc in Object.FindObjectsByType<PlayerController>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (pc.gameObject.scene.name == "GreedBossScene") { source = pc; break; }

            // Play Mode 中的 Muir 手上已經掛著執行期生成的武器與手腕錨點，不能拿來複製
            if (Application.isPlaying) source = null;

            if (source == null)
            {
                var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PreviewMuirPath);
                Debug.LogWarning(existing != null
                    ? "[WeaponInfoLayoutBuilder] 沒有開著 GreedBossScene，沿用現有的展示用 Muir"
                    : "[WeaponInfoLayoutBuilder] 沒有開著 GreedBossScene，無法產生展示用 Muir");
                return existing;
            }

            // 在暫存的預覽空間裡複製，不會動到場景
            var tempScene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
            var contents = Object.Instantiate(source.gameObject);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(contents, tempScene);
            try
            {
                var hit = contents.transform.Find("AttackHitBox");
                if (hit != null) Object.DestroyImmediate(hit.gameObject);

                // 保險：清掉執行期才會生成的東西（武器模型、手腕錨點），由 PlayerWeaponVisual 在展示時重新生成
                foreach (var t in contents.GetComponentsInChildren<Transform>(true))
                {
                    if (t == null || t == contents.transform) continue;
                    if (t.name.EndsWith("(Clone)") || t.name.Contains("(WristAnchor)")) Object.DestroyImmediate(t.gameObject);
                }

                // RequireComponent 的關係要多刪幾輪才刪得乾淨
                for (int pass = 0; pass < 5; pass++)
                {
                    foreach (var c in contents.GetComponentsInChildren<Component>(true))
                    {
                        if (c == null || c is Transform || c is Animator || c is SkinnedMeshRenderer || c is PlayerWeaponVisual) continue;
                        // 根物件上的方塊（MeshFilter/MeshRenderer）是遊戲用的佔位，不要帶過來
                        Object.DestroyImmediate(c);
                    }
                }
                var anim = contents.GetComponent<Animator>();
                anim.runtimeAnimatorController = null;
                anim.applyRootMotion = false;
                anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                contents.transform.position = Vector3.zero;
                contents.transform.rotation = Quaternion.identity;
                contents.name = "MovePreviewMuir";
                Debug.Log($"[WeaponInfoLayoutBuilder] 展示用 Muir 從 GreedBossScene 的 {source.name} 複製（scale={source.transform.localScale}）");
                return PrefabUtility.SaveAsPrefabAsset(contents, PreviewMuirPath);
            }
            finally
            {
                Object.DestroyImmediate(contents);
                UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(tempScene);
            }
        }

        private static void HintIcon(Transform parent, Sprite sprite, float x, float cy, float w, float h)
        {
            var img = Img("Icon", parent, sprite, Color.white);
            img.preserveAspect = true;
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = new Vector2(x, cy);
            rt.sizeDelta = new Vector2(w, h);
        }

        // ─────────────────────────── 小工具 ───────────────────────────

        private static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform c in t) { var r = FindDeep(c, name); if (r != null) return r; }
            return null;
        }

        private static Sprite Sprite(string file) => AssetDatabase.LoadAssetAtPath<Sprite>(UiDir + file);
        private static Sprite Glyph(string file) => AssetDatabase.LoadAssetAtPath<Sprite>(GlyphDir + file);
        private static InputPromptData Prompt(string name) => AssetDatabase.LoadAssetAtPath<InputPromptData>($"Assets/遊戲素材/SO/Prompts/{name}.asset");

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

        /// <summary>父物件內靠左、垂直置中（單位已是 1920 基準）。</summary>
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

        private static void Divider(Transform page, float my)
        {
            var d = Img("Divider", page, Sprite("線_左濃右淡.png"), new Color(Gold.r, Gold.g, Gold.b, 0.7f));
            At(d.rectTransform, 108, my, 1032, 1);
            d.rectTransform.sizeDelta = new Vector2(1548f, 2f);
        }

        /// <summary>雙線框：外框 + 內縮 inset 的淡內框。</summary>
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
