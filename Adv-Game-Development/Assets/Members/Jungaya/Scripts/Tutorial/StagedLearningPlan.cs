using System;
using System.Collections.Generic;
using UnityEngine;
using Toufuku.Playtest;
using Toufuku.Rescue;

namespace Toufuku.Tutorial
{
    // 段階学習の段階（企画書 v8 8章「時間割」・18章「開始30秒の段階学習」）
    public enum LearningStage
    {
        NotStarted = 0,
        // 教える1「届ける」: 緑1色・1人（〜0:08）
        Deliver = 1,
        // 教える2「選ぶ」: 2色・2人（〜0:18）
        Choose = 2,
        // 教える3「見抜く」: 3色・3人、1人だけ D=65 と二重円（〜0:30）
        Discern = 3,
        // 見抜くを達成したあと、0:30 までの自由練習
        FreePractice = 4,
        // 0:30.000 からの競技
        Competition = 5
    }

    // 状況べつゴーストの種類。「色を選ぶ→狙う→振る→危険客を救う」のうち、足りない操作を1つだけ見せる
    public enum LearningGhost
    {
        None = 0,
        // 振っていない: 大幣が一振りする
        Swing = 1,
        // 客に当たっていない: 照準が客へすべる
        Aim = 2,
        // 正しい色で当てていない: 照準が客へすべって、その客の色のボタンが脈動する
        Color = 3,
        // 二重円の客を救っていない: 照準が二重円の客へすべって、その客の色のボタンが脈動する
        Priority = 4
    }

    // 段階学習の数値。正本は企画書 v8 8章「時間割」と18章の表
    [Serializable]
    public class StagedLearningSettings
    {
        [Header("締切（秒。8章 時間割）")]
        [Tooltip("教える1「届ける」の締切。未達でもこの時刻に「選ぶ」へ進む。")]
        [Min(0f)] public float deliverEndSeconds = 8f;
        [Tooltip("教える2「選ぶ」の締切。未達でもこの時刻に「見抜く」へ進む。")]
        [Min(0f)] public float chooseEndSeconds = 18f;
        [Tooltip("学習の終わり＝競技の始まり。GameSession があるときはその learningSeconds で上書きする。")]
        [Min(0f)] public float learningSeconds = 30f;

        [Header("早期達成の条件")]
        [Tooltip("教える2で「見抜く」へ進むのに必要な救済人数（8章「2人救済で直ちに次へ」）。")]
        [Min(1)] public int chooseRequiredRescues = 2;

        [Header("場面（色）")]
        [Tooltip("教える1の客の色。18章「緑1色・1人」。")]
        public OmamoriType[] deliverColors = { OmamoriType.Kenkou };
        [Tooltip("教える2の客の色。18章「緑と青の2人」。")]
        public OmamoriType[] chooseColors = { OmamoriType.Kenkou, OmamoriType.Gakugyou };
        [Tooltip("教える3と自由練習の客の色。8章「3色・3人」。")]
        public OmamoriType[] discernColors = { OmamoriType.Kenkou, OmamoriType.Gakugyou, OmamoriType.Enmusubi };
        [Tooltip("教える3で D をスクリプトで固定して、危険円＋二重円を出す客の色。")]
        public OmamoriType priorityColor = OmamoriType.Enmusubi;
        [Tooltip("教える3の二重円の客に固定する D。18章「1人だけD=65をスクリプトで固定」。危険円が出る 50 以上にする。")]
        [Range(0f, 99f)] public float priorityDanger = 65f;

        [Header("ゴースト")]
        [Tooltip("教える1で、段階の始まりか最後の振りからこの秒数振らなかったら、大幣ゴーストを1回だけ出す（18章「3秒止まったら」）。")]
        [Min(0.1f)] public float idleGhostSeconds = 3f;
        [Tooltip("0:30 のあとの状況べつゴーストを出してよい秒数（8章「最初の5秒だけ」）。")]
        [Min(0f)] public float afterLearningGhostWindowSeconds = 5f;

        // 段階ごとの客の色
        public OmamoriType[] ColorsFor(LearningStage stage)
        {
            switch (stage)
            {
                case LearningStage.Deliver: return deliverColors ?? Array.Empty<OmamoriType>();
                case LearningStage.Choose: return chooseColors ?? Array.Empty<OmamoriType>();
                case LearningStage.Discern:
                case LearningStage.FreePractice: return discernColors ?? Array.Empty<OmamoriType>();
                default: return Array.Empty<OmamoriType>();
            }
        }

