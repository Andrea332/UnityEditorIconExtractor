using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEditor;


public static class IconExtractor
{
    [MenuItem("Unity Editor Icons/Export All %e", priority = -1001)]
    private static void ExportIcons()
    {
        EditorUtility.DisplayProgressBar("Export Icons", "Exporting...", 0.0f);
        try
        {
            var editorAssetBundle = GetEditorAssetBundle();
            var iconsPath = GetIconsPath();
            var count = 0;
            foreach (var assetName in EnumerateIcons(editorAssetBundle, iconsPath))
            {
                var icon = editorAssetBundle.LoadAsset<Texture2D>(assetName);
                if (icon == null)
                    continue;
                
                var readableTexture = CreateReadableTexture(icon);

                var folderPath = Path.GetDirectoryName(Path.Combine("icons/original/", assetName.Substring(iconsPath.Length)));
                if (Directory.Exists(folderPath) == false)
                    Directory.CreateDirectory(folderPath);

                var iconPath = Path.Combine(folderPath, icon.name + ".png");
                File.WriteAllBytes(iconPath, readableTexture.EncodeToPNG());

                UnityEngine.Object.DestroyImmediate(readableTexture);

                count++;
            }

            Debug.Log($"{count} icons has been exported!");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    [MenuItem("Unity Editor Icons/Generate README.md %g", priority = -1000)]
    private static void GenerateREADME()
    {
        var guidMaterial = new Material(Shader.Find("Unlit/Texture"));
        var guidMaterialId = "Assets/Editor/GuidMaterial.mat";
        AssetDatabase.CreateAsset(guidMaterial, guidMaterialId);

        EditorUtility.DisplayProgressBar("Generate README.md", "Generating...", 0.0f);
        try
        {
            var editorAssetBundle = GetEditorAssetBundle();
            var iconsPath = GetIconsPath();
            var readmeContents = new StringBuilder();

            readmeContents.AppendLine($"Unity Editor Built-in Icons");
            readmeContents.AppendLine($"==============================");
            readmeContents.AppendLine($"Unity version: {Application.unityVersion}");
            readmeContents.AppendLine($"Icons what can load using `EditorGUIUtility.IconContent`");
            readmeContents.AppendLine();
            readmeContents.AppendLine($"File ID");
            readmeContents.AppendLine($"-------------");
            readmeContents.AppendLine($"You can change script icon by file id");
            readmeContents.AppendLine($"1. Open `*.cs.meta` in Text Editor");
            readmeContents.AppendLine($"2. Modify line `icon: {{instanceID: 0}}` to `icon: {{fileID: <FILE ID>, guid: 0000000000000000d000000000000000, type: 0}}`");
            readmeContents.AppendLine($"3. Save and focus Unity Editor");
            readmeContents.AppendLine();
            readmeContents.AppendLine($"| Icon | Name | File ID |");
            readmeContents.AppendLine($"|------|------|---------|");

            var assetNames = EnumerateIcons(editorAssetBundle, iconsPath).ToArray();
            for (var i = 0; i < assetNames.Length; i++)
            {
                var assetName = assetNames[i];
                var icon = editorAssetBundle.LoadAsset<Texture2D>(assetName);
                if (icon == null)
                    continue;

                EditorUtility.DisplayProgressBar("Generate README.md", $"Generating... ({i + 1}/{assetNames.Length})", (float)i / assetNames.Length);
                
                var readableTexture = CreateReadableTexture(icon);

                var folderPath = Path.GetDirectoryName(Path.Combine("icons/small/", assetName.Substring(iconsPath.Length)));
                if (Directory.Exists(folderPath) == false)
                    Directory.CreateDirectory(folderPath);

                var iconPath = Path.Combine(folderPath, icon.name + ".png");
                File.WriteAllBytes(iconPath, readableTexture.EncodeToPNG());
                
                UnityEngine.Object.DestroyImmediate(readableTexture);
                
                guidMaterial.mainTexture = icon;
                EditorUtility.SetDirty(guidMaterial);
                AssetDatabase.SaveAssets();
                var fileId = GetFileId(guidMaterialId);

                var escapedUrl = iconPath.Replace(" ", "%20").Replace('\\', '/');
                readmeContents.AppendLine($"| ![]({escapedUrl}) | `{icon.name}` | `{fileId}` |");
            }

            File.WriteAllText("README.md", readmeContents.ToString());

            Debug.Log($"'README.md' has been generated.");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            AssetDatabase.DeleteAsset(guidMaterialId);
        }
    }
    
    private static Texture2D CreateReadableTexture(Texture2D source)
    {
        RenderTexture renderTex = RenderTexture.GetTemporary(
            source.width,
            source.height,
            0,
            RenderTextureFormat.ARGB32,
            RenderTextureReadWrite.Default);

        Graphics.Blit(source, renderTex);
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = renderTex;
        
        Texture2D readableText = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
        readableText.ReadPixels(new Rect(0, 0, renderTex.width, renderTex.height), 0, 0);
        readableText.Apply();
        
        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(renderTex);
        
        return readableText;
    }

    private static IEnumerable<string> EnumerateIcons(AssetBundle editorAssetBundle, string iconsPath)
    {
        foreach (var assetName in editorAssetBundle.GetAllAssetNames())
        {
            if (assetName.StartsWith(iconsPath, StringComparison.OrdinalIgnoreCase) == false)
                continue;
            if (assetName.EndsWith(".png", StringComparison.OrdinalIgnoreCase) == false &&
                assetName.EndsWith(".asset", StringComparison.OrdinalIgnoreCase) == false)
                continue;

            yield return assetName;
        }
    }

    private static string GetFileId(string proxyAssetPath)
    {
        var serializedAsset = File.ReadAllText(proxyAssetPath);
        var index = serializedAsset.IndexOf("_MainTex:", StringComparison.Ordinal);
        if (index == -1)
            return string.Empty;

        const string FileId = "fileID:";
        var startIndex = serializedAsset.IndexOf(FileId, index) + FileId.Length;
        var endIndex = serializedAsset.IndexOf(',', startIndex);
        return serializedAsset.Substring(startIndex, endIndex - startIndex).Trim();
    }

    private static AssetBundle GetEditorAssetBundle()
    {
        var editorGUIUtility = typeof(EditorGUIUtility);
        var getEditorAssetBundle = editorGUIUtility.GetMethod(
            "GetEditorAssetBundle",
            BindingFlags.NonPublic | BindingFlags.Static);

        return (AssetBundle)getEditorAssetBundle.Invoke(null, new object[] { });
    }

    private static string GetIconsPath()
    {
        return UnityEditor.Experimental.EditorResources.iconsPath;
    }
}
