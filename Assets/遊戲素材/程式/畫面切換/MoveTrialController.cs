using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using PilgrimOfSin.StateMachine;
using Kind = PilgrimOfSin.MoveListTableUI.MoveKind;

namespace PilgrimOfSin
{
    /// <summary>
    /// 武器介紹第 2 頁的「按鍵試招」。玩家直接按 □ / △ / R1：
    ///   · 出招表照實際連段規則亮起（打過的路徑、正在出的招、預輸入的下一招）
    ///   · 右邊的小 Muir 同步做出那一招
    ///   · 出招表上方顯示剛剛按了哪些鍵（像格鬥遊戲的訓練模式）
    /// 判定規則與遊戲中的 MoveAttackState 完全相同（接收開始、取消時間點、停頓連打寬限、△ 長按蓄力），
    /// 數值全部讀武器的 WeaponActionSet，組員調整出招節奏這裡自動跟著變。
    /// 暫停選單時 timeScale = 0，所以這裡一律用不受暫停影響的時間。
    /// </summary>
    public class MoveTrialController : MonoBehaviour
    {
        [SerializeField] private MoveListTableUI _table;
        [SerializeField] private MovePreviewStage _preview;
        [Tooltip("遊戲用的主角 Animator（Muir_Gameplay.controller）。動作清單裡放的動畫不是人形格式時，到這裡找同名的人形版本。")]
        [SerializeField] private RuntimeAnimatorController _gameplayController;

        [Header("剛剛按了什麼")]
        [SerializeField] private RectTransform _historyRoot;
        [SerializeField] private TMP_FontAsset _font;
        [SerializeField] private Sprite _square;
        [SerializeField] private Sprite _triangle;
        [SerializeField] private Sprite _r1;
        [SerializeField] private int _historyMax = 10;
        [SerializeField] private float _historyIcon = 36f;
        [SerializeField] private Color _historyNoteColor = new Color32(0xCF, 0xC4, 0xAD, 0xFF);
        [Tooltip("按太早、被接招規則丟掉的按鍵顏色")]
        [SerializeField] private Color _historyIgnoredColor = new Color(0.45f, 0.42f, 0.38f, 0.55f);

        [Header("節奏")]
        [Tooltip("一套連段打完後，路徑保持亮著幾秒再熄掉（讓玩家看清楚剛剛打出什麼）")]
        [SerializeField] private float _afterglowSeconds = 0.8f;
        [Tooltip("取消時間點開關（同 PlayerController 的「攻擊可取消」，預設開）")]
        [SerializeField] private bool _enableAttackCancel = true;

        private WeaponActionSet _actions;
        private ComboMoveSet _set;
        private AnimationClip _idleClip;
        private AnimationClip _specialClip;
        private bool _running;

        // 目前這一招（同 MoveAttackState）
        private ComboMoveEntry _move;
        private Kind _kind;
        private int _index;
        private bool _special;
        private float _elapsed, _graceTimer;
        private bool _bufferedLight, _bufferedHeavy, _bufferedPause;
        private float _heavyPressTime;
        private bool _heavyReleased, _awaitingHold;
        private int _pendingStrongIndex;
        private float _afterglow;
        private bool _acceptedLight, _acceptedHeavy, _acceptedSpecial;

        private readonly List<GameObject> _historyItems = new List<GameObject>();
        private bool _historyFresh = true;

        // ─────────────────────────── 對外 ───────────────────────────

        /// <summary>打開第 2 頁：換成這把武器（1~4）並開始接受按鍵。</summary>
        public void Begin(WeaponActionSet actions, int weaponIndex)
        {
            _actions = actions;
            _set = actions != null ? actions.combo : null;
            _idleClip = FindActionClip(PlayerActionId.Idle);
            _specialClip = FindActionClip(PlayerActionId.SpecialSkill);
            _running = true;
            ClearHistory();
            if (_preview != null)
            {
                _preview.Show(weaponIndex, FramingClips());
                PlayIdle();
            }
            ResetCombo(true);
        }

        public void End()
        {
            _running = false;
            ResetCombo(true);
            ClearHistory();
            if (_preview != null) _preview.Hide();
        }

        /// <summary>取景用：待機＋這把武器所有招式＋秘技。</summary>
        private List<AnimationClip> FramingClips()
        {
            var list = new List<AnimationClip>();
            if (_idleClip != null) list.Add(_idleClip);
            if (_set != null)
            {
                for (int i = 0; i < ComboMoveSet.Count; i++)
                {
                    if (Valid(_set.GetNormal(i))) list.Add(_set.GetNormal(i).clip);
                    if (Valid(_set.GetCharge(i))) list.Add(_set.GetCharge(i).clip);
                }
                if (Valid(_set.pauseCombo)) list.Add(_set.pauseCombo.clip);
                if (Valid(_set.chargeHold)) list.Add(_set.chargeHold.clip);
            }
            if (_specialClip != null) list.Add(_specialClip);
            return list;
        }

