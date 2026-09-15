using System;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

/*
    展示ビルドの生存確認ファイル（#65 / 企画書 v8 17章 T7「故障しても60秒以内に戻せる」）

    アプリがフリーズすると中からは直せないので、外の見張り（Tools/exhibit_watchdog.ps1）が止めて起動しなおす
    そのために、メインスレッドが動いている間だけ 1秒ごとにファイルへ書く。書かれなくなったら固まっている

    ・起動の引数に -toufukuHeartbeat <ファイルのパス> があるときだけ動く（エディタや、見張りなしで起動したビルドでは何もしない）
    ・シーンに置かなくても起動したら自動で作られて、シーンをまたいでも残る
    ・ファイルの中身: 1行目 UTC の時刻、2行目 起動してからの秒、3行目 今のシーン名（見張りはファイルの更新時刻だけを見る）
*/
public sealed class ExhibitHeartbeat : MonoBehaviour
{
    public const string CommandLineKey = "-toufukuHeartbeat";
    public const double IntervalSeconds = 1.0;

    string _path;
    double _next;
    bool _warned;

    public static ExhibitHeartbeat Instance { get; private set; }
    public string Path => _path;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        string path = FindArgument(Environment.GetCommandLineArgs(), CommandLineKey);
        if (string.IsNullOrEmpty(path) || Instance != null) return;

        var go = new GameObject(nameof(ExhibitHeartbeat));
        DontDestroyOnLoad(go);
        Instance = go.AddComponent<ExhibitHeartbeat>();
        Instance._path = path;
        Debug.Log($"[Heartbeat] 生存確認ファイルを {IntervalSeconds:0}秒ごとに書きます: {path}");
        Instance.Write();
    }

    // 起動の引数から key のすぐうしろの値を探す（大文字小文字は区別しない）。なければ null
    public static string FindArgument(string[] args, string key)
    {
        if (args == null || string.IsNullOrEmpty(key)) return null;
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], key, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }
        return null;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Update()
    {
        if (Time.realtimeSinceStartupAsDouble >= _next) Write();
    }

    void Write()
    {
        double now = Time.realtimeSinceStartupAsDouble;
        _next = now + IntervalSeconds;
        try
        {
            File.WriteAllText(_path,
                DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture) + "\n" +
                now.ToString("0.000", CultureInfo.InvariantCulture) + "\n" +
                SceneManager.GetActiveScene().name + "\n");
        }
        catch (Exception e)
        {
            if (!_warned) Debug.LogWarning($"[Heartbeat] 生存確認ファイルに書けません（{_path}）: {e.Message}");
            _warned = true;
        }
    }
}
