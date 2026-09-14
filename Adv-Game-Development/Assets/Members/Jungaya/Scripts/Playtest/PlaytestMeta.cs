using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Toufuku.Playtest
{
    public enum PlaytestSeedMode
    {
        /// <summary>毎プレイ同じシード（T2・T3・TA の比較用）。</summary>
        Fixed = 0,
        /// <summary>プレイごとに新しいシード（使ったシードはログに残る）。</summary>
        RandomEachPlay = 1
    }

    /// <summary>
    /// 計測プレイの識別情報 — Issue #63（仕様書 v8 17章: 数値変更にはテストID・日付・対象人数・責任者を必須）
    ///
    /// ・Inspector で設定し、Inspector の無いビルドでは起動引数で上書きする（<see cref="ApplyCommandLine"/>）。
    ///   例: <c>Game.exe -playtestTestId T3 -playtestSeed 42 -playtestBuild 17 -playtestParticipant P05 -playtestOperator Jungaya</c>
    /// ・テストID・日付・ビルド番号・シード値はファイル名（<see cref="FileStem"/>）とヘッダーの両方に入る。
    /// </summary>
    [Serializable]
    public class PlaytestMeta
    {
        public const string ArgPrefix = "-playtest";

        public string testId = "T0";
        public string buildNumber = "";
        public PlaytestSeedMode seedMode = PlaytestSeedMode.Fixed;
        public int seed = 12345;
        public string participantId = "";
        public string operatorName = "";
        public string hypothesis = "";
        public string note = "";
        public string outputFolder = "";

        public PlaytestMeta Clone()
        {
            return (PlaytestMeta)MemberwiseClone();
        }

        /// <summary>
        /// <c>-playtestXxx 値</c> の組を読んで上書きする。適用したキー（小文字）を返す。
        /// キー: TestId / Build / Seed（整数 or random）/ Participant / Operator / Hypothesis / Note / Out
        /// </summary>
        public List<string> ApplyCommandLine(IReadOnlyList<string> args)
        {
            var applied = new List<string>();
            if (args == null) return applied;

            for (int i = 0; i < args.Count; i++)
            {
                string arg = args[i];
                if (arg == null || !arg.StartsWith(ArgPrefix, StringComparison.OrdinalIgnoreCase)) continue;
                if (i + 1 >= args.Count) break;

                string key = arg.Substring(ArgPrefix.Length).ToLowerInvariant();
                string value = args[i + 1];
                bool ok = true;

                switch (key)
                {
                    case "testid": testId = value; break;
                    case "build": buildNumber = value; break;
                    case "participant": participantId = value; break;
                    case "operator": operatorName = value; break;
                    case "hypothesis": hypothesis = value; break;
                    case "note": note = value; break;
                    case "out": outputFolder = value; break;
                    case "seed":
                        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
                        {
                            seed = parsed;
                            seedMode = PlaytestSeedMode.Fixed;
                        }
                        else if (string.Equals(value, "random", StringComparison.OrdinalIgnoreCase))
                        {
                            seedMode = PlaytestSeedMode.RandomEachPlay;
                        }
                        else
                        {
                            ok = false;
                        }
                        break;
                    default:
                        ok = false;
                        break;
                }

                if (!ok) continue;
                applied.Add(key);
                i++;
            }
            return applied;
        }

        /// <summary>ファイル名の共通部分: <c>{テストID}_{yyyyMMdd-HHmmss}_b{ビルド番号}_s{シード}[_{参加者ID}]</c>。</summary>
        public string FileStem(DateTime startedAt, string build, int seedUsed)
        {
            var sb = new StringBuilder();
            sb.Append(SanitizeFilePart(testId));
            sb.Append('_').Append(startedAt.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
            sb.Append("_b").Append(SanitizeFilePart(build));
            sb.Append("_s").Append(seedUsed.ToString(CultureInfo.InvariantCulture));
            if (!string.IsNullOrWhiteSpace(participantId))
                sb.Append('_').Append(SanitizeFilePart(participantId));
            return sb.ToString();
        }

        /// <summary>ファイル名に使えない文字・空白を '-' に置き換える。空なら "NA"。</summary>
        public static string SanitizeFilePart(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "NA";

            var sb = new StringBuilder(value.Length);
            foreach (char c in value.Trim())
            {
                bool bad = c < 32 || c == '\\' || c == '/' || c == ':' || c == '*' || c == '?' || c == '"'
                           || c == '<' || c == '>' || c == '|' || c == '_' || char.IsWhiteSpace(c);
                sb.Append(bad ? '-' : c);
            }
            return sb.ToString();
        }
    }
}