        /// <summary>
        /// 找動作清單裡這個動作的動畫。小 Muir 是人形骨架，只能播人形（Humanoid）動畫：
        /// 放的動畫不是人形格式（例如鉛筆秘技的 Update/Pencil/SP_skill_pencil.anim 是 Generic）時，
        /// 改用 Animator 原本的那段（baseClip），不然整段會定格不動。
        /// </summary>
        private AnimationClip FindActionClip(PlayerActionId id)
        {
            if (_actions?.actions == null) return null;
            foreach (var a in _actions.actions)
            {
                if (a == null || a.action != id) continue;
                if (a.clip != null && a.clip.humanMotion) return a.clip;
                if (a.baseClip != null && a.baseClip.humanMotion) return a.baseClip;
                // 都不是人形：遊戲中實際播的是 Animator 裡同名的人形版本（SO 的替換找不到對象就不會發生）
                var human = FindHumanoidInController(a.clip != null ? a.clip.name : a.baseClip != null ? a.baseClip.name : null);
                if (human != null) return human;
                if (a.clip != null) return a.clip;
            }
            return null;
        }

        private AnimationClip FindHumanoidInController(string clipName)
        {
            if (_gameplayController == null || string.IsNullOrEmpty(clipName)) return null;
            foreach (var c in _gameplayController.animationClips)
                if (c != null && c.humanMotion && string.Equals(c.name, clipName, System.StringComparison.OrdinalIgnoreCase)) return c;
            return null;
        }

        private void OnDisable()
        {
            if (_running) End();
        }

        // ─────────────────────────── 每幀 ───────────────────────────

        private void Update()
        {
            if (!_running || _set == null) return;
            var reader = PauseMenuUI.Instance != null ? PauseMenuUI.Instance.InputReader : null;
            if (reader == null) return;

            float dt = Time.unscaledDeltaTime;
            bool light = reader.LightAttackPressed;
            bool heavy = reader.HeavyAttackPressed;
            bool special = reader.SpecialPressed;
            if (special) reader.ConsumeSpecial();   // 別讓選單裡按的 R1 留到回遊戲時才放出來

            // 上一套已收招、這一下是新的開始：先清掉上一套的紀錄
            if ((light || heavy || special) && _historyFresh && _move == null && !_special && !_awaitingHold) ClearHistory();

            _acceptedLight = _acceptedHeavy = _acceptedSpecial = false;
            Step(dt, light, heavy, special, reader.HeavyAttackHeld);

            // 剛剛按了什麼：有被接受的正常顯示，按太早被規則丟掉的顯示成灰色
            if (light) AddHistory(_square, _acceptedLight);
            if (heavy) AddHistory(_triangle, _acceptedHeavy);
            if (special) AddHistory(_r1, _acceptedSpecial);
            PushView();
        }

        private void Step(float dt, bool light, bool heavy, bool special, bool heavyHeld)
        {
            // 秘技：R1 直接放（遊戲中 MoveAttackState 也是 R1 優先）；秘技播放中再按不算（遊戲中放秘技時也不能再放一次）
            if (special && _specialClip != null && !_special) { _acceptedSpecial = true; StartSpecial(); return; }

            if (_special)
            {
                _elapsed += dt;
                if (_elapsed >= _specialClip.length) Finish();
                return;
            }

            if (!heavyHeld) _heavyReleased = true;

            // 待機中：□ = N1，△ = C1（有蓄力動畫時先判斷是不是長按）
            if (_move == null && !_awaitingHold)
            {
                if (_afterglow > 0f) _afterglow -= dt;
                if (light) { _acceptedLight = true; StartNormal(0); }
                else if (heavy) { _acceptedHeavy = true; BeginHeavyTracking(heavyHeld); QueueStrong(0); }
                return;
            }

            if (_awaitingHold) { ResolveHold(); return; }

            _elapsed += dt * Mathf.Max(0.1f, _move.speed);
            float length = _move.clip.length;
            float progress = length > 0f ? _elapsed / length : 1f;
            bool pauseEligible = PauseEligible;

            bool alreadyBuffered = _bufferedLight || _bufferedHeavy || _bufferedPause;
            if (!alreadyBuffered && progress >= _move.inputStartRatio)
            {
                if (heavy) { _bufferedHeavy = true; _acceptedHeavy = true; BeginHeavyTracking(heavyHeld); }
                else if (light)
                {
                    _acceptedLight = true;
                    if (pauseEligible && progress >= _move.cancelRatio) _bufferedPause = true;
                    else _bufferedLight = true;
                }
            }

            bool canCancel = _enableAttackCancel ? progress >= _move.cancelRatio : progress >= 1f;
            if (canCancel)
            {
                if (_bufferedPause) { if (!StartPause()) StartNormal(0); return; }
                if (_bufferedLight) { if (_kind != Kind.Normal || !StartNormal(_index + 1)) StartNormal(0); return; }
                if (_bufferedHeavy)
                {
                    int target = _kind == Kind.Normal && progress < 1f ? _index + 1 : 0;
                    QueueStrong(target);
                    return;
                }
            }

            if (progress >= 1f)
            {
                if (pauseEligible)
                {
                    _graceTimer += dt;
                    if (_graceTimer <= _set.pauseGraceSeconds) return;
                }
                Finish();
            }
        }

