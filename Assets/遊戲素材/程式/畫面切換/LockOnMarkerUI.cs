using UnityEngine;

namespace PilgrimOfSin
{
    /// <summary>
    /// 鎖定準星 UI。
    /// 鎖定時顯示圖示在畫面中央（鏡頭已對準 Boss，中央即準心位置）。
    /// 圖示大小依 Boss 在畫面上的投影高度縮放：Boss 越遠、畫面上越小，圖示跟著縮小，
    /// 避免固定大小的圖示把遠處的 Boss 整個蓋住。
    /// </summary>
    [DefaultExecutionOrder(200)]
    public class LockOnMarkerUI : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private StateMachine.CameraController _cameraController;

        [Header("Marker")]
        [SerializeField] private RectTransform _markerRect;

        [Header("Distance Scaling")]
        [Tooltip("圖示高度 = Boss 畫面高度 × 這個比例（再限制在 Min/Max Scale 之間）。越小圖示越不擋 Boss。")]
        [SerializeField] private float _sizeToBossHeightRatio = 0.35f;
        [Tooltip("縮放下限（相對於 Prefab 原本大小）。避免遠到看不見。")]
        [SerializeField] private float _minScale = 0.25f;
        [Tooltip("縮放上限（相對於 Prefab 原本大小）。1 = 近距離維持原本大小。")]
        [SerializeField] private float _maxScale = 1f;
        [SerializeField] private float _scaleSmoothing = 12f;
        [Tooltip("讀不到 Boss 的 Renderer 時，改用這個世界高度（公尺）估算。")]
        [SerializeField] private float _fallbackTargetHeight = 3f;

        private Camera _camera;
        private Canvas _canvas;
        private Vector2 _baseSize;
        private Transform _cachedTarget;
        private Renderer[] _targetRenderers;
        private float _currentScale = 1f;

        private void Start()
        {
            if (_cameraController == null)
                _cameraController = FindFirstObjectByType<StateMachine.CameraController>();

            if (_markerRect != null)
            {
                _baseSize = _markerRect.sizeDelta;
                _canvas = _markerRect.GetComponentInParent<Canvas>();
                _markerRect.gameObject.SetActive(false);
            }
        }

        private void LateUpdate()
        {
            if (_cameraController == null || _markerRect == null) return;

            var target = _cameraController.LockTarget;
            bool locked = _cameraController.IsLockedOn && target != null;

            if (_markerRect.gameObject.activeSelf != locked)
            {
                _markerRect.gameObject.SetActive(locked);
                // 剛鎖定時直接跳到目標大小，不要從上次的大小慢慢補間
                if (locked) _currentScale = -1f;
            }

            if (!locked) return;

            _markerRect.anchoredPosition = Vector2.zero;
            UpdateScale(target);
        }

        private void UpdateScale(Transform target)
        {
            if (_camera == null) _camera = Camera.main;
            if (_camera == null) return;

            if (target != _cachedTarget)
            {
                _cachedTarget = target;
                _targetRenderers = target.GetComponentsInChildren<Renderer>();
            }

            float worldHeight = GetTargetWorldHeight(target, out Vector3 center);

            // 沿鏡頭前方的距離（不是直線距離），投影高度才準
            float depth = Vector3.Dot(center - _camera.transform.position, _camera.transform.forward);
            if (depth < 0.1f) return;

            float screenHeightPx = worldHeight
                                   / (2f * depth * Mathf.Tan(_camera.fieldOfView * 0.5f * Mathf.Deg2Rad))
                                   * Screen.height;

            // 螢幕像素 → Canvas 單位（Canvas Scaler 會依解析度縮放）
            float canvasScale = _canvas != null && _canvas.scaleFactor > 0f ? _canvas.scaleFactor : 1f;
            float desiredHeight = screenHeightPx / canvasScale * _sizeToBossHeightRatio;

            float baseHeight = Mathf.Max(1f, _baseSize.y);
            float targetScale = Mathf.Clamp(desiredHeight / baseHeight, _minScale, _maxScale);

            _currentScale = _currentScale < 0f
                ? targetScale
                : Mathf.Lerp(_currentScale, targetScale, 1f - Mathf.Exp(-_scaleSmoothing * Time.unscaledDeltaTime));

            _markerRect.localScale = new Vector3(_currentScale, _currentScale, 1f);
        }

        private float GetTargetWorldHeight(Transform target, out Vector3 center)
        {
            if (_targetRenderers != null && _targetRenderers.Length > 0)
            {
                bool has = false;
                Bounds b = default;
                foreach (var r in _targetRenderers)
                {
                    // 粒子/拖尾特效的範圍會亂放大，只用實體網格
                    if (r == null || !r.enabled || !(r is MeshRenderer || r is SkinnedMeshRenderer)) continue;
                    if (!has) { b = r.bounds; has = true; }
                    else b.Encapsulate(r.bounds);
                }
                if (has && b.size.y > 0.01f)
                {
                    center = b.center;
                    return b.size.y;
                }
            }

            center = target.position + Vector3.up * (_fallbackTargetHeight * 0.5f);
            return _fallbackTargetHeight;
        }
    }
}