        // 値が仕様の約束をやぶっていないかを調べる。問題がなければ空のリスト
        public List<string> Validate()
        {
            var problems = new List<string>();

            if (!(0f < deliverEndSeconds && deliverEndSeconds < chooseEndSeconds && chooseEndSeconds < learningSeconds))
                problems.Add($"締切が 0 < 届ける({deliverEndSeconds}) < 選ぶ({chooseEndSeconds}) < 学習の終わり({learningSeconds}) の順になっていません。");

            CheckColors(problems, "教える1", deliverColors);
            CheckColors(problems, "教える2", chooseColors);
            CheckColors(problems, "教える3", discernColors);

            if (!ContainsAll(chooseColors, deliverColors))
                problems.Add("教える2の色に教える1の色が入っていません（前の段階で救えなかった客が余分に残ります）。");
            if (!ContainsAll(discernColors, chooseColors))
                problems.Add("教える3の色に教える2の色が入っていません（前の段階で救えなかった客が余分に残ります）。");

            if (chooseColors != null && chooseRequiredRescues > chooseColors.Length)
                problems.Add($"教える2の必要な救済人数 {chooseRequiredRescues} が客の人数 {chooseColors.Length} をこえています（早期達成できません）。");

            if (Array.IndexOf(discernColors ?? Array.Empty<OmamoriType>(), priorityColor) < 0)
                problems.Add($"二重円の客の色 {priorityColor} が教える3の色に入っていません。");
            else if (Array.IndexOf(chooseColors ?? Array.Empty<OmamoriType>(), priorityColor) >= 0)
                problems.Add($"二重円の客の色 {priorityColor} が教える2の色にも入っています（教える2で救えなかった客がそのまま二重円になります）。");

            if (priorityDanger < DangerRingDisplay.ShowDanger)
                problems.Add($"二重円の客の D={priorityDanger} では危険円が出ません（{DangerRingDisplay.ShowDanger} 以上にしてください）。");

            return problems;
        }

        static void CheckColors(List<string> problems, string label, OmamoriType[] colors)
        {
            if (colors == null || colors.Length == 0)
            {
                problems.Add($"{label}の色がありません。");
                return;
            }
            if (new HashSet<OmamoriType>(colors).Count != colors.Length)
                problems.Add($"{label}の色が重なっています（1色1人）。");
        }

        static bool ContainsAll(OmamoriType[] set, OmamoriType[] subset)
        {
            if (subset == null) return true;
            if (set == null) return subset.Length == 0;
            foreach (OmamoriType c in subset)
            {
                if (Array.IndexOf(set, c) < 0) return false;
            }
            return true;
        }
    }

    /*
        開始30秒の段階学習の進み方（#58 / 企画書 v8 8章「時間割」、18章「開始30秒の段階学習」、17章 T1）

        時刻（GameSession の時計の秒）とできごと（振った・客に当たった・救えた）だけを受け取って、
        段階の始まりと終わり（早期達成・時間切れ）、自由練習の秒、競技の始まり、ゴーストを出す合図を返す。それだけのクラス
          ・教える1「届ける」: 1人救えたら直ちに「選ぶ」。未達でも 0:08 で「選ぶ」
            段階の始まりか最後の振りから3秒振らなかったら、大幣ゴーストを1回だけ（1プレイに1回）
          ・教える2「選ぶ」: この段階で2人救えたら直ちに「見抜く」。未達でも 0:18 で「見抜く」
          ・教える3「見抜く」: 二重円の客を救えたら直ちに自由練習。未達でも 0:30 で競技
          ・0:30.000: 競技の始まり。見抜くが未達なら、足りない操作のゴーストを1つ決めて合図する
        締切ちょうどの時刻に起きたことは次の段階として数える（0:08.000 に救えたら「選ぶ」の1人目）
        いくつかの締切をいっぺんにこえた（おそいフレーム）ときも、締切の時刻の順に1つずつ進める

        学習の値（救済数・命中など）は競技に持ちこまない。0:30 からの得点は ScoreManager / ShrineRating が 0 から数える
        MonoBehaviour は使っていない。進み方はエディタのテストで確かめる（StagedLearningPlanTests）
    */
    public sealed class StagedLearningPlan
    {
        // T1Ghost の detail の始まり（PlaytestMetrics で集計する）
        public const string ReasonStage1Idle = PlaytestMetrics.GhostStage1Idle;
        public const string ReasonAfterLearning = PlaytestMetrics.GhostAfterLearning;

