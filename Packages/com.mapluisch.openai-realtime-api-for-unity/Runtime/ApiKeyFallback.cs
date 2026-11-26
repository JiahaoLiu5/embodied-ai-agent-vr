using System;
using System.IO;
using UnityEngine;

/// <summary>
/// Keeps keys out of scenes: an empty key field falls back to the OPENAI_API_KEY environment variable, then to a file
/// named openai_api_key.txt (project folder in the Editor, persistentDataPath on a device).
/// </summary>
public static class ApiKeyFallback
{
    public static string Resolve(string key)
    {
        if (!string.IsNullOrWhiteSpace(key) && key != "YOUR_API_KEY") return key.Trim();

        string variable = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (!string.IsNullOrWhiteSpace(variable)) return variable.Trim();

        foreach (string folder in new[] { Application.persistentDataPath, Application.isEditor ? "." : null })
        {
            if (folder == null) continue;
            string path = Path.Combine(folder, "openai_api_key.txt");
            if (File.Exists(path)) return File.ReadAllText(path).Trim();
        }

        Debug.LogError("No OpenAI API key found. Set OPENAI_API_KEY or create openai_api_key.txt.");
        return "";
    }
}