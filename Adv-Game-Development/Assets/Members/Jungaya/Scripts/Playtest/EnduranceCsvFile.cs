using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Toufuku.Playtest
{
    /// <summary>
    /// T0-3M の記録ファイル — Issue #53
    ///
    /// 保存先は #49 / #50 と同じ <see cref="AbTestCsvFile.FolderName"/>（Editor はプロジェクト直下、ビルドは persistentDataPath）。
    /// ファイル名は <c>{テストID}_{日付}.csv</c>。1 人終わるたびに 1 行追記するので、途中で落ちてもそこまでは残る。UTF-8 BOM 付き。
    /// 聞き取りの列を Excel で埋めるのは<b>その日の追記が終わってから</b>（開いたままだと書き込めない）。
    /// </summary>
    public static class EnduranceCsvFile
    {
        public static string DefaultFolder => AbTestCsvFile.DefaultFolder;

        public static string PathOf(string folder, string testId, string date) =>
            Path.Combine(string.IsNullOrEmpty(folder) ? DefaultFolder : folder,
                $"{Sanitize(testId, "T0-3M")}_{Sanitize(date, "no-date")}.csv");

        /// <summary>1 人分を追記する。ファイルが無ければ見出し行から作る。</summary>
        public static bool Append(string path, EnduranceRecord record, out string error)
        {
            error = null;
            if (record == null) { error = "記録がありません"; return false; }
            try
            {
                EnsureFolder(path);
                bool exists = File.Exists(path) && new FileInfo(path).Length > 0;
                string text = (exists ? "" : EnduranceCsv.Header + Environment.NewLine) +
                              EnduranceCsv.RowOf(record) + Environment.NewLine;
                File.AppendAllText(path, text, new UTF8Encoding(true));
                return true;
            }
            catch (Exception e)
            {
                error = e.Message;
                Debug.LogError($"[T0-3M] 記録を保存できませんでした: {path}\n{e}");
                return false;
            }
        }

        /// <summary>全文を書き直す（聞き取りの列をあとから入れ直したとき）。</summary>
        public static bool WriteAll(string path, IReadOnlyList<EnduranceRecord> records, out string error)
        {
            error = null;
            try
            {
                EnsureFolder(path);
                var sb = new StringBuilder();
                sb.Append(EnduranceCsv.Header).Append(Environment.NewLine);
                if (records != null)
                {
                    foreach (EnduranceRecord r in records)
                    {
                        if (r != null) sb.Append(EnduranceCsv.RowOf(r)).Append(Environment.NewLine);
                    }
                }
                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
                return true;
            }
            catch (Exception e)
            {
                error = e.Message;
                Debug.LogError($"[T0-3M] 記録を書き直せませんでした: {path}\n{e}");
                return false;
            }
        }

        public static List<EnduranceRecord> Load(string path)
        {
            var records = new List<EnduranceRecord>();
            try
            {
                if (!File.Exists(path)) return records;
                foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
                {
                    if (EnduranceCsv.TryParseRow(line.TrimStart('﻿'), out EnduranceRecord r)) records.Add(r);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[T0-3M] 記録を読み込めませんでした: {path}\n{e.Message}");
            }
            return records;
        }

        static void EnsureFolder(string path)
        {
            string folder = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
        }

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
