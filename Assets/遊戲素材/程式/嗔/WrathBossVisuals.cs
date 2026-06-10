using UnityEngine;

namespace PilgrimOfSin.StateMachine
{
    public class WrathBossVisuals : MonoBehaviour
    {
        [Header("Body Renderers")]
        [SerializeField] private Renderer _bodyRenderer;
        [SerializeField] private Renderer _headRenderer;

        [Header("Hitbox Visuals (shown when collider active)")]
        [SerializeField] private Renderer _dashVisualRenderer;
        [SerializeField] private Renderer _explosionVisualRenderer;
        [SerializeField] private Collider _dashCollider;
        [SerializeField] private Collider _explosionCollider;

        private static readonly Color ColorIdle        = Color.white;
        private static readonly Color ColorDash        = new Color(1f,   0.15f, 0.1f);
        private static readonly Color ColorVertex      = new Color(1f,   0.55f, 0.0f);
        private static readonly Color ColorVertexSafe  = new Color(0.2f, 0.8f,  0.2f); // 畫已改寫 → 綠色
        private static readonly Color ColorAttack1     = new Color(1f,   0.2f,  0.0f);
        private static readonly Color ColorAttack2     = new Color(1f,   0.0f,  0.35f);
        private static readonly Color ColorAttack3     = new Color(0.7f, 0f,    0.85f);
        private static readonly Color ColorStagger     = new Color(0.3f, 0.5f,  1f);
        private static readonly Color ColorDead        = new Color(0.15f,0.15f, 0.15f);

        private WrathBossController _boss;
        private WrathBossStateType _lastState = (WrathBossStateType)(-1);
        private bool _lastPaintingModified;

        private void Awake()
        {
            _boss = GetComponent<WrathBossController>();
        }

        private void Update()
        {
            if (_boss == null) return;

            var state = _boss.CurrentState;
            bool paintingModified = _boss.CurrentPointPaintingModified;

            if (state != _lastState || paintingModified != _lastPaintingModified)
            {
                _lastState = state;
                _lastPaintingModified = paintingModified;
                var c = GetStateColor(state, paintingModified);
                SetColor(_bodyRenderer, c);
                SetColor(_headRenderer, c);
            }

            if (_dashVisualRenderer && _dashCollider)
                _dashVisualRenderer.enabled = _dashCollider.enabled;

            if (_explosionVisualRenderer && _explosionCollider)
                _explosionVisualRenderer.enabled = _explosionCollider.enabled;
        }

        private static void SetColor(Renderer r, Color c)
        {
            if (r != null) r.material.color = c;
        }

        private static Color GetStateColor(WrathBossStateType s, bool paintingModified) => s switch
        {
            WrathBossStateType.Idle         => ColorIdle,
            WrathBossStateType.Dash         => ColorDash,
            WrathBossStateType.StayAtVertex => paintingModified ? ColorVertexSafe : ColorVertex,
            WrathBossStateType.Attack1      => ColorAttack1,
            WrathBossStateType.Attack2      => ColorAttack2,
            WrathBossStateType.Attack3      => ColorAttack3,
            WrathBossStateType.Stagger      => ColorStagger,
            WrathBossStateType.Dead         => ColorDead,
            _                               => ColorIdle,
        };
    }
}
