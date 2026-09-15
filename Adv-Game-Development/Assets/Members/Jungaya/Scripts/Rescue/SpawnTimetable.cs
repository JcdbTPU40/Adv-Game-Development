using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Toufuku.Playtest;

namespace Toufuku.Rescue
{
    // 大きい負荷ウェーブで足す人数（T3 で +3 / +4 / +5 をくらべる。企画書 v8 17章、付録B WAVE.FINAL）
    public enum FinalWaveAdd
    {
        Plus3 = 3,
        Plus4 = 4,
        Plus5 = 5
    }

    // 負荷ウェーブ1つぶんの時間と、足す人数
    [Serializable]
    public struct LoadWaveWindow
    {
        [Tooltip("始まる時刻（プレイ開始からの秒）。")]
        public float startSeconds;
        [Tooltip("長さ（秒）。")]
        [Min(0f)] public float durationSeconds;
        [Tooltip("基準の同時上限に足す人数。")]
        [Min(0)] public int added;

        public LoadWaveWindow(float start, float duration, int add)
        {
            startSeconds = start;
            durationSeconds = duration;
            added = add;
        }

        public float EndSeconds => startSeconds + durationSeconds;

        // t がこのウェーブの中か（終わりの時刻ちょうどは入らない）
        public bool Contains(float t) => t >= startSeconds && t < EndSeconds;
    }

    // ある時刻の負荷ウェーブの様子
    public readonly struct LoadWaveState
    {
        // 0: 小 / 1: 中 / 2: 大。ウェーブの外なら -1
        public readonly int Index;
        // このウェーブで足す人数（増やしきったときの値）
        public readonly int Added;
        // 今足している人数（始まってから rampSeconds かけて Added まで増える）
        public readonly int Bonus;
        public readonly float StartSeconds;
        public readonly float EndSeconds;

        public LoadWaveState(int index, int added, int bonus, float start, float end)
        {
            Index = index;
            Added = added;
            Bonus = bonus;
            StartSeconds = start;
            EndSeconds = end;
        }

        public static LoadWaveState None => new LoadWaveState(-1, 0, 0, 0f, 0f);

        public bool IsActive => Index >= 0;

        public string Label => IsActive ? SpawnTimetable.WaveLabels[Index] : "通常";
    }

    /*
        180秒の時間割（企画書 v8 8章「時間割」「スポーン」「負荷ウェーブ」、10章「解禁スケジュール」「出現比率」、付録B）（#57）

        時刻を1つ渡すと、そのときの「同時にいていい人数」と「客の種類のくじのわりあい」を返す。それだけのクラス
          ・解禁: 通常客は最初から。欲張り 0:38 → 遠方 0:48 → 移動 1:00。ボス 2:00 は追加要素なので、ふつうは OFF
          ・わりあい: 月ごとのわりあい（6月・11月・1月）から、まだ解禁されていない種類のぶんを通常客に寄せる
            6月のわりあい 55/0/15/30 から寄せると 0:30〜0:38 は 100/0/0/0、0:38〜0:48 は 70/0/0/30 になって、付録B の 06A/06B/06C と同じになる
            1月はボスを OFF にするとボスの 5 が通常客にもどって 60/10/15/15（付録B SPAWN.TYPE.01 の MVP の値）
          ・同時上限: 基準10人。負荷ウェーブの間だけ上限を上げる（一気に出す「ウェーブ制」じゃない。8章）
              小 0:40〜0:55 +3人 / 中 1:20〜1:45 +5人 / 大 2:20〜3:00 +3/+4/+5人（T3 でくらべる。ふつうは +4）。合計は15人をこえない
              始まったら rampSeconds（5秒）かけて1人ずつ上げる。終わったら上限を下げるだけで、多いぶんの客はむりやり帰らせない（出す側の担当）
          ・負荷ウェーブの間は、通常客のわりあいを +15 ポイントにして、ほかの種類は元のわりあいのまま小さくする
          ・3つの月（篝火3つ）それぞれに、ウェーブが1つずつある。名前・太鼓・季節の演出は追加要素なので、ここには持たない

        0:00〜0:30 の段階学習（#58）は、ここでは「通常客だけ・基準の上限」として返す
        段階学習を置いたシーンでは、その間 StagedLearningDirector が補充を止めて学習の客を1人ずつ置き、0:30.000 から時間割どおりに補充する
        MonoBehaviour は使っていない。さかい目はエディタのテストで確かめる（SpawnTimetableTests）
    */
    [Serializable]
    public class SpawnTimetable
    {
        // 競技の終わり（付録B GAME.END）
        public const float MatchEndSeconds = 180f;
        // 月の数（篝火の数）
        public const int MonthCount = 3;