        readonly StagedLearningSettings _settings;
        readonly bool[] _achieved = new bool[4];
        double _lastSwing = double.NegativeInfinity;

        // 段階が始まった（引数: 段階, 時刻）。自由練習もここで知らせる。競技は CompetitionStarted
        public event Action<LearningStage, double> StageStarted;
        // 段階が終わった（引数: 段階 1〜3, 早期達成したか, 時刻）
        public event Action<LearningStage, bool, double> StageEnded;
        // 自由練習が終わった（引数: 自由練習の秒, 時刻）。見抜くを達成したときだけ
        public event Action<double, double> FreePracticeEnded;
        // 競技が始まった（引数: 時刻＝学習の終わり）
        public event Action<double> CompetitionStarted;
        // ゴーストを出す（引数: 種類, 理由 ReasonStage1Idle / ReasonAfterLearning, 合図の時刻）
        public event Action<LearningGhost, string, double> GhostRequested;

        public StagedLearningPlan(StagedLearningSettings settings)
        {
            _settings = settings ?? new StagedLearningSettings();
        }

        public StagedLearningSettings Settings => _settings;
        public LearningStage Stage { get; private set; }
        // 今の段階が始まった時刻
        public double StageStartSeconds { get; private set; }
        // 最後に進めた時刻
        public double Now { get; private set; }
        // 学習中（届ける〜自由練習）か
        public bool IsLearning => Stage >= LearningStage.Deliver && Stage <= LearningStage.FreePractice;

        // ---- 学習中に数えたもの（0:30 のあとは数えない） ----
        public int AcceptedSwings { get; private set; }
        public int CustomerHits { get; private set; }
        public int CorrectColorHits { get; private set; }
        public int ColorErrors { get; private set; }
        public int Rescues { get; private set; }
        // 今の段階で救った人数
        public int StageRescues { get; private set; }
        public bool PriorityRescued { get; private set; }
        public double? FirstRescueSeconds { get; private set; }
        public double? FreePracticeStartSeconds { get; private set; }
        public bool IdleGhostRequested { get; private set; }
        // 0:30 に合図したゴースト（見抜くを達成していれば None）
        public LearningGhost AfterLearningGhost { get; private set; }

        double DeliverEnd => Math.Min(_settings.deliverEndSeconds, _settings.learningSeconds);
        double ChooseEnd => Math.Min(_settings.chooseEndSeconds, _settings.learningSeconds);

        // 段階 stage（届ける・選ぶ・見抜く）を早期達成したか
        public bool IsAchieved(LearningStage stage)
        {
            int i = (int)stage;
            return i >= 1 && i <= 3 && _achieved[i];
        }

        // 時刻 t から学習を始める（リトライのときも使う）
        public void Start(double t = 0.0)
        {
            Array.Clear(_achieved, 0, _achieved.Length);
            _lastSwing = double.NegativeInfinity;
            AcceptedSwings = 0;
            CustomerHits = 0;
            CorrectColorHits = 0;
            ColorErrors = 0;
            Rescues = 0;
            StageRescues = 0;
            PriorityRescued = false;
            FirstRescueSeconds = null;
            FreePracticeStartSeconds = null;
            IdleGhostRequested = false;
            AfterLearningGhost = LearningGhost.None;

            Now = t;
            BeginStage(LearningStage.Deliver, t);
            Advance(t);
        }

        // 学習をやめる（段階学習を置いたシーンから外したときなど）。合図は出さない
        public void Stop()
        {
            Stage = LearningStage.NotStarted;
        }

        // 時刻 t まで進める。締切をこえていたら、締切の時刻で時間切れにして次の段階へ進める
        public void Advance(double t)
        {
            if (t < Now) t = Now;

            while (IsLearning)
            {
                if (Stage == LearningStage.Deliver)
                {
                    CheckIdleGhost(Math.Min(t, DeliverEnd));
                    if (t < DeliverEnd) break;
                    EndStage(false, DeliverEnd);
                    BeginStage(LearningStage.Choose, DeliverEnd);
                }
                else if (Stage == LearningStage.Choose)
                {
                    if (t < ChooseEnd) break;
                    EndStage(false, ChooseEnd);
                    BeginStage(LearningStage.Discern, ChooseEnd);
                }
                else
                {
                    if (t < _settings.learningSeconds) break;
                    FinishLearning(_settings.learningSeconds);
                }
            }

            Now = t;
        }

        // 有効スイングが決まった（ふつうの弾）
        public void NoteSwingAccepted(double t)
        {
            Advance(t);
            if (!IsLearning) return;
            AcceptedSwings++;
            _lastSwing = Math.Max(_lastSwing, t);
        }

        // 学習の客に弾が当たった（黒客は渡さない）。correctColor: 相性◯で当たったか
        public void NoteCustomerHit(double t, bool correctColor)
        {
            Advance(t);
            if (!IsLearning) return;
            CustomerHits++;
            if (correctColor) CorrectColorHits++;
            else ColorErrors++;
        }

        /*
            学習の客を救えた。isPriorityCustomer: 教える3で D を固定した二重円の客か
            返す値: 学習の救済として数えたら true（0:30 のあとなら false）
        */
        public bool NoteRescue(double t, bool isPriorityCustomer)
        {
            Advance(t);
            if (!IsLearning) return false;

            Rescues++;
            if (!FirstRescueSeconds.HasValue) FirstRescueSeconds = t;
            StageRescues++;

            switch (Stage)
            {
                case LearningStage.Deliver:
                    Achieve(t, LearningStage.Choose);
                    break;
                case LearningStage.Choose:
                    if (StageRescues >= Math.Max(1, _settings.chooseRequiredRescues)) Achieve(t, LearningStage.Discern);
                    break;
                case LearningStage.Discern:
                    if (isPriorityCustomer)
                    {
                        PriorityRescued = true;
                        FreePracticeStartSeconds = t;
                        Achieve(t, LearningStage.FreePractice);
                    }
                    break;
            }
            return true;
        }

        /*
            0:30 の時点で足りない操作を「色を選ぶ→狙う→振る→危険客を救う」の、できていないいちばん手前から1つ選ぶ
            ただし「振る」がないと色も狙いも確かめられないので、判定の順は 振る → 狙う（客に当たる）→ 色 → 二重円 にする
        */
        public static LearningGhost DecideGhost(int acceptedSwings, int customerHits, int correctColorHits, bool priorityRescued)
        {
            if (acceptedSwings <= 0) return LearningGhost.Swing;
            if (customerHits <= 0) return LearningGhost.Aim;
            if (correctColorHits <= 0) return LearningGhost.Color;
            if (!priorityRescued) return LearningGhost.Priority;
            return LearningGhost.None;
        }

        void CheckIdleGhost(double at)
        {
            if (IdleGhostRequested) return;
            double due = Math.Max(StageStartSeconds, _lastSwing) + _settings.idleGhostSeconds;
            if (at < due) return;

            IdleGhostRequested = true;
            GhostRequested?.Invoke(LearningGhost.Swing, ReasonStage1Idle, due);
        }

        void FinishLearning(double t)
        {
            if (Stage == LearningStage.Discern)
                EndStage(false, t);
            else if (Stage == LearningStage.FreePractice && FreePracticeStartSeconds.HasValue)
                FreePracticeEnded?.Invoke(t - FreePracticeStartSeconds.Value, t);

            Stage = LearningStage.Competition;
            StageStartSeconds = t;
            CompetitionStarted?.Invoke(t);

            if (IsAchieved(LearningStage.Discern)) return;
            AfterLearningGhost = DecideGhost(AcceptedSwings, CustomerHits, CorrectColorHits, PriorityRescued);
            if (AfterLearningGhost != LearningGhost.None)
                GhostRequested?.Invoke(AfterLearningGhost, ReasonAfterLearning, t);
        }

        void Achieve(double t, LearningStage next)
        {
            EndStage(true, t);
            BeginStage(next, t);
        }

        void BeginStage(LearningStage stage, double t)
        {
            Stage = stage;
            StageStartSeconds = t;
            StageRescues = 0;
            StageStarted?.Invoke(stage, t);
        }

        void EndStage(bool achieved, double t)
        {
            int i = (int)Stage;
            if (i >= 1 && i <= 3) _achieved[i] = achieved;
            StageEnded?.Invoke(Stage, achieved, t);
        }
    }
}
