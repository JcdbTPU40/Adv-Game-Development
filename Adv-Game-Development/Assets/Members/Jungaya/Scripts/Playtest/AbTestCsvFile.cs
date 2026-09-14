using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Toufuku.Playtest
{
    /// <summary>
    /// 記録 CSV の読み書き — Issue #49
    ///
    /// 保存先は Editor ならプロジェクト直下の PlaytestLogs/、ビルドなら persistentDataPath/PlaytestLogs/。
    /// 1 人分を 1 行ずつ追記するので、途中で落ちてもそこまでは残る。Excel 用に UTF-8 BOM を付ける。
    /// </summary>
    public static class AbTestCsvFile
    {
        public const string FolderName = "PlaytestLogs";

        public static string DefaultFolder
        {
            get
            {
                string root = Application.isEditor
                    ? Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath
                    : Application.persistentDataPath;
                return Path.Combine(root, FolderName);
            }
        }

        /// <summary>テストID と日付からファイル名を作る。</summary>
        public static string PathOf(string folder, string testId, string date)
        {
            string name = $"{Sanitize(testId, "T0-AB")}_{Sanitize(date, "no-date")}.csv";
            return Path.Combine(string.IsNullOrEmpty(folder) ? DefaultFolder : folder, name);
        }

        /// <summary>1 人分を追記する。ファイルが無ければ見出し行から作る。</summary>
        public static bool Append(string path, AbParticipantRecord record, out string error)
        {
            error = null;
            if (record == null) { error = "記録がありません"; return false; }

            try
            {
                string folder = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);

                var encoding = new UTF8Encoding(true);
                bool exists = File.Exists(path) && new FileInfo(path).Length > 0;
                string text = (exists ? "" : AbTestCsv.Header + Environment.NewLine)
                              + AbTestCsv.RowOf(record) + Environment.NewLine;
                File.AppendAllText(path, text, encoding);
                return true;
            }
            catch (Exception e)
            {
                error = e.Message;
                Debug.LogError($"[T0-A/B] 記録を保存できませんでした: {path}\n{e}");
                return false;
            }
        }

        /// <summary>既に書かれている行を読み戻す（同じ日の続きから再開したときの集計用）。</summary>
        public static List<AbParticipantRecord> Load(string path)
        {
            var records = new List<AbParticipantRecord>();
            try
            {
                if (!File.Exists(path)) return records;
                foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
                {
                    if (AbTestCsv.TryParseRow(line.TrimStart('﻿'), out AbParticipantRecord r))
                        records.Add(r);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[T0-A/B] 記録を読み込めませんでした: {path}\n{e.Message}");
            }
            return records;
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
