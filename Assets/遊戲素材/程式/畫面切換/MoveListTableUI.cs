using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using PilgrimOfSin.StateMachine;

namespace PilgrimOfSin
{
    /// <summary>
    /// 武器介紹第 2 頁的出招表。照 F1 開發面板（ComboDevPanel）的結構：
    ///   一般攻擊 □→□→□→□
    ///   強攻擊   △  △  △  △   （第 k 下 □ 斜線連到第 k+1 格 △）
    ///   變招     □停頓連打 △蓄力 ＋ 旁邊一行按法說明（沒放動畫的武器整列隱藏）
    ///   秘技     R1 特殊招式
    /// 格子依 WeaponActionSet 自動產生：有放動畫的招式才出現，組員改出招表這裡自動跟著變。
    /// 一格只放一個按鍵。玩家在這頁直接按 □ △ R1 試招（MoveTrialController），
    /// 打過的路徑亮金色、正在出的那一招換紅色按鍵圖＋光暈、預輸入等著接的下一招用淡框標示。
    /// 掛在出招表區域（左上角 = 天地線範圍左上角）的 RectTransform 上，內容全部執行期產生。
    /// 數值以 1920×1080 為準。
    /// </summary>
    public class MoveListTableUI : MonoBehaviour
    {
        public enum MoveKind { Normal, Strong, Pause, Hold, Special }

        public class Move
        {
            public MoveKind kind;
            public int index;   // N / C 第幾下（0 起算）
            public int col, row;
            public AnimationClip clip;
        }

        [Header("字型")]
        [SerializeField] private TMP_FontAsset _font;

        [Header("按鍵圖（一般 / 選中紅色）")]
        [SerializeField] private Sprite _square;
        [SerializeField] private Sprite _squareLit;
        [SerializeField] private Sprite _triangle;
        [SerializeField] private Sprite _triangleLit;
        [SerializeField] private Sprite _r1;
        [SerializeField] private Sprite _r1Lit;

        [Header("格子／箭頭用貼圖")]
        [SerializeField] private Sprite _white;
        [SerializeField] private Sprite _thinFrame;
        [SerializeField] private Sprite _thickFrame;
        [SerializeField] private Sprite _glow;
        [SerializeField] private Sprite _arrowHead;

        [Header("版面（區域左上角為原點，往下為正）")]
        [SerializeField] private float _areaHeight = 540f;
        [SerializeField] private float _labelX = 42f;
        [SerializeField] private float _gridX = 222f;
        [SerializeField] private float _cell = 81f;
        [SerializeField] private float _pitchX = 123f;
        [SerializeField] private float _pitchY = 123f;
        [SerializeField] private float _glyphSize = 57f;
        [SerializeField] private float _labelFontSize = 28f;
        [SerializeField] private float _noteFontSize = 22f;
        [SerializeField] private float _arrowWidth = 4f;
        [SerializeField] private float _arrowHeadSize = 14f;

        [Header("顏色")]
        [SerializeField] private Color _labelColor = new Color32(0xEF, 0xE6, 0xD2, 0xFF);
        [SerializeField] private Color _noteColor = new Color32(0xCF, 0xC4, 0xAD, 0xFF);
        [SerializeField] private Color _gold = new Color32(0xD9, 0xB3, 0x6A, 0xFF);
        [SerializeField] private Color _arrowLit = new Color32(0xE2, 0xC2, 0x7D, 0xFF);
        [SerializeField] private Color _arrowDim = new Color32(0x8A, 0x80, 0x70, 0xFF);
        [SerializeField] private Color _cellBg = new Color(12 / 255f, 9 / 255f, 6 / 255f, 0.6f);
        [SerializeField] private Color _cellBgSelected = new Color(226 / 255f, 194 / 255f, 125 / 255f, 0.18f);
        [SerializeField] private Color _cellBorder = new Color(217 / 255f, 179 / 255f, 106 / 255f, 0.38f);
        [SerializeField] private Color _cellBorderSelected = new Color32(0xE2, 0xC2, 0x7D, 0xFF);
        [SerializeField] private Color _cellGlow = new Color(226 / 255f, 194 / 255f, 125 / 255f, 0.6f);
        [Tooltip("已經打過的格子")]
        [SerializeField] private Color _cellBgLit = new Color(226 / 255f, 194 / 255f, 125 / 255f, 0.10f);
        [SerializeField] private Color _cellBorderLit = new Color(226 / 255f, 194 / 255f, 125 / 255f, 0.85f);
        [Tooltip("已預輸入、等著接的下一招")]
        [SerializeField] private Color _cellBorderBuffered = new Color(226 / 255f, 194 / 255f, 125 / 255f, 0.95f);