        public static readonly string[] WaveLabels = { "小負荷ウェーブ", "中負荷ウェーブ", "大負荷ウェーブ" };
        public static readonly string[] MonthLabels = { "6月", "11月", "1月" };

        [Header("同時上限（付録B SPAWN.BASE / WAVE.FINAL）")]
        [Tooltip("通常時の同時上限（人）。付録B SPAWN.BASE＝10。黒客は数えない。")]
        [Min(0)]
        [SerializeField] private int baseCap = 10;
        [Tooltip("負荷ウェーブ中も含めた総同時上限（人）。付録B WAVE.FINAL＝15。")]
        [Min(0)]
        [SerializeField] private int maxTotalCap = 15;
        [Tooltip("ウェーブ開始から加算人数まで増やしきる秒数。8章「5秒かけて徐々に増員する」。")]
        [Min(0f)]
        [SerializeField] private float waveRampSeconds = 5f;
        [Tooltip("負荷ウェーブ中に通常客の抽選率へ足すポイント。8章・10章＝+15。残りの種類は元の比率で按分する。")]
        [Range(0f, 100f)]
        [SerializeField] private float waveNormalBonus = 15f;

        [Header("負荷ウェーブ（8章 時間割 / 付録B WAVE.*）")]
        [Tooltip("6月の小負荷ウェーブ。付録B WAVE.SMALL＝0:40 から +3人・15秒。")]
        [SerializeField] private LoadWaveWindow smallWave = new LoadWaveWindow(40f, 15f, 3);
        [Tooltip("11月の中負荷ウェーブ。付録B WAVE.MIDDLE＝1:20 から +5人・25秒。")]
        [SerializeField] private LoadWaveWindow middleWave = new LoadWaveWindow(80f, 25f, 5);
        [Tooltip("1月の大負荷ウェーブの開始（秒）。8章＝2:20。")]
        [SerializeField] private float finalWaveStartSeconds = 140f;
        [Tooltip("1月の大負荷ウェーブの長さ（秒）。付録B WAVE.FINAL＝40秒（最後まで続く）。")]
        [Min(0f)]
        [SerializeField] private float finalWaveDurationSeconds = 40f;
        [Tooltip("大負荷ウェーブの加算人数。T3 はここを +3／+4／+5 に切り替えて比較する（総上限13／14／15人）。基準は +4。")]
        [SerializeField] private FinalWaveAdd finalWaveAdd = FinalWaveAdd.Plus4;

        [Header("解禁（付録B UNLOCK.TIME）")]
        [Tooltip("欲張り客の解禁（秒）。0:38。")]
        [SerializeField] private float greedyUnlockSeconds = 38f;
        [Tooltip("遠方客の解禁（秒）。0:48。")]
        [SerializeField] private float distantUnlockSeconds = 48f;
        [Tooltip("移動客の解禁（秒）。1:00。")]
        [SerializeField] private float movingUnlockSeconds = 60f;
        [Tooltip("ボス客の解禁（秒）。2:00。追加要素なので bossEnabled が ON のときだけ使う。")]
        [SerializeField] private float bossUnlockSeconds = 120f;
        [Tooltip("ボス客（追加要素）を出すか。MVP・T3 は OFF（1月のボス5%は通常客へ戻る）。")]
        [SerializeField] private bool bossEnabled;

        [Header("月ごとの抽選率（付録B SPAWN.TYPE.*）")]
        [Tooltip("ゲーム内1ヶ月の秒数。企画書では60秒。")]
        [Min(1f)]
        [SerializeField] private float secondsPerMonth = 60f;
        [Tooltip("6月（0:48 以降の 06C）。未解禁の種類は通常客へ寄せるので、0:30〜0:38 は 06A、0:38〜0:48 は 06B になる。")]
        [SerializeField] private CustomerKindWeights juneWeights = CustomerKindWeights.June;
        [Tooltip("11月（SPAWN.TYPE.11）。")]
        [SerializeField] private CustomerKindWeights novemberWeights = CustomerKindWeights.November;
        [Tooltip("1月（SPAWN.TYPE.01）。ボス OFF なら 5 が通常客へ戻る。")]
        [SerializeField] private CustomerKindWeights januaryWeights = CustomerKindWeights.January;

        // くじのならびと同じ順番。解禁のチェックに使う
        static readonly CustomerKind[] NonNormalKinds =
        {
            CustomerKind.Moving, CustomerKind.Distant, CustomerKind.Greedy, CustomerKind.Boss
        };