        private bool PauseEligible =>
            _move != null && _kind == Kind.Normal && _index == _set.pauseAfterNormal - 1 &&
            _set.pauseCombo != null && _set.pauseCombo.IsValid;

        // ── △ 長按判斷（同 MoveAttackState）──────────────────────────
        private bool HasHoldMove => _set.chargeHold != null && _set.chargeHold.IsValid;
        private bool HeldLongEnough => !_heavyReleased && Time.unscaledTime - _heavyPressTime >= _set.chargeHoldSeconds;

        private void BeginHeavyTracking(bool heldNow)
        {
            _heavyPressTime = Time.unscaledTime;
            _heavyReleased = !heldNow;
        }

        private void QueueStrong(int target)
        {
            _bufferedHeavy = false;
            if (!HasHoldMove || _heavyReleased) { StartStrongOrFallback(target); return; }
            if (HeldLongEnough) { StartHold(); return; }
            _awaitingHold = true;
            _pendingStrongIndex = target;
        }

        private void ResolveHold()
        {
            if (_heavyReleased) { _awaitingHold = false; StartStrongOrFallback(_pendingStrongIndex); }
            else if (HeldLongEnough) { _awaitingHold = false; StartHold(); }
        }

        private void StartStrongOrFallback(int target)
        {
            if (StartStrong(target) || StartStrong(0)) return;
            if (_move == null) StartNormal(0);
        }

        // ── 出招 ───────────────────────────────────────────────────────
        private bool StartNormal(int i) => Begin(_set.GetNormal(i), Kind.Normal, i);
        private bool StartStrong(int i) => Begin(_set.GetCharge(i), Kind.Strong, i);
        private bool StartPause()
        {
            bool ok = Begin(_set.pauseCombo, Kind.Pause, 0);
            if (ok) InsertHistoryGap();
            return ok;
        }
        private bool StartHold() => Begin(_set.chargeHold, Kind.Hold, 0);

        private bool Begin(ComboMoveEntry move, Kind kind, int index)
        {
            if (move == null || !move.IsValid) return false;
            _move = move;
            _kind = kind;
            _index = index;
            _special = false;
            _elapsed = 0f;
            _graceTimer = 0f;
            _afterglow = 0f;
            _bufferedLight = _bufferedHeavy = _bufferedPause = false;
            _historyFresh = false;
            _preview?.Play(move.clip, move.speed, false);
            return true;
        }

        private void StartSpecial()
        {
            _move = null;
            _awaitingHold = false;
            _special = true;
            _kind = Kind.Special;
            _elapsed = 0f;
            _afterglow = 0f;
            _bufferedLight = _bufferedHeavy = _bufferedPause = false;
            _historyFresh = false;
            _preview?.Play(_specialClip, 1f, false);
        }

        /// <summary>收招：回待機，路徑再亮一下下才熄掉；下一次從待機出招時清掉「剛剛按了什麼」。</summary>
        private void Finish()
        {
            _move = null;
            _special = false;
            _awaitingHold = false;
            _afterglow = _afterglowSeconds;
            _historyFresh = true;
            PlayIdle();
        }

        private void ResetCombo(bool clearView)
        {
            _move = null;
            _special = false;
            _awaitingHold = false;
            _bufferedLight = _bufferedHeavy = _bufferedPause = false;
            _afterglow = 0f;
            _historyFresh = true;
            if (clearView) PushView();
        }

        private void PlayIdle()
        {
            if (_preview != null && _idleClip != null) _preview.Play(_idleClip, 1f, true);
        }

