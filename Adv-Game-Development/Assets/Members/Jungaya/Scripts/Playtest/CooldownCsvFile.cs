using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Toufuku.Playtest
{
    /*
        T0-CD の記録ファイルを読み書きするクラス（#50）

        保存する場所は #49 と同じ AbTestCsvFile.FolderName（エディタはプロジェクトのすぐ下、ビルドは persistentDataPath）

        ファイル:
        ・T0-CD-1_日付.csv: 参加者 × 条件の集計（1行ずつ足していく）。ゲームが自動で書く。動画側の列はあとで人がうめる
        ・T0-CD-1_日付_events.csv: 発射1回を1行にしたそのままのログ（動画と照らし合わせる用）。ゲームが自動で書く。さわらない

        集計の CSV は条件が1つ終わるたびに足していくので、とちゅうで落ちてもそこまでは残る。UTF-8 BOM 付き
        動画側の列を Excel でうめたあとに足すと行がまざるので、足し終わってからうめること
        （終わってからうめなおしたいときは WriteAll がぜんぶ書きなおす）
    */
    public static class CooldownCsvFile
    {
        public static string DefaultFolder => AbTestCsvFile.DefaultFolder;

        public static string PathOf(string folder, string testId, string date) =>
            Path.Combine(FolderOf(folder), $"{Sanitize(testId, "T0-CD")}_{Sanitize(date, "no-date")}.csv");

        // そのままのログ（発射1回が1行）のパス
        public static string EventPathOf(string folder, string testId, string date) =>
            Path.Combine(FolderOf(folder), $"{Sanitize(testId, "T0-CD")}_{Sanitize(date, "no-date")}_events.csv");

        // 条件1つぶんを足す。ファイルがなければ見出しの行から作る
        public static bool Append(string path, CooldownConditionRecord record, out string error)
        {
            error = null;
            if (record == null) { error = "記録がありません"; return false; }
            return AppendLine(path, CooldownCsv.Header, CooldownCsv.RowOf(record), out error);
        }

        // そのままのログを1行足す
        public static bool AppendEvent(string path, string line, out string error) =>
            AppendLine(path, CooldownEventLog.Header, line, out error);

        // ぜんぶ書きなおす（動画側の列をあとから入れなおしたとき）
        public static bool WriteAll(string path, IReadOnlyList<CooldownConditionRecord> records, out string error)
        {
            error = null;
            try
            {
                EnsureFolder(path);
                var sb = new StringBuilder();
                sb.Append(CooldownCsv.Header).Append(Environment.NewLine);
                if (records != null)
                {
                    for (int i = 0; i < records.Count; i++)
                    {
                        if (records[i] == null) continue;
                        sb.Append(CooldownCsv.RowOf(records[i])).Append(Environment.NewLine);
                    }
                }
                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
                return true;
            }
            catch (Exception e)
            {
                error = e.Message;
                Debug.LogError($"[T0-CD] 記録を書き直せませんでした: {path}\n{e}");
                return false;
            }
        }

        // もう書いてある行を読みもどす（同じ日の続きや、動画側をうめたあとの集計用）
        public static List<CooldownConditionRecord> Load(string path)
        {
            var records = new List<CooldownConditionRecord>();
            try
            {
                if (!File.Exists(path)) return records;
                foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
                {
                    if (CooldownCsv.TryParseRow(line.TrimStart('﻿'), out CooldownConditionRecord r))
                        records.Add(r);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[T0-CD] 記録を読み込めませんでした: {path}\n{e.Message}");
            }
            return records;
        }

        static bool AppendLine(string path, string header, string line, out string error)
        {
            error = null;
            try
            {
                EnsureFolder(path);
                bool exists = File.Exists(path) && new FileInfo(path).Length > 0;
                string text = (exists ? "" : header + Environment.NewLine) + line + Environment.NewLine;
                File.AppendAllText(path, text, new UTF8Encoding(true));
                return true;
            }
            catch (Exception e)
            {
                error = e.Message;
                Debug.LogError($"[T0-CD] 記録を保存できませんでした: {path}\n{e}");
                return false;
            }
        }

        static void EnsureFolder(string path)
        {
            string folder = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
        }

        static string FolderOf(string folder) => string.IsNullOrEmpty(folder) ? DefaultFolder : folder;

        static string Sanitize(string text, string fallback)
        {
            if (string.IsNullOrWhiteSpace(text)) return fallback;
            var sb = new StringBuilder(text.Length);
            foreach (char c in text)
                sb.Append(Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 || c == ' ' ? '_' : c);
            return sb.ToString();
        }
    }
}