        public int BaseCap => baseCap;
        public int MaxTotalCap => maxTotalCap;
        public float WaveRampSeconds => waveRampSeconds;
        public float WaveNormalBonus => waveNormalBonus;
        public int WaveCount => WaveLabels.Length;

        // 大負荷ウェーブの加算人数（T3 の条件）。プレイ中に変えると、次のフレームから上限に反映される
        public FinalWaveAdd FinalWaveAdd
        {
            get => finalWaveAdd;
            set => finalWaveAdd = value;
        }

        // ボス客（追加要素）を出すか
        public bool BossEnabled
        {
            get => bossEnabled;
            set => bossEnabled = value;
        }

        // ---- 月 ----

        // 時刻 t の月（1〜3）。1: 6月 / 2: 11月 / 3: 1月。篝火が何番目の区切りかと同じ
        public int MonthAt(float t)
        {
            return Mathf.Clamp(Mathf.FloorToInt(t / Mathf.Max(1f, secondsPerMonth)) + 1, 1, MonthCount);
        }

        public static string MonthLabel(int month) => MonthLabels[Mathf.Clamp(month, 1, MonthCount) - 1];

        // その月のもとのわりあい（解禁の前）
        public CustomerKindWeights MonthWeights(int month)
        {
            switch (Mathf.Clamp(month, 1, MonthCount))
            {
                case 1: return juneWeights;
                case 2: return novemberWeights;
                default: return januaryWeights;
            }
        }

        // ---- 解禁 ----

        // その種類が解禁される時刻（秒）。出てこない種類は +∞
        public float UnlockSeconds(CustomerKind kind)
        {
            switch (kind)
            {
                case CustomerKind.Normal:  return 0f;
                case CustomerKind.Greedy:  return greedyUnlockSeconds;
                case CustomerKind.Distant: return distantUnlockSeconds;
                case CustomerKind.Moving:  return movingUnlockSeconds;
                case CustomerKind.Boss:    return bossEnabled ? bossUnlockSeconds : float.PositiveInfinity;
                default:                   return float.PositiveInfinity;
            }
        }

        public bool IsUnlocked(CustomerKind kind, float t) => t >= UnlockSeconds(kind);

        // ---- 負荷ウェーブ ----

        // index 番のウェーブの時間と人数（2 番＝大負荷ウェーブは finalWaveAdd の人数）
        public LoadWaveWindow WaveWindow(int index)
        {
            switch (index)
            {
                case 0: return smallWave;
                case 1: return middleWave;
                case 2: return new LoadWaveWindow(finalWaveStartSeconds, finalWaveDurationSeconds, (int)finalWaveAdd);
                default: return default;
            }
        }

        // 時刻 t の負荷ウェーブ。ウェーブの外なら LoadWaveState.None
        public LoadWaveState WaveAt(float t)
        {
            for (int i = 0; i < WaveCount; i++)
            {
                LoadWaveWindow w = WaveWindow(i);
                if (!w.Contains(t)) continue;

                int bonus = RampedBonus(w.added, t - w.startSeconds, waveRampSeconds);
                return new LoadWaveState(i, w.added, bonus, w.startSeconds, w.EndSeconds);
            }
            return LoadWaveState.None;
        }

        /*
            始まってから sinceStart 秒たったときに足している人数。rampSeconds かけて 0 から added まで1人ずつ上げる
            （+5 を5秒なら 1秒ごとに1人）。rampSeconds が 0 なら、始まった瞬間に added
        */
        public static int RampedBonus(int added, float sinceStart, float rampSeconds)
        {
            if (added <= 0 || sinceStart < 0f) return 0;
            if (rampSeconds <= 0f || sinceStart >= rampSeconds) return added;
            // 1e-4 は、ちょうど1秒のところで float の誤差で1人少なくならないようにするため
            return Mathf.Clamp(Mathf.FloorToInt(added * sinceStart / rampSeconds + 1e-4f), 0, added);
        }

        // ---- そのときの上限とわりあい ----

        // 時刻 t の同時上限（人）。黒客は数えない（8章）
        public int CapAt(float t)
        {
            return Mathf.Max(0, Mathf.Min(maxTotalCap, baseCap + WaveAt(t).Bonus));
        }

        // 時刻 t の月のわりあいから、まだ解禁されていない種類のぶんを通常客に寄せたもの（負荷ウェーブの足し算の前）
        public CustomerKindWeights UnlockedWeightsAt(float t)
        {
            CustomerKindWeights w = MonthWeights(MonthAt(t));
            float normal = w.normal;
            foreach (CustomerKind kind in NonNormalKinds)
            {
                if (IsUnlocked(kind, t)) continue;
                normal += w.Get(kind);
                w = w.With(kind, 0f);
            }
            return w.With(CustomerKind.Normal, normal);
        }

        // 時刻 t の客の種類のくじのわりあい。負荷ウェーブの間は通常客を +waveNormalBonus ポイントにする
        public CustomerKindWeights WeightsAt(float t)
        {
            CustomerKindWeights w = UnlockedWeightsAt(t);
            return WaveAt(t).IsActive ? WithNormalBonus(w, waveNormalBonus) : w;
        }

        /*
            通常客のわりあいを points ポイント（100% に直したときの値）足して、ほかの種類は元のわりあいのまま小さくする
            返す値は合計 100 にそろえたわりあい。例: 1月 MVP 60/10/15/15 に +15 → 75/6.25/9.375/9.375（8章の T3 の計算と同じ）
        */
        public static CustomerKindWeights WithNormalBonus(CustomerKindWeights w, float points)
        {
            float total = w.Total;
            if (total <= 0f) return w;

            float normal = w.normal / total * 100f;
            float nextNormal = Mathf.Clamp(normal + points, 0f, 100f);
            float others = 100f - normal;
            float scale = others > 0f ? (100f - nextNormal) / others : 0f;

            var result = new CustomerKindWeights { normal = nextNormal };
            foreach (CustomerKind kind in NonNormalKinds)
                result = result.With(kind, w.Get(kind) / total * 100f * scale);
            return result;
        }

        // ---- 表示・確認用 ----

        // 「1:23 11月 中負荷ウェーブ +5（いま +5）上限 15人」みたいな1行
        public string Describe(float t)
        {
            LoadWaveState wave = WaveAt(t);
            string waveText = wave.IsActive ? $"{wave.Label} +{wave.Added}（いま +{wave.Bonus}）" : "通常";
            return $"{FormatClock(t)} {MonthLabel(MonthAt(t))} {waveText} 上限 {CapAt(t)}人";
        }

        // 解禁ずみの種類を「通常・欲張り・遠方」の形で
        public string DescribeUnlocked(float t)
        {
            var sb = new StringBuilder("通常");
            if (IsUnlocked(CustomerKind.Greedy, t)) sb.Append("・欲張り");
            if (IsUnlocked(CustomerKind.Distant, t)) sb.Append("・遠方");
            if (IsUnlocked(CustomerKind.Moving, t)) sb.Append("・移動");
            if (IsUnlocked(CustomerKind.Boss, t)) sb.Append("・ボス");
            return sb.ToString();
        }

        // 秒を「m:ss」に
        public static string FormatClock(float seconds)
        {
            int s = Mathf.Max(0, Mathf.FloorToInt(seconds));
            return $"{s / 60}:{s % 60:00}";
        }

        /*
            時間割の中で、上限やわりあいが変わる時刻をならべたもの（解禁・ウェーブの開始と終わり・月の区切り）
            ログやテストで「区切りごとに何が変わったか」を見るのに使う
        */
        public List<float> ChangePoints()
        {
            var points = new List<float> { 0f };
            for (int m = 1; m < MonthCount; m++) points.Add(m * secondsPerMonth);
            foreach (CustomerKind kind in NonNormalKinds)
            {
                float u = UnlockSeconds(kind);
                if (!float.IsInfinity(u)) points.Add(u);
            }
            for (int i = 0; i < WaveCount; i++)
            {
                LoadWaveWindow w = WaveWindow(i);
                points.Add(w.startSeconds);
                points.Add(w.EndSeconds);
            }
            points.RemoveAll(p => p < 0f || p > MatchEndSeconds);
            points.Sort();
            for (int i = points.Count - 1; i > 0; i--)
            {
                if (Mathf.Abs(points[i] - points[i - 1]) < 0.0001f) points.RemoveAt(i);
            }
            return points;
        }