        // ─────────────────────────── 顯示 ───────────────────────────

        private Kind _lastKind;
        private int _lastIndex, _lastLit;

        private void PushView()
        {
            if (_table == null) return;
            var v = new MoveListTableUI.TrialView { litNormals = -1, bufferedNormal = -1, bufferedStrong = -1 };

            bool active = _move != null || _special;
            if (active)
            {
                v.hasCurrent = true;
                v.current = _special ? Kind.Special : _kind;
                v.currentIndex = _index;
                v.litNormals = _special ? -1 : _kind switch
                {
                    Kind.Normal => _index,
                    Kind.Strong => _index - 1,
                    Kind.Pause => _set.pauseAfterNormal - 1,
                    _ => -1,
                };
                _lastKind = v.current; _lastIndex = v.currentIndex; _lastLit = v.litNormals;
            }
            else if (_afterglow > 0f)
            {
                // 剛收招：保留最後一招的樣子
                v.hasCurrent = true;
                v.current = _lastKind; v.currentIndex = _lastIndex; v.litNormals = _lastLit;
            }

            if (_move != null && _bufferedLight)
                v.bufferedNormal = _kind == Kind.Normal && _index + 1 < ComboMoveSet.Count && Valid(_set.GetNormal(_index + 1)) ? _index + 1 : 0;
            if (_move != null && _bufferedHeavy)
            {
                int c = _kind == Kind.Normal ? _index + 1 : 0;
                v.bufferedStrong = c < ComboMoveSet.Count && Valid(_set.GetCharge(c)) ? c : 0;
            }
            if (_awaitingHold)
            {
                v.bufferedStrong = Valid(_set.GetCharge(_pendingStrongIndex)) ? _pendingStrongIndex : 0;
                v.bufferedHold = true;
            }
            v.bufferedPause = _move != null && _bufferedPause;
            _table.SetTrialView(v);
        }

        private static bool Valid(ComboMoveEntry e) => e != null && e.IsValid;

        // ── 剛剛按了什麼 ───────────────────────────────────────────────
        private void AddHistory(Sprite s, bool accepted)
        {
            if (_historyRoot == null || s == null) return;
            var go = new GameObject(accepted ? "Key" : "KeyIgnored", typeof(RectTransform));
            go.transform.SetParent(_historyRoot, false);
            var img = go.AddComponent<Image>();
            img.sprite = s;
            img.preserveAspect = true;
            img.raycastTarget = false;
            if (!accepted) img.color = _historyIgnoredColor;
            float w = s == _r1 ? _historyIcon * 1.6f : _historyIcon;
            ((RectTransform)go.transform).sizeDelta = new Vector2(w, _historyIcon);
            _historyItems.Add(go);
            Relayout();
        }

        /// <summary>停頓連打：在最後一個 □ 前面插一個「‥」，看得出中間停了一拍。</summary>
        private void InsertHistoryGap()
        {
            if (_historyRoot == null || _historyItems.Count == 0) return;
            var go = new GameObject("Gap", typeof(RectTransform));
            go.transform.SetParent(_historyRoot, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            t.font = _font;
            t.text = "‥";
            t.fontSize = _historyIcon * 0.8f;
            t.color = _historyNoteColor;
            t.alignment = TextAlignmentOptions.Center;
            t.raycastTarget = false;
            ((RectTransform)go.transform).sizeDelta = new Vector2(_historyIcon * 0.8f, _historyIcon);
            _historyItems.Insert(_historyItems.Count - 1, go);
            Relayout();
        }

        private void Relayout()
        {
            while (_historyItems.Count > _historyMax)
            {
                Destroy(_historyItems[0]);
                _historyItems.RemoveAt(0);
            }
            float x = 0f;
            for (int i = 0; i < _historyItems.Count; i++)
            {
                var rt = (RectTransform)_historyItems[i].transform;
                rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f);
                rt.pivot = new Vector2(0f, 0.5f);
                rt.anchoredPosition = new Vector2(x, 0f);
                x += rt.sizeDelta.x + 8f;
                // 越舊越淡（被丟掉的按鍵固定灰色，不參與）
                if (_historyItems[i].name == "KeyIgnored") continue;
                float a = Mathf.Lerp(0.45f, 1f, (i + 1f) / _historyItems.Count);
                var g = _historyItems[i].GetComponent<Graphic>();
                if (g != null) { var c = g.color; c.a = a; g.color = c; }
            }
        }

        private void ClearHistory()
        {
            foreach (var go in _historyItems) if (go != null) Destroy(go);
            _historyItems.Clear();
        }
    }
}
