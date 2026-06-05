using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using System;
using System.IO;
using UnityEngine;

public static class CliBuildScript
{
    static string LogPath => Path.Combine(Application.dataPath, "../Build/cli_build.log");
    static string TestResultPath => Path.Combine(Application.dataPath, "../Build/test_results.xml");

    public static void BuildWindows()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(LogPath));
        var options = new BuildPlayerOptions
        {
            scenes = GetEnabledScenes(),
            locationPathName = "Build/PilgrimOfSin.exe",
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None
        };

        var report = BuildPipeline.BuildPlayer(options);
        var summary = report.summary;

        string result = summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded
            ? $"[BUILD SUCCESS] Size={summary.totalSize / 1024}KB Time={summary.totalTime.TotalSeconds:F1}s"
            : $"[BUILD FAILED] Errors={summary.totalErrors} Warnings={summary.totalWarnings}";

        File.WriteAllText(LogPath, result + "\n" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        Console.WriteLine(result);

        EditorApplication.Exit(summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded ? 0 : 1);
    }

    public static void RunEditModeTests()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(TestResultPath));
        var api = ScriptableObject.CreateInstance<TestRunnerApi>();
        var filter = new Filter { testMode = TestMode.EditMode };
        api.Execute(new ExecutionSettings(filter));
        EditorApplication.Exit(0);
    }

    static string[] GetEnabledScenes()
    {
        var scenes = new System.Collections.Generic.List<string>();
        foreach (var scene in EditorBuildSettings.scenes)
            if (scene.enabled) scenes.Add(scene.path);
        return scenes.ToArray();
    }
}
