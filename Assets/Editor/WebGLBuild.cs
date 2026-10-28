using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

// 命令行打 WebGL 包，产物在 Builds/WebGL/，用于发布到 GitHub Pages / itch.io。
//   Unity -batchmode -quit -projectPath . -executeMethod WebGLBuild.Build
// GitHub Pages 不会给 .br/.gz 加 Content-Encoding 响应头，所以这里关掉压缩，
// 否则浏览器拿到的是压缩字节、加载时直接报错白屏。
public static class WebGLBuild
{
    private const string OutputPath = "Builds/WebGL";

    [MenuItem("Tools/Build WebGL")]
    public static void Build()
    {
        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
        PlayerSettings.WebGL.decompressionFallback = false;
        PlayerSettings.WebGL.nameFilesAsHashes = false;
        // 网页标题用产品名，但 Windows 版的 PlayerPrefs 路径也挂在产品名上，
        // 所以只在打包期间临时替换，打完还原，免得已发布版本的存档对不上。
        string originalProductName = PlayerSettings.productName;
        PlayerSettings.productName = "Lottery Mania";

        string[] scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
        if (scenes.Length == 0) scenes = new[] { "Assets/Scenes/SampleScene.unity" };

        BuildReport report;
        try
        {
            report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = OutputPath,
                target = BuildTarget.WebGL,
                options = BuildOptions.None,
            });
        }
        finally
        {
            PlayerSettings.productName = originalProductName;
            AssetDatabase.SaveAssets();
        }

        Debug.Log($"[WebGLBuild] {report.summary.result}, {report.summary.totalSize / (1024 * 1024)} MB, {report.summary.totalTime}");
        if (Application.isBatchMode)
            EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
    }
}
