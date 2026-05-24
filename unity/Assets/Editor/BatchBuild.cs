using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

public class BatchBuild
{
    static void SetupAndBuild()
    {
        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;

        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        SceneSetup.SetupScene();

        string scenePath = "Assets/Scenes/GPSScene.unity";
        if (!AssetDatabase.IsValidFolder("Assets/Scenes"))
            AssetDatabase.CreateFolder("Assets", "Scenes");
        EditorSceneManager.SaveScene(scene, scenePath);

        Debug.Log("Scene setup and saved to " + scenePath);

        string buildPath = "../webgl-build";
        var options = new BuildPlayerOptions
        {
            scenes = new[] { scenePath },
            locationPathName = buildPath,
            target = BuildTarget.WebGL,
            options = BuildOptions.None
        };

        var result = BuildPipeline.BuildPlayer(options);
        if (result.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded)
            Debug.Log("WebGL build succeeded: " + buildPath);
        else
            Debug.LogError("WebGL build failed: " + result.summary.result);
    }
}
