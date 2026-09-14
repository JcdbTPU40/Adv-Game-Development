using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Toufuku.Playtest
{
    /// <summary>
    /// T0-CD の記録ファイル — Issue #50
    ///
    /// 保存先は #49 と同じ <see cref="AbTestCsvFile.FolderName"/>（Editor はプロジェクト直下、ビルドは persistentDataPath）。
    ///
    /// | ファイル | 中身 | 誰が書くか |
    /// |---|---|---|
    /// | <c>T0-CD-1_日付.csv</c> | 参加者 × 条件の集計（1 行ずつ追記） | ゲームが自動。動画側の列はあとから人が埋める |
    /// | <c>T0-CD-1_日付_events.csv</c> | 発射 1 件 1 行の生ログ（動画との突き合わせ用） | ゲームが自動。編集しない |
    ///
    /// 集計 CSV は 1 条件終わるたびに追記するので、途中で落ちてもそこまでは残る。UTF-8 BOM 付き。
    /// 動画側の列を Excel で埋めたあとに追記すると行が混ざるので、<b>追記が終わってから埋める</b>
    /// （終わったあとで埋め直したいときは <see cref="WriteAll"/> が全文を書き直す）。
    /// </summary>
    public static class CooldownCsvFile
    {
        public static string DefaultFolder => AbTestCsvFile.DefaultFolder;

        public static string PathOf(string folder, string testId, string date) =>
            Path.Combine(FolderOf(folder), $"{Sanitize(testId, "T0-CD")}_{Sanitize(date, "no-date")}.csv");

        /// <summary>生ログ（発射 1 件 1 行）のパス。</summary>
        public static string EventPathOf(string folder, string testId, string date) =>
            Path.Combine(FolderOf(folder), $"{Sanitize(testId, "T0-CD")}_{Sanitize(date, "no-date")}_events.csv");

        /// <summary>1 条件分を追記する。ファイルが無ければ見出し行から作る。</summary>
        public static bool Append(string path, CooldownConditionRecord record, out string error)
        {
            error = null;
            if (record == null) { error = "記録がありません"; return false; }
            return AppendLine(path, CooldownCsv.Header, CooldownCsv.RowOf(record), out error);
        }

        /// <summary>生ログを 1 行追記する。</summary>
        public static bool AppendEvent(string path, string line, out string error) =>
            AppendLine(path, CooldownEventLog.Header, line, out error);

        /// <summary>全文を書き直す（動画側の列をあとから入れ直したとき）。</summary>
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

        /// <summary>既に書かれている行を読み戻す（同じ日の続き・動画側を埋めたあとの集計）。</summary>
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
