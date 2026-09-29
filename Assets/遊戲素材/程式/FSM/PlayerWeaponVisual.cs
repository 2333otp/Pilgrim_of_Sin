using System.Collections.Generic;
using UnityEngine;

namespace PilgrimOfSin.StateMachine
{
    /// <summary>
    /// 依 PlayerController.OnWeaponSwitched 動態把武器模型掛到角色手骨（hand_attach）上。
    /// 索引對應：1鉛筆(右手) 2畫筆(右手) 3畫刀(雙手各一把) 4調色盤(左手)。
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class PlayerWeaponVisual : MonoBehaviour
    {
        [Header("武器模型（索引對應 CurrentWeaponIndex）")]
        [SerializeField] private GameObject _pencilPrefab;
        [SerializeField] private GameObject _brushPrefab;
        [SerializeField] private GameObject _paintKnifePrefab;
        [SerializeField] private GameObject _palettePrefab;

        [Header("手部掛點骨骼名稱")]
        [SerializeField] private string _handAttachLeftBoneName = "DEF-hand_attach.L";
        [SerializeField] private string _handAttachRightBoneName = "DEF-hand_attach.R";

        // 部分武器模型的建模軸向跟手骨朝向不一致，掛上去角度會歪，靠這幾個欄位在 Inspector 微調到對齊為止。
        [Header("各武器軸向校正（掛上後方向不對時在這裡調）")]
        [SerializeField] private Vector3 _pencilRotationOffset;
        [SerializeField] private Vector3 _brushRotationOffset;
        [SerializeField] private Vector3 _paintKnifeRotationOffsetRight;
        [SerializeField] private Vector3 _paintKnifeRotationOffsetLeft;
        [SerializeField] private Vector3 _paletteRotationOffset;

        private PlayerController _player;
        private Transform _handAttachL;
        private Transform _handAttachR;
        private readonly List<GameObject> _spawned = new List<GameObject>();

        private void Awake()
        {
            _player = GetComponent<PlayerController>();

            var root = GetComponent<Animator>().transform;
            _handAttachL = FindDescendant(root, _handAttachLeftBoneName);
            _handAttachR = FindDescendant(root, _handAttachRightBoneName);

            if (_handAttachL == null)
                Debug.LogWarning($"[PlayerWeaponVisual] 找不到骨頭 {_handAttachLeftBoneName}", this);
            if (_handAttachR == null)
                Debug.LogWarning($"[PlayerWeaponVisual] 找不到骨頭 {_handAttachRightBoneName}", this);
        }

        private void Start()
        {
            if (_player == null) return;
            _player.OnWeaponSwitched += Equip;
            Equip(_player.Combat != null ? _player.Combat.CurrentWeaponIndex : 1);
        }

        private void OnDestroy()
        {
            if (_player != null)
                _player.OnWeaponSwitched -= Equip;
        }

        private void Equip(int weaponIndex)
        {
            ClearSpawned();

            switch (weaponIndex)
            {
                case 1:
                    SpawnOn(_handAttachR, _pencilPrefab, _pencilRotationOffset);
                    break;
                case 2:
                    SpawnOn(_handAttachR, _brushPrefab, _brushRotationOffset);
                    break;
                case 3:
                    // 左右手骨骼的 rest pose 不是完美鏡像（實測驗證過），兩手要各自獨立的軸向校正，不能共用同一個offset。
                    SpawnOn(_handAttachR, _paintKnifePrefab, _paintKnifeRotationOffsetRight);
                    SpawnOn(_handAttachL, _paintKnifePrefab, _paintKnifeRotationOffsetLeft);
                    break;
                case 4:
                    SpawnOn(_handAttachL, _palettePrefab, _paletteRotationOffset);
                    break;
            }
        }

        private void SpawnOn(Transform bone, GameObject prefab, Vector3 rotationOffset)
        {
            if (bone == null || prefab == null) return;

            var instance = Instantiate(prefab, bone);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.Euler(rotationOffset);

            // rig_deform 骨架本身在匯入時就帶了 100x 的縮放（Blender 匯出的常見換算），
            // hand_attach 骨頭的世界縮放因此被放大了 100 倍。武器模型是照真實世界比例做的，
            // 直接繼承這個縮放會被放大 100 倍，所以這裡用骨頭的 lossyScale 反向抵銷，
            // 讓武器維持原本作者設定好的實際大小。
            var parentLossyScale = bone.lossyScale;
            instance.transform.localScale = new Vector3(
                1f / parentLossyScale.x,
                1f / parentLossyScale.y,
                1f / parentLossyScale.z);

            _spawned.Add(instance);
        }

        private void ClearSpawned()
        {
            for (int i = 0; i < _spawned.Count; i++)
            {
                if (_spawned[i] != null) Destroy(_spawned[i]);
            }
            _spawned.Clear();
        }

        private static Transform FindDescendant(Transform root, string name)
        {
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                var found = FindDescendant(root.GetChild(i), name);
                if (found != null) return found;
            }
            return null;
        }
    }
}
