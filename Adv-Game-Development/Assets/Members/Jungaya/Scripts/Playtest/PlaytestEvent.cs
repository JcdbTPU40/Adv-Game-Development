namespace Toufuku.Playtest
{
    /// <summary>イベントログの「event」列に入る種別。集計スクリプト・表計算はこの文字列で絞り込む。</summary>
    public static class PlaytestEventType
    {
        public const string SessionStart = "session_start";
        public const string SessionEnd = "session_end";
        public const string Month = "month";
        public const string Spawn = "spawn";
        public const string Despawn = "despawn";
        /// <summary>色の選択が変わった。</summary>
        public const string Select = "select";
        /// <summary>有効スイング確定（発射確定）。</summary>
        public const string Fire = "fire";
        /// <summary>振りピークを却下した（detail = 理由）。</summary>
        public const string SwingRejected = "swing_rejected";
        /// <summary>弾の着弾（客に当たった／外した）。命中時は得点・倍率も同じ行に入る。</summary>
        public const string Landing = "landing";
        /// <summary>着弾を伴わない命中（物理弾 OmamoriBullet）。</summary>
        public const string Hit = "hit";
        /// <summary>救済成功。</summary>
        public const string Rescue = "rescue";
        /// <summary>
        /// 笑顔の伝播が 1 件成立した（#56）。target_id = 伝播を受けた客、gain = その 1 回で入った縁、
        /// multiplier = 救済時の倍率スナップショット、value = この救済で何人目か、detail = from:救済客ID。
        /// </summary>
        public const string Propagate = "propagate";
        /// <summary>黒客化（ゲージ満タン）。</summary>
        public const string BlackConversion = "black_conversion";
        public const string Rating = "rating";
        public const string GokagoStart = "gokago_start";
        public const string GokagoEnd = "gokago_end";

        /// <summary>停止（振りが止まって idleThresholdSeconds 経過した時点で記録。value = 最後に振った時刻）。</summary>
        public const string IdleStart = "idle_start";
        /// <summary>停止の終わり（次の振り。value = 停止の秒数）。</summary>
        public const string IdleEnd = "idle_end";

        // ---- T1（#58 の段階学習から PlaytestLog 経由で記録。仕様書 v8 19章の記録シート）----
        /// <summary>学習段階の開始（stage = 段階番号 1〜3）。</summary>
        public const string T1StageStart = "t1_stage_start";
        /// <summary>学習段階の終わり（stage = 段階番号、detail = achieved / timeout）。</summary>
        public const string T1StageEnd = "t1_stage_end";
        /// <summary>自由練習の秒数（value）。記録しなければ 3 段階目の達成から学習区間の終わりまでの秒で集計する。</summary>
        public const string T1FreePractice = "t1_free_practice";
        /// <summary>0:30.000 のカウンタ初期化（競技開始）。</summary>
        public const string T1CounterReset = "t1_counter_reset";
        /// <summary>ゴースト表示（detail = 状況）。</summary>
        public const string T1Ghost = "t1_ghost";
        /// <summary>スタッフの介入（detail = 内容）。</summary>
        public const string T1Intervention = "t1_intervention";

        // ---- T2 ----
        /// <summary>T2 の試行開始。無ければセッション開始を起点にする。</summary>
        public const string T2TrialStart = "t2_trial_start";
    }

    /// <summary>イベントログ 1 行。null の列は空欄で書き出す。列の意味は <see cref="PlaytestCsv.EventColumns"/>。</summary>
    public sealed class PlaytestEvent
    {
        /// <summary>セッション開始からの秒（ゲーム内時計。T3 の区間分けに使う）。</summary>
        public double T;
        /// <summary>Time.realtimeSinceStartupAsDouble。</summary>
        public double Realtime;
        public int Frame;
        public string Type;

        public int? ThrowNo;
        public int? TargetId;
        public string Category;
        /// <summary>客の色（黒客は Black）。</summary>
        public string Color;
        /// <summary>投げた（選んだ）お守りの種類。</summary>
        public string Omamori;
        public bool? IsBlack;
        /// <summary>命中精度: 判定半径に対する中心からの距離（0 = 中心、1 = 縁）。</summary>
        public double? Accuracy;
        public string Zone;
        /// <summary>色誤り（黒客以外への相性✗の命中）。</summary>
        public bool? ColorError;
        public int? Gain;
        public int? En;
        public double? Multiplier;
        /// <summary>計上後の福の連なり（ScoreManager.Combo）。</summary>
        public int? FukuChain;
        public bool? Rescued;
        public int? PriorityTargetId;

        /// <summary>入力時刻（コントローラ側の時計・秒）。ファームウェアが送らなければ空欄。</summary>
        public double? InputTime;
        /// <summary>Unity 受信時刻（振りピークのサンプルを受け取った realtimeSinceStartup）。</summary>
        public double? ReceiveTime;
        /// <summary>発射確定時刻（SwingAccepted を配った realtimeSinceStartup）。</summary>
        public double? FireTime;

        public double? Strength;
        public double? FlightSeconds;
        public double? Distance;
        public double? PosX;
        public double? PosZ;
        /// <summary>危険度 D（0〜100）。#54 までは不満ゲージ × 100。</summary>
        public double? Danger;
        public double? Rating;
        public string Rank;
        public int? Stage;
        public double? Value;
        public string Detail;

        public PlaytestEvent(double t, string type)
        {
            T = t;
            Type = type;
        }
    }
}
