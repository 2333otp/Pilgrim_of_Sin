using UnityEngine;

namespace PilgrimOfSin
{
    /// <summary>
    /// 鎖定準星 UI。
    /// 鎖定時顯示圖示在畫面中央（鏡頭已對準 Boss，中央即準心位置）。
    /// </summary>
    [DefaultExecutionOrder(200)]
    public class LockOnMarkerUI : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private StateMachine.CameraController _cameraController;

        [Header("Marker")]
        [SerializeField] private RectTransform _markerRect;

        private void Start()
        {
            if (_cameraController == null)
                _cameraController = FindFirstObjectByType<StateMachine.CameraController>();

            if (_markerRect != null)
                _markerRect.gameObject.SetActive(false);
        }

        private void LateUpdate()
        {
            if (_cameraController == null || _markerRect == null) return;

            bool locked = _cameraController.IsLockedOn && _cameraController.LockTarget != null;

            _markerRect.gameObject.SetActive(locked);

            if (locked)
                _markerRect.anchoredPosition = Vector2.zero;
        }
    }
}
