using UnityEngine;
using UnityEngine.EventSystems;

namespace PilgrimOfSin
{
    /// <summary>
    /// 掛在使用 Unity 內建 EventSystem 導航的按鈕上，
    /// 手把/鍵盤導航選中或滑鼠停留時放大，離開/取消選中時恢復，
    /// 跟 PauseMenuUI 的選中縮放效果一致（滑鼠停留即觸發，不需按下）。
    /// </summary>
    public class NavSelectScale : MonoBehaviour, ISelectHandler, IDeselectHandler, IPointerEnterHandler, IPointerExitHandler
    {
        [SerializeField] private float _selectedScale = 1.08f;

        public void OnSelect(BaseEventData eventData)
        {
            transform.localScale = Vector3.one * _selectedScale;
        }

        public void OnDeselect(BaseEventData eventData)
        {
            transform.localScale = Vector3.one;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            transform.localScale = Vector3.one * _selectedScale;
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            transform.localScale = Vector3.one;
        }

        // 面板被切換隱藏時（例如按 ESC 從設置選單返回主選單），
        // OnPointerExit / OnDeselect 不會被觸發，縮放狀態會卡住。
        // OnDisable 保證按鈕不管因為什麼原因被停用，縮放都會強制復原。
        private void OnDisable()
        {
            transform.localScale = Vector3.one;
        }
    }
}
