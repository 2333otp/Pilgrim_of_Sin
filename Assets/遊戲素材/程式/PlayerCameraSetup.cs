using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// 掛在 CinemachineCamera 上，自動找到 Player 並設定跟隨與注視目標。
/// </summary>
[RequireComponent(typeof(CinemachineCamera))]
public class PlayerCameraSetup : MonoBehaviour
{
    [Tooltip("留空則自動尋找場景中的 Player 物件")]
    public Transform playerTarget;

    private void Awake()
    {
        if (playerTarget == null)
        {
            var player = FindFirstObjectByType<Player>();
            if (player != null)
                playerTarget = player.transform;
        }

        if (playerTarget == null)
        {
            Debug.LogWarning("[PlayerCameraSetup] 找不到 Player，請手動把 Cube 拖到 Player Target 欄位");
            return;
        }

        var vcam = GetComponent<CinemachineCamera>();
        vcam.Follow = playerTarget;
        vcam.LookAt = playerTarget;

        // 確保有旋轉追蹤組件，讓攝影機始終看向角色
        if (GetComponent<CinemachineRotationComposer>() == null)
        {
            var composer = gameObject.AddComponent<CinemachineRotationComposer>();
            composer.TargetOffset = new Vector3(0f, 0.8f, 0f); // 看向角色腰部偏上
        }
    }
}
