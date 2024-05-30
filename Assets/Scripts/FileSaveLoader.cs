using SFB;
using System.IO;
using UnityEngine;

public static class FileSaveLoader
{
    private static string ParamsFolderPath => Application.streamingAssetsPath + "/params";
    private static string DefaultParamsFilePath => ParamsFolderPath + "/default.json";

    public static void SaveDefaultParams(IParams @params)
    {
        Directory.CreateDirectory(ParamsFolderPath);
        using var fs = new StreamWriter(DefaultParamsFilePath, false);
        var json = JsonUtility.ToJson(@params, true);
        fs.Write(json);
    }

    public static void LoadDefaultParams(IParams @params)
    {
        if (!File.Exists(DefaultParamsFilePath))
        {
            Debug.Log("JSON File not Found");
            SaveDefaultParams(@params);
            return;
        }

        using var fs = new StreamReader(DefaultParamsFilePath);
        var json = fs.ReadToEnd();
        JsonUtility.FromJsonOverwrite(json, @params);
    }

    public static void SaveParamsWithFileBrowser(IParams @params)
    {
        var path = StandaloneFileBrowser.SaveFilePanel("Save File", ParamsFolderPath, "", "json");

        if (path == "")
        {
            Debug.LogWarning("Invalid File Name");
            return;
        }

        using var fs = new StreamWriter(path, false);
        var json = JsonUtility.ToJson(@params, true);
        fs.Write(json);
    }

    public static void LoadParamsWithFileBrowser(IParams @params)
    {
        var paths = StandaloneFileBrowser.OpenFilePanel("Load File", ParamsFolderPath, "json", false);
        if (paths.Length == 0) return;

        if (!File.Exists(paths[0]))
        {
            Debug.LogWarning("JSON File not Found");
            return;
        }

        using var fs = new StreamReader(paths[0]);
        var json = fs.ReadToEnd();
        JsonUtility.FromJsonOverwrite(json, @params);
    }
}