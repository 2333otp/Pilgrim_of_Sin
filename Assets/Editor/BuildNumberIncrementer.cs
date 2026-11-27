using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

/// <summary>
/// 每次 Build 前自動更新 Build 號（格式 YYMMDD_XX）。
/// 同一天再 build 就 XX + 1，換日就從 01 開始。
/// 結果寫進 Assets/Resources/BuildInfo.txt，由主畫面的 VersionInfoText 讀取顯示。
/// </summary>
public class BuildNumberIncrementer : IPreprocessBuildWithReport
{
    public const string AssetPath = "Assets/Resources/BuildInfo.txt";

    public int callbackOrder => 0;

    public void OnPreprocessBuild(BuildReport report)
    {
        string today = DateTime.Now.ToString("yyMMdd");
        int count = 1;

        if (File.Exists(AssetPath))
        {
            string[] parts = File.ReadAllText(AssetPath).Trim().Split('_');
            if (parts.Length == 2 && parts[0] == today && int.TryParse(parts[1], out int last))
                count = last + 1;
        }

        string buildNumber = $"{today}_{count:00}";
        File.WriteAllText(AssetPath, buildNumber);
        AssetDatabase.ImportAsset(AssetPath, ImportAssetOptions.ForceSynchronousImport);
        UnityEngine.Debug.Log($"[BuildNumber] {buildNumber}");
    }
}
