using TMPro;
using UnityEngine;

/// <summary>
/// 主畫面右下角版本資訊。
/// 第一行：Project Settings > Player > Version（Application.version）。
/// 第二行：Build 號，讀 Resources/BuildInfo.txt（每次 Build 前由 BuildNumberIncrementer 自動更新）。
/// </summary>
[RequireComponent(typeof(TMP_Text))]
public class VersionInfoText : MonoBehaviour
{
    [Tooltip("最下方的版權宣告")]
    [SerializeField] private string copyrightLine = "© 2026 Team huluhuluxili. All Rights Reserved.";

    private void Awake()
    {
        Refresh();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        Refresh();
    }
#endif

    public void Refresh()
    {
        var text = GetComponent<TMP_Text>();
        if (text == null) return;

        var buildInfo = Resources.Load<TextAsset>("BuildInfo");
        string buildNumber = buildInfo != null ? buildInfo.text.Trim() : "-";

        text.text = $"{Application.version}\nBuild {buildNumber}\n{copyrightLine}";
    }
}