        /// <summary>試招目前的狀態（由 MoveTrialController 每幀給）。照 F1 面板的亮法。</summary>
        public struct TrialView
        {
            public bool hasCurrent;
            public MoveKind current;
            public int currentIndex;
            /// <summary>一般攻擊已經打到第幾下（含），-1 = 沒有。</summary>
            public int litNormals;
            /// <summary>已預輸入、等著接的下一招（-1 = 沒有）。</summary>
            public int bufferedNormal, bufferedStrong;
            public bool bufferedPause, bufferedHold;
        }

        public bool HasMoves => _moves.Count > 0;

        private class CellView
        {
            public Image bg, border, glow, glyph;
            public Sprite normal, lit;
            public Move move;
        }

        private readonly List<Move> _moves = new List<Move>();
        private readonly List<CellView> _cells = new List<CellView>();
        private readonly List<GameObject> _spawned = new List<GameObject>();
        private Image[] _hArrows = new Image[0];   // [線, 頭] × 3（N→N）
        private Image[] _dArrows = new Image[0];   // [線, 頭] × 3（N→C）
        private WeaponActionSet _set;
        private TrialView _view = new TrialView { litNormals = -1, bufferedNormal = -1, bufferedStrong = -1 };

        private RectTransform Area => (RectTransform)transform;

        // ─────────────────────────── 對外 ───────────────────────────

        public void Build(WeaponActionSet set)
        {
            _set = set;
            Clear();
            if (set == null) return;

            CollectMoves(set);
            bool hasVariant = _moves.Exists(m => m.kind == MoveKind.Pause || m.kind == MoveKind.Hold);
            int rows = hasVariant ? 4 : 3;
            float totalH = (rows - 1) * _pitchY + _cell;
            float top = (_areaHeight - totalH) * 0.5f;
            float RowY(int r) => top + r * _pitchY;
            int spRow = hasVariant ? 3 : 2;

            // 列標題
            MakeLabel("一般攻擊", RowY(0));
            MakeLabel("強攻擊", RowY(1));
            if (hasVariant) MakeLabel("變招", RowY(2));
            MakeLabel("秘技", RowY(spRow));

            // 箭頭（先產生，格子蓋在上面）
            BuildArrows(RowY(0), RowY(1));

            // 格子
            foreach (var m in _moves)
            {
                if (m.kind == MoveKind.Special) m.row = spRow;
                _cells.Add(MakeCell(m, CellPos(m.col, RowY(m.row))));
            }

            // 變招說明
            if (hasVariant) BuildVariantNotes(set, RowY(2));

            // 秘技說明
            var sp = MakeText("特殊招式（用完需冷卻）", _noteFontSize + 4f, _noteColor, TextAlignmentOptions.MidlineLeft);
            Place(sp.rectTransform, _gridX + _cell + 30f, RowY(spRow), 500f, _cell);

            SetTrialView(new TrialView { litNormals = -1, bufferedNormal = -1, bufferedStrong = -1 });
        }

        public void SetTrialView(TrialView view)
        {
            _view = view;
            RefreshHighlight();
        }

        // ─────────────────────────── 資料 ───────────────────────────

        private void CollectMoves(WeaponActionSet set)
        {
            _moves.Clear();
            var c = set.combo;
            for (int i = 0; i < ComboMoveSet.Count; i++)
            {
                var n = c.GetNormal(i);
                if (n != null && n.IsValid) _moves.Add(new Move { kind = MoveKind.Normal, index = i, col = i, row = 0, clip = n.clip });
            }
            for (int i = 0; i < ComboMoveSet.Count; i++)
            {
                var s = c.GetCharge(i);
                if (s != null && s.IsValid) _moves.Add(new Move { kind = MoveKind.Strong, index = i, col = i, row = 1, clip = s.clip });
            }
            if (c.pauseCombo != null && c.pauseCombo.IsValid)
                _moves.Add(new Move { kind = MoveKind.Pause, col = 0, row = 2, clip = c.pauseCombo.clip });
            if (c.chargeHold != null && c.chargeHold.IsValid)
                _moves.Add(new Move { kind = MoveKind.Hold, col = 1, row = 2, clip = c.chargeHold.clip });

            AnimationClip special = null;
            if (set.actions != null)
                foreach (var a in set.actions)
                    if (a != null && a.action == PlayerActionId.SpecialSkill && a.clip != null) { special = a.clip; break; }
            _moves.Add(new Move { kind = MoveKind.Special, col = 0, row = 3, clip = special });
        }

        // ─────────────────────────── 產生 UI ───────────────────────────

        private Vector2 CellPos(int col, float rowY) => new Vector2(_gridX + col * _pitchX, rowY);

