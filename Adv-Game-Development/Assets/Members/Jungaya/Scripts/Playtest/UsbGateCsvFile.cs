using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Toufuku.Playtest
{
    /// <summary>
    /// T6-USB の記録ファイル — Issue #52
    ///
    /// 保存先は #49 / #50 / #53 と同じ <see cref="AbTestCsvFile.DefaultFolder"/>（Editor はプロジェクト直下、ビルドは persistentDataPath）。
    /// ・<c>{テストID}_{日付}_events.csv</c>: 1 イベント 1 行。区間が終わるたびに追記するので、途中で落ちてもそれまでの区間は残る。
    /// ・<c>{テストID}_{日付}_summary.csv</c>: 完了条件ごとの判定。区間が終わるたびに書き直す（19章のテスト記録へ貼る）。
    /// どちらも UTF-8 BOM 付き。
    /// </summary>
    public static class UsbGateCsvFile
    {
        public static string DefaultFolder => AbTestCsvFile.DefaultFolder;

        public static string EventsPathOf(string folder, string testId, string date) =>
            Path.Combine(FolderOf(folder), $"{Sanitize(testId, UsbGatePlan.DefaultTestId)}_{Sanitize(date, "no-date")}_events.csv");

        public static string SummaryPathOf(string folder, string testId, string date) =>
            Path.Combine(FolderOf(folder), $"{Sanitize(testId, UsbGatePlan.DefaultTestId)}_{Sanitize(date, "no-date")}_summary.csv");

        public static bool Append(string path, IReadOnlyList<UsbGateEvent> events, out string error)
        {
            error = null;
            if (events == null || events.Count == 0) return true;
            try
            {
                EnsureFolder(path);
                bool exists = File.Exists(path) && new FileInfo(path).Length > 0;
                var sb = new StringBuilder(events.Count * 120);
                if (!exists) sb.Append(UsbGateCsv.Header).Append(Environment.NewLine);
                foreach (UsbGateEvent e in events)
                {
                    if (e != null) sb.Append(UsbGateCsv.RowOf(e)).Append(Environment.NewLine);
                }
                File.AppendAllText(path, sb.ToString(), new UTF8Encoding(true));
                return true;
            }
            catch (Exception e)
            {
                error = e.Message;
                Debug.LogError($"[T6-USB] 記録を保存できませんでした: {path}\n{e}");
                return false;
            }
        }

        public static List<UsbGateEvent> Load(string path)
        {
            var events = new List<UsbGateEvent>();
            try
            {
                if (!File.Exists(path)) return events;
                foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
                {
                    if (UsbGateCsv.TryParseRow(line.TrimStart('﻿'), out UsbGateEvent e)) events.Add(e);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[T6-USB] 記録を読み込めませんでした: {path}\n{e.Message}");
            }
            return events;
        }

        /// <summary>完了条件ごとの判定を書き直す。</summary>
        public static bool WriteSummary(string path, string testId, string date, UsbGateSummary summary, out string error)
        {
            error = null;
            if (summary == null) return true;
            try
            {
                EnsureFolder(path);
                var sb = new StringBuilder();
                sb.Append("テストID,日付,項目,実測,基準,判定,不合格時の処置").Append(Environment.NewLine);
                foreach (UsbGateCriterion c in summary.Criteria)
                {
                    sb.Append(AbTestCsv.Escape(testId)).Append(',')
                      .Append(AbTestCsv.Escape(date)).Append(',')
                      .Append(AbTestCsv.Escape(c.Criterion.Name)).Append(',')
                      .Append(AbTestCsv.Escape(c.Criterion.Actual)).Append(',')
                      .Append(AbTestCsv.Escape(c.Criterion.Required)).Append(',')
                      .Append(c.Criterion.Passed ? "合格" : "不合格").Append(',')
                      .Append(AbTestCsv.Escape(c.Criterion.Passed ? "" : c.Remedy))
                      .Append(Environment.NewLine);
                }
                sb.Append(AbTestCsv.Escape(testId)).Append(',').Append(AbTestCsv.Escape(date))
                  .Append(",総合,,,").Append(summary.Passed ? "合格" : "不合格").Append(",")
                  .Append(Environment.NewLine);
                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
                return true;
            }
            catch (Exception e)
            {
                error = e.Message;
                Debug.LogError($"[T6-USB] 判定を書き出せませんでした: {path}\n{e}");
                return false;
            }
        }

        static string FolderOf(string folder) => string.IsNullOrEmpty(folder) ? DefaultFolder : folder;

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
