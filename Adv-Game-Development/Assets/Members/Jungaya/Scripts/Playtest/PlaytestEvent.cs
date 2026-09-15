namespace Toufuku.Playtest
{
    // イベントログの「event」列に入る種類。集計のスクリプトや表計算はこの文字で絞りこむ
    public static class PlaytestEventType
    {
        public const string SessionStart = "session_start";
        public const string SessionEnd = "session_end";
        public const string Month = "month";
        public const string Spawn = "spawn";
        public const string Despawn = "despawn";
        // 色を選びなおした
        public const string Select = "select";
        // 有効スイングが決まった（発射が決まった）
        public const string Fire = "fire";
        // 振りピークをはじいた（detail = 理由）
        public const string SwingRejected = "swing_rejected";
        // 弾が落ちた（客に当たった・外れた）。当たったときは得点と倍率も同じ行に入る
        public const string Landing = "landing";
        // 着弾じゃない当たり（物理の弾の OmamoriBullet）
        public const string Hit = "hit";
        // 救済できた
        public const string Rescue = "rescue";
        /*
            笑顔が1件伝わった（#56）
            target_id = 伝わった相手、gain = その1回で入った縁、multiplier = 救済したときに保存した倍率、
            value = その救済で何人目か、detail = from:救済した客のID
        */
        public const string Propagate = "propagate";
        // 黒客になった（ゲージが満タン）
        public const string BlackConversion = "black_conversion";
        public const string Rating = "rating";
        public const string GokagoStart = "gokago_start";
        public const string GokagoEnd = "gokago_end";

        // 止まった（振るのが止まって idleThresholdSeconds たった時点で記録する。value = 最後に振った時刻）
        public const string IdleStart = "idle_start";
        // 止まっていたのが終わった（次に振った。value = 止まっていた秒数）
        public const string IdleEnd = "idle_end";

        /*
            ---- T1（#58 の段階学習から PlaytestLog で記録する。企画書 v8 19章の記録シート） ----
            学習の段階が始まった（stage = 段階の番号 1〜3）
        */
        public const string T1StageStart = "t1_stage_start";
        // 学習の段階が終わった（stage = 段階の番号、detail = achieved / timeout）
        public const string T1StageEnd = "t1_stage_end";
        // 自由練習の秒数（value）。記録しなければ、3段階目をクリアしてから学習の区間が終わるまでの秒で集計する
        public const string T1FreePractice = "t1_free_practice";
        // 0:30.000 でカウンターを最初にもどした（本番スタート）
        public const string T1CounterReset = "t1_counter_reset";
        // ゴーストを表示した（detail = 状況。#58 は "stage1_idle:swing"（段階1で3秒止まった）/ "after_learning:aim" など（0:30 のあと））
        public const string T1Ghost = "t1_ghost";
        // 手伝った（detail = 内容。スタッフは "staff"、0:30 のあとの状況べつゴーストは "ghost:aim" など）
        public const string T1Intervention = "t1_intervention";

        /*
            ---- T2 ----
            T2 のテストを始めた。なければゲームの開始をスタートにする
        */
        public const string T2TrialStart = "t2_trial_start";
    }

    // イベントログの1行。null の列は空欄で書き出す。列の意味は PlaytestCsv.EventColumns を見る
    public sealed class PlaytestEvent
    {
        // ゲームが始まってからの秒（ゲームの中の時計。T3 の区間分けに使う）
        public double T;
        // Time.realtimeSinceStartupAsDouble
        public double Realtime;
        public int Frame;
        public string Type;

        public int? ThrowNo;
        public int? TargetId;
        public string Category;
        // 客の色（黒客は Black）
        public string Color;
        // 投げた（選んだ）お守りの種類
        public string Omamori;
        public bool? IsBlack;
        // 命中精度: 中心からの距離が判定半径の何割か（0 = 中心、1 = ふち）
        public double? Accuracy;
        public string Zone;
        // 色まちがい（黒客じゃない客に相性✗で当たった）
        public bool? ColorError;
        public int? Gain;
        public int? En;
        public double? Multiplier;
        // スコアに入れたあとの福の連なり（ScoreManager.Combo）
        public int? FukuChain;
        public bool? Rescued;
        public int? PriorityTargetId;

        // 入力時刻（コントローラー側の時計、秒）。ファームウェアが送ってこなければ空欄
        public double? InputTime;
        // Unity が受け取った時刻（振りピークのデータを受け取った realtimeSinceStartup）
        public double? ReceiveTime;
        // 発射が決まった時刻（SwingAccepted を配った realtimeSinceStartup）
        public double? FireTime;

        public double? Strength;
        public double? FlightSeconds;
        public double? Distance;
        public double? PosX;
        public double? PosZ;
        // 危険度 D（0〜100）。#54 までは不満ゲージ × 100
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