        private void Clear()
        {
            foreach (var go in _spawned) if (go != null) Destroy(go);
            _spawned.Clear();
            _cells.Clear();
            _moves.Clear();
            _hArrows = new Image[0];
            _dArrows = new Image[0];
        }

        private void Place(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
        }

        private GameObject NewChild(string name, Transform parent = null)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent != null ? parent : transform, false);
            if (parent == null) _spawned.Add(go);
            return go;
        }

        private TextMeshProUGUI MakeText(string text, float size, Color color, TextAlignmentOptions align, Transform parent = null)
        {
            var go = NewChild("Text_" + text, parent);
            var t = go.AddComponent<TextMeshProUGUI>();
            t.font = _font;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.raycastTarget = false;
            t.text = text;
            return t;
        }

        private Image MakeImage(string name, Sprite sprite, Color color, Transform parent = null, bool sliced = false)
        {
            var go = NewChild(name, parent);
            var img = go.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.raycastTarget = false;
            if (sliced) img.type = Image.Type.Sliced;
            return img;
        }

        private void MakeLabel(string text, float rowY)
        {
            var t = MakeText(text, _labelFontSize, _labelColor, TextAlignmentOptions.MidlineLeft);
            t.characterSpacing = 6f;
            Place(t.rectTransform, _labelX, rowY, 170f, _cell);
        }

        private CellView MakeCell(Move m, Vector2 pos)
        {
            var root = NewChild("Cell_" + m.kind + m.index);
            Place((RectTransform)root.transform, pos.x, pos.y, _cell, _cell);

            var v = new CellView();
            v.glow = MakeImage("Glow", _glow, _cellGlow, root.transform);
            Stretch(v.glow.rectTransform, -_cell * 0.5f);
            v.bg = MakeImage("Bg", _white, _cellBg, root.transform);
            Stretch(v.bg.rectTransform, 0f);
            v.border = MakeImage("Border", _thinFrame, _cellBorder, root.transform, true);
            Stretch(v.border.rectTransform, 0f);

            switch (m.kind)
            {
                case MoveKind.Normal:
                case MoveKind.Pause: v.normal = _square; v.lit = _squareLit; break;
                case MoveKind.Strong:
                case MoveKind.Hold: v.normal = _triangle; v.lit = _triangleLit; break;
                default: v.normal = _r1; v.lit = _r1Lit; break;
            }
            v.glyph = MakeImage("Glyph", v.normal, Color.white, root.transform);
            v.glyph.preserveAspect = true;
            var grt = v.glyph.rectTransform;
            grt.anchorMin = grt.anchorMax = grt.pivot = new Vector2(0.5f, 0.5f);
            grt.anchoredPosition = Vector2.zero;
            grt.sizeDelta = m.kind == MoveKind.Special ? new Vector2(_glyphSize * 1.2f, _glyphSize) : new Vector2(_glyphSize, _glyphSize);

            v.move = m;
            return v;
        }

        private static void Stretch(RectTransform rt, float inset)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(inset, inset);
            rt.offsetMax = new Vector2(-inset, -inset);
        }

        private void BuildArrows(float nY, float cY)
        {
            var h = new List<Image>();
            var d = new List<Image>();
            for (int i = 0; i < ComboMoveSet.Count - 1; i++)
            {
                float x0 = _gridX + i * _pitchX + _cell + 4.5f;
                float x1 = _gridX + (i + 1) * _pitchX - 4.5f;
                float y = nY + _cell * 0.5f;
                h.AddRange(MakeArrow(new Vector2(x0, y), new Vector2(x1, y)));
            }
            for (int k = 1; k < ComboMoveSet.Count; k++)
            {
                var a = new Vector2(_gridX + (k - 1) * _pitchX + _cell * 0.89f, nY + _cell + 3f);
                var b = new Vector2(_gridX + k * _pitchX + _cell * 0.09f, cY - 4.5f);
                d.AddRange(MakeArrow(a, b));
            }
            _hArrows = h.ToArray();
            _dArrows = d.ToArray();
        }

        /// <summary>從 a 指向 b 的箭頭（座標：區域左上角為原點、往下為正）。回傳 [線, 頭]。</summary>
        private Image[] MakeArrow(Vector2 a, Vector2 b)
        {
            Vector2 dir = (b - a).normalized;
            float len = Vector2.Distance(a, b);
            float angle = Mathf.Atan2(-(b.y - a.y), b.x - a.x) * Mathf.Rad2Deg;   // UI 往上為正

            var line = MakeImage("ArrowLine", _white, _arrowDim);
            var lrt = line.rectTransform;
            lrt.anchorMin = lrt.anchorMax = new Vector2(0f, 1f);
            lrt.pivot = new Vector2(0f, 0.5f);
            lrt.anchoredPosition = new Vector2(a.x, -a.y);
            lrt.sizeDelta = new Vector2(Mathf.Max(0f, len - _arrowHeadSize * 0.6f), _arrowWidth);
            lrt.localRotation = Quaternion.Euler(0f, 0f, angle);

            var head = MakeImage("ArrowHead", _arrowHead, _arrowDim);
            var hrt = head.rectTransform;
            hrt.anchorMin = hrt.anchorMax = new Vector2(0f, 1f);
            hrt.pivot = new Vector2(1f, 0.5f);
            hrt.anchoredPosition = new Vector2(b.x, -b.y);
            hrt.sizeDelta = new Vector2(_arrowHeadSize, _arrowHeadSize);
            hrt.localRotation = Quaternion.Euler(0f, 0f, angle);
            return new[] { line, head };
        }

        private void BuildVariantNotes(WeaponActionSet set, float rowY)
        {
            float x = _gridX + 2 * _pitchX + 33f;
            var c = set.combo;
            if (c.pauseCombo != null && c.pauseCombo.IsValid)
            {
                var items = new List<object> { "停頓連打" };
                for (int i = 0; i < c.pauseAfterNormal; i++) items.Add(_square);
                items.Add("停一拍");
                items.Add(_square);
                MakeNoteLine(items, x, rowY - 3f);
            }
            if (c.chargeHold != null && c.chargeHold.IsValid)
                MakeNoteLine(new List<object> { "蓄力", _triangle, "按住不放" }, x, rowY + _cell * 0.5f + 3f);
        }

        /// <summary>一行說明：第一個字串是金色名稱（固定寬度），之後是按鍵圖與說明文字。手動排位置，不用 LayoutGroup。</summary>
        private void MakeNoteLine(List<object> items, float x, float y)
        {
            const float lineH = 42f, icon = 33f, gap = 6f, labelW = 120f;
            float cx = x;
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i] is Sprite s)
                {
                    var img = MakeImage("NoteIcon", s, Color.white);
                    img.preserveAspect = true;
                    Place(img.rectTransform, cx, y + (lineH - icon) * 0.5f, icon, icon);
                    cx += icon + gap;
                }
                else
                {
                    bool isLabel = i == 0;
                    var t = MakeText((string)items[i], _noteFontSize, isLabel ? _gold : _noteColor, TextAlignmentOptions.MidlineLeft);
                    float w = isLabel ? labelW : t.GetPreferredValues((string)items[i]).x + 18f;
                    Place(t.rectTransform, isLabel ? cx : cx + 6f, y, w, lineH);
                    cx += isLabel ? labelW : w + gap;
                }
            }
        }

        // ─────────────────────────── 高亮 ───────────────────────────

        private void RefreshHighlight()
        {
            var v = _view;
            foreach (var c in _cells)
            {
                var m = c.move;
                bool current = v.hasCurrent && v.current == m.kind && (m.kind == MoveKind.Pause || m.kind == MoveKind.Hold || m.kind == MoveKind.Special || v.currentIndex == m.index);
                bool lit = !current && m.kind == MoveKind.Normal && m.index <= v.litNormals;
                bool buffered = !current && (
                    (m.kind == MoveKind.Normal && m.index == v.bufferedNormal) ||
                    (m.kind == MoveKind.Strong && m.index == v.bufferedStrong) ||
                    (m.kind == MoveKind.Pause && v.bufferedPause) ||
                    (m.kind == MoveKind.Hold && v.bufferedHold));

                c.glow.enabled = current;
                c.bg.color = current ? _cellBgSelected : (lit || buffered) ? _cellBgLit : _cellBg;
                c.border.sprite = current || buffered ? _thickFrame : _thinFrame;
                c.border.color = current ? _cellBorderSelected : buffered ? _cellBorderBuffered : lit ? _cellBorderLit : _cellBorder;
                // 已經按好、等著接的下一招也換紅色按鍵：一按下去就看得到回饋
                c.glyph.sprite = current || buffered ? c.lit : c.normal;
            }

            // 連段路徑：已經打過的一般攻擊之間亮起；正在出的強攻擊亮它那條斜線
            int dLit = v.hasCurrent && v.current == MoveKind.Strong ? v.currentIndex : -1;
            for (int i = 0; i < _hArrows.Length; i++) _hArrows[i].color = (i / 2) + 1 <= v.litNormals ? _arrowLit : _arrowDim;
            for (int i = 0; i < _dArrows.Length; i++) _dArrows[i].color = (i / 2 + 1) == dLit ? _arrowLit : _arrowDim;
        }
    }
}