        /*
            値が仕様の約束をやぶっていないかを調べる。問題がなければ空のリスト
              ・ウェーブは時刻の順にならんで重ならない。1つの月（篝火1つ）に1つずつ入る
              ・大負荷ウェーブは 3:00 で終わる（最後まで続く）
              ・基準＋加算が総上限をこえない
              ・解禁は 欲張り → 遠方 → 移動 の順
        */
        public List<string> Validate()
        {
            var problems = new List<string>();

            for (int i = 0; i < WaveCount; i++)
            {
                LoadWaveWindow w = WaveWindow(i);
                if (w.durationSeconds <= 0f) problems.Add($"{WaveLabels[i]} の長さが 0 秒です。");
                if (MonthAt(w.startSeconds) != i + 1 || MonthAt(w.EndSeconds - 0.001f) != i + 1)
                    problems.Add($"{WaveLabels[i]}（{FormatClock(w.startSeconds)}〜{FormatClock(w.EndSeconds)}）が {MonthLabel(i + 1)} の中に収まっていません。");
                if (baseCap + w.added > maxTotalCap)
                    problems.Add($"{WaveLabels[i]} で 基準{baseCap}＋{w.added}＝{baseCap + w.added}人 が総上限 {maxTotalCap}人 をこえます（上限で止まります）。");
                if (i > 0 && w.startSeconds < WaveWindow(i - 1).EndSeconds)
                    problems.Add($"{WaveLabels[i]} が {WaveLabels[i - 1]} と重なっています。");
            }

            if (Mathf.Abs(WaveWindow(2).EndSeconds - MatchEndSeconds) > 0.001f)
                problems.Add($"大負荷ウェーブが {FormatClock(WaveWindow(2).EndSeconds)} で終わります（8章は 3:00 まで続く）。");

            if (!(greedyUnlockSeconds < distantUnlockSeconds && distantUnlockSeconds < movingUnlockSeconds))
                problems.Add("解禁の順番が 欲張り → 遠方 → 移動 になっていません（10章）。");

            return problems;
        }

        /*
            T0-3M（#53）で測った1投の間かく（中央値・p75）で、それぞれの負荷ウェーブに追いつけるかを計算する（8章）
            ウェーブの中でわりあいが変わる（小負荷ウェーブは 0:48 に遠方客が解禁される）ときは、いちばん重い時刻の値を使う
            table: 客の種類ごとの数値（D が満タンになる秒数・最初の R）
            medianCycle / p75Cycle: 1投の間かく（秒）。0 以下なら「まだ測っていない」として判定を出さない
        */
        public string DescribeLoad(CustomerKindTable table, double medianCycle, double p75Cycle)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"負荷ウェーブの算術（8章。全命中・待ち時間なしの定常近似。成立の証明ではなく棄却の上限）");
            sb.AppendLine(medianCycle > 0.0 && p75Cycle > 0.0
                ? $"実操作周期 中央値 {medianCycle:0.00}秒 / p75 {p75Cycle:0.00}秒"
                : "実操作周期: 未測定（T0-3M の中央値・p75 を入れると判定します）");

            for (int i = 0; i < WaveCount; i++)
            {
                WaveCandidate c = HeaviestCandidate(i, table, out float atSeconds, out CustomerKindWeights weights);
                sb.Append($"{WaveLabels[i]} {FormatClock(WaveWindow(i).startSeconds)}〜 +{c.Added}（上限{c.TotalCap}人） ")
                  .Append($"比率@{FormatClock(atSeconds)} {weights.Describe()} → ")
                  .Append($"救済/秒 {c.RequiredRescuesPerSecond:0.00} / 命中/秒 {c.RequiredHitsPerSecond:0.00} / 許容周期 {c.AllowedCycleSeconds:0.00}秒");
                if (medianCycle > 0.0 && p75Cycle > 0.0)
                    sb.Append($" → {WaveLoadArithmetic.VerdictLabel(WaveLoadArithmetic.Judge(c, medianCycle, p75Cycle), t3Candidate: i == 2)}");
                sb.AppendLine();
            }
            return sb.ToString();
        }

        /*
            index 番のウェーブで、許される1投の間かくがいちばん短くなる（いちばん重い）時刻の計算結果
            見る時刻: 増やしきった時刻と、ウェーブの中にある解禁・月の区切り
        */
        public WaveCandidate HeaviestCandidate(int index, CustomerKindTable table, out float atSeconds, out CustomerKindWeights weights)
        {
            LoadWaveWindow w = WaveWindow(index);
            var samples = new List<float> { Mathf.Min(w.startSeconds + waveRampSeconds, w.EndSeconds - 0.001f) };
            foreach (float p in ChangePoints())
            {
                if (p > w.startSeconds && p < w.EndSeconds) samples.Add(p);
            }

            WaveCandidate best = default;
            atSeconds = w.startSeconds;
            weights = default;
            bool found = false;
            foreach (float t in samples)
            {
                CustomerKindWeights wt = WeightsAt(t);
                KindMix mix = WaveLoadArithmetic.MixOf(wt, table);
                WaveCandidate c = WaveLoadArithmetic.Of(w.added, Mathf.Min(maxTotalCap, baseCap + w.added), mix);
                if (!found || c.AllowedCycleSeconds < best.AllowedCycleSeconds)
                {
                    best = c;
                    atSeconds = t;
                    weights = wt;
                    found = true;
                }
            }
            return best;
        }
    }
}
