using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

public sealed class AzureSpeechBuildCredentialsExporter :
    IPreprocessBuildWithReport
{
    private const string KeyPreference = "VoiceChess.AzureSpeech.Key";
    private const string RegionPreference = "VoiceChess.AzureSpeech.Region";
    private const string CredentialsFileName = "azure-speech.json";

    [Serializable]
    private sealed class BuildSpeechCredentials
    {
        public string key = string.Empty;
        public string region = string.Empty;
    }

    public int callbackOrder => 0;

    public void OnPreprocessBuild(BuildReport report)
    {
        if (report.summary.platform != BuildTarget.StandaloneWindows64)
        {
            throw new BuildFailedException(
                "The bundled Azure Speech SDK currently supports only Windows x64 builds.");
        }

        GetSavedCredentials(out string key, out string region);

        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(region))
        {
            throw new BuildFailedException(
                "Azure Speech Key/Region is missing. Open Voice Chess > " +
                "Azure Speech Settings and save both values before building.");
        }

        string streamingAssetsDirectory = Path.Combine(
            Application.dataPath,
            "StreamingAssets");
        string credentialsPath = Path.Combine(
            streamingAssetsDirectory,
            CredentialsFileName);

        Directory.CreateDirectory(streamingAssetsDirectory);

        BuildSpeechCredentials credentials = new()
        {
            key = key,
            region = region
        };
        string json = JsonUtility.ToJson(credentials);
        File.WriteAllText(credentialsPath, json, new UTF8Encoding(false));

        AssetDatabase.ImportAsset(
            $"Assets/StreamingAssets/{CredentialsFileName}",
            ImportAssetOptions.ForceSynchronousImport |
            ImportAssetOptions.ForceUpdate);

        // Debug.Log(
        //     "Azure Speech credentials are ready to be included in StreamingAssets.");
    }

    private static void GetSavedCredentials(out string key, out string region)
    {
        key = EditorPrefs.GetString(KeyPreference, string.Empty).Trim();
        region = EditorPrefs.GetString(RegionPreference, string.Empty).Trim();
    }
}
