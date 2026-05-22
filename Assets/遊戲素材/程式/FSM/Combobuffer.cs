using System.Collections.Generic;

namespace PilgrimOfSin.StateMachine
{
    /// <summary>
    /// 記錄玩家的輕/重攻擊輸入序列，判斷是否觸發連段。
    /// 每把武器有 4 種連段，以鉛筆武器的定義為預設。
    /// </summary>
    public class ComboBuffer
    {
        public enum AttackInput { Light, Heavy }

        private readonly List<AttackInput> _inputs = new List<AttackInput>();

        /// <summary>目前記錄的連段索引（1~4），0 表示尚未判斷或輸入不符。</summary>
        public int CurrentComboIndex { get; private set; }

        /// <summary>最後一個輸入，供攻擊狀態判斷下一步。</summary>
        public AttackInput LastInput => _inputs.Count > 0
                                        ? _inputs[_inputs.Count - 1]
                                        : AttackInput.Light;

        private const float InputWindow = 0.5f; // 秒：連段輸入窗口
        private float _windowTimer;

        public void AddInput(AttackInput input)
        {
            _inputs.Add(input);
            _windowTimer = InputWindow;
        }

        public void Tick(float dt)
        {
            if (_inputs.Count == 0) return;
            _windowTimer -= dt;
            if (_windowTimer <= 0f) Reset();
        }

        /// <summary>嘗試取得當前輸入序列對應的連段索引。</summary>
        public bool TryGetCombo(out int comboIndex)
        {
            comboIndex = EvaluateCombo();
            CurrentComboIndex = comboIndex;
            if (comboIndex > 0)
                UnityEngine.Debug.Log($"[ComboBuffer] 觸發 Combo{comboIndex}，輸入序列：{string.Join(",", _inputs)}");
            return comboIndex > 0;
        }

        public void Reset()
        {
            _inputs.Clear();
            CurrentComboIndex = 0;
            _windowTimer = 0f;
        }

        /// <summary>
        /// 以鉛筆武器的連段定義為預設：
        ///   Combo1: 輕重
        ///   Combo2: 重重
        ///   Combo3: 重輕
        ///   Combo4: 輕輕輕重
        /// 其他武器的連段定義從 WeaponData ScriptableObject 讀取（後續擴充）。
        /// </summary>
        private int EvaluateCombo()
        {
            if (_inputs.Count < 2) return 0;

            // Combo4: 輕輕輕重（需先判斷，避免被 Combo1 提前匹配）
            if (_inputs.Count >= 4
                && _inputs[0] == AttackInput.Light
                && _inputs[1] == AttackInput.Light
                && _inputs[2] == AttackInput.Light
                && _inputs[3] == AttackInput.Heavy) return 4;

            // Combo1: 輕重
            if (_inputs[0] == AttackInput.Light
                && _inputs[1] == AttackInput.Heavy) return 1;

            // Combo2: 重重
            if (_inputs[0] == AttackInput.Heavy
                && _inputs[1] == AttackInput.Heavy) return 2;

            // Combo3: 重輕
            if (_inputs[0] == AttackInput.Heavy
                && _inputs[1] == AttackInput.Light) return 3;

            return 0;
        }
    }
}