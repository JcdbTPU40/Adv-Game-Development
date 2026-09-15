using System;
using UnityEngine;
using UnityEngine.Serialization;
using Toufuku.Rescue;

/*
    スコア（縁）と福の連なり C をまとめて管理するクラス。シーンに1つだけ置く（#22 / #31 / #55 / #61）

    #31: スコアの変化を C# のイベントで配る。HUD・効果音・ご加護（#29）は
         毎フレーム見に行かないで、これらのイベントを受け取ってつなぐ

    #55: 優先救済（二重円）のボーナスを足す。ボーナスの数値は付録B B-2 を写した
         ScoreBonusTable（入っていなければ下の予備の値）から取る

    #61: 企画書 v8 7章「縁の計算式（通常弾）」にそろえた
      救済得点 = round((基礎点 + 最終弾の命中精度加点 + 優先救済加点) × 救済時の福の連なり倍率 × 発射時のご加護倍率)
      伝播得点 = round(20 × 救済時に保存した福の連なり倍率 × 救済時に保存したご加護倍率)
      ・計算は EnFormula。小数をぜんぶ掛けた最後に1回だけ四捨五入する
      ・福の連なり C は救済完了でだけ +1 して、+1 したあとの C の倍率（3/6/10 で ×1.10/×1.20/×1.30）を使う（FukuChain）
      ・とちゅうの当たり（欲張り客の1発目）は C を保って 5秒タイマーだけもどす。縁は 0 点
      ・誤投擲（色ちがい）・黒客への通常弾・正しい色を5秒当てない、で C を 0 にもどす。地面への外れでは切らない
      ・ランク（神社の評価）は倍率に入れない（付録B「ランク すべて×1.0」）。前にあった「評価による縁の倍率」はなくした
      ・3:00 のあと受理済みの弾がぜんぶ落ちたら、GameSession が LockScore() を呼ぶ。固定したあとは何も足さない
*/
public class ScoreManager : MonoBehaviour
{
    public static ScoreManager Instance { get; private set; }

    [Header("命中の基礎スコア（縁）")]
    [Tooltip("旧APIのフォールバック基礎点。#54 以降の正規の基礎点は客種ごと（CustomerKindTable / 付録B B-1）で、" +
             "救済完了時に CustomerState から渡される。")]
    [FormerlySerializedAs("outerScore")]
    [SerializeField] int hitScore = 100;

    [Header("加点の数値表（付録B B-2）")]
    [Tooltip("加点・福の連なり・伝播の数値表（付録B B-2 の写し）。割り当てるとこの表の値が下のフォールバックより優先される。" +
             "T2 で優先救済を +50 → +30 へ下げるときは、この表の数字だけを直す。")]
    [SerializeField] ScoreBonusTable bonusTable;

    [Header("フォールバック：命中精度ボーナス（#60: 判定半径に対する中心からの距離）")]
    [Tooltip("中心 40% 以内")]
    [SerializeField] int centerBonus = 50;
    [Tooltip("40〜70%")]
    [SerializeField] int innerBonus  = 20;
    [Tooltip("70〜100%")]
    [SerializeField] int outerBonus  = 0;

    [Header("フォールバック：優先救済ボーナス（#55 / 付録B B-2）")]
    [Tooltip("発射時に保存した二重円の客を、その弾で救済完了させたときの加点。数値表が未割り当てのときだけ使う。")]
    [SerializeField] int priorityRescueBonus = 50;

    /*
        ---------- 外に配るイベント（#31） ----------
        縁が変わった（引数: 今の縁の合計）
    */
    public event Action<int> onEnChanged;
    // 福の連なり C が変わった（引数: 今の C）
    public event Action<int> onComboChanged;
    // 合計の倍率が変わった（引数: 福の連なり × ご加護 の合計の倍率）
    public event Action<float> onMultiplierChanged;
    // 誤投擲（相性✗）か黒客への通常弾で、福の連なりが切れた。コンボが切れる演出や効果音用
    public event Action onMiss;
    // ResetAll が呼ばれた（リトライ用。#32 のゲーム管理が受け取る）
    public event Action onReset;
    // 笑顔の伝播で縁が入った（引数: 入った点）（#61 / #56）
    public event Action<int> onPropagationScored;
    // スコアを固定した（引数: 固定した縁）。3:00 のあと受理済みの弾がぜんぶ落ちたとき（#61）
    public event Action<int> onScoreLocked;

    readonly FukuChainCounter _chain = new FukuChainCounter();

    // 合計のスコア（縁）。減らないで増えつづける
    public int En { get; private set; }
    // 今の福の連なり C
    public int Combo => _chain.Count;
    // このプレイ中のいちばん大きい福の連なり（リザルト用）
    public int MaxCombo => _chain.Max;

    // いちばん新しい命中ゾーン（HUD の表示・確認用）
    public HitZone LastZone { get; private set; }
    // いちばん新しくもらった点（救済得点か伝播得点。HUD の表示・確認用）
    public int LastGain { get; private set; }
    // いちばん新しい命中精度のボーナス（倍率をかける前。HUD の表示・確認用）
    public int LastBonus { get; private set; }
    // いちばん新しい優先救済のボーナス（倍率をかける前。0 なら二重円の客じゃなかった。HUD の表示・確認用）
    public int LastPriorityBonus { get; private set; }
    // いちばん新しい救済で確定した2つの倍率（7章「倍率の保存順」）。救済客からの伝播得点に使う
    public EnMultiplierSnapshot LastRescueSnapshot { get; private set; }

    // スコアを固定したか（3:00 の解決が終わった）。固定したあとは縁も C も動かない
    public bool IsLocked { get; private set; }

    // このプレイで救済を完了した人数（#65 の3:00境界ログ「決着前後の救済数」）
    public int RescueCount { get; private set; }

    // 優先救済（二重円）のボーナス（付録B B-2）。数値の表が入っていればその値
    public int PriorityRescueBonus => bonusTable != null ? bonusTable.PriorityRescueBonus : priorityRescueBonus;
    // 笑顔の伝播1人ぶんの縁（付録B PROPAGATE）
    public int PropagationPoints => bonusTable != null ? bonusTable.PropagationPoints : EnFormula.DefaultPropagationPoints;
    // 1回の救済から伝播できる人数の上限（付録B PROPAGATE。#56）
    public int PropagationMaxTargets =>
        bonusTable != null ? bonusTable.PropagationMaxTargets : SmilePropagation.DefaultMaxTargets;
    // 1回の救済で伝播から入りうる縁の上限（+20 × 4人 = +80）。遠方客の「基礎200 ＋ 伝播最大 +80」の後ろ半分
    public int MaxPropagationEnPerRescue => PropagationPoints * PropagationMaxTargets;
    // 正しい色を当てないまま C が 0 にもどるまでの秒数（付録B CHAIN.TIMEOUT）
    public float ChainTimeoutSeconds => bonusTable != null ? bonusTable.ChainTimeoutSeconds : FukuChain.DefaultTimeoutSeconds;

    // 今の福の連なり倍率（C の段で決まる）
    public float Multiplier => ChainMultiplierOf(Combo);

    // ご加護タイム（#29）の倍率。GokagoTime が設定する。ふつうは1。弾は発射したときにこの値を保存する
    public float GokagoMultiplier { get; private set; } = 1f;

    // 今の合計の倍率（福の連なり × ご加護）。ランクは入らない
    public float TotalMultiplier => Multiplier * GokagoMultiplier;

    // 時計（テストで差しかえる用）。null なら GameSession の時計（#65。ポーズ・通信の復帰中は止まる）、GameSession がなければ Time.time
    public Func<float> Clock { get; set; }

    float Now => Clock != null ? Clock() : (GameSession.Instance != null ? GameSession.Instance.ElapsedSeconds : Time.time);

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Update()
    {
        TickChainTimeout(Now);
    }

    /*
        正しい色の5秒タイマーを進める（ふつうは Update から自動。テストからわざと呼ぶこともできる）
        C を 0 にもどしたら true
    */
    public bool TickChainTimeout(float now)
    {
        if (IsLocked) return false;

        int before = _chain.Count;
        if (!_chain.TickTimeout(now, ChainTimeoutSeconds)) return false;

        Debug.Log($"[Score] 福の連なり {before} → 0（正しい色を {ChainTimeoutSeconds:0.#}秒当てなかった）");
        NotifyChainChanged();
        return true;
    }

    /*
        正しい色で当たったのを記録する（#54 / #61）

        企画書 v8 7章、付録B B-2: 欲張り客のとちゅうの当たりは 0 点で、C も増やさない（5秒タイマーだけもどす）
        救えた（R=0）ときだけ C を +1 して、救済得点を1回で決める
        zone: 命中精度のゾーン（Miss ならミスあつかい）
        rescued: この当たりで救えたか（R=0 になったか）
        rescueBaseScore: 救えたときの基礎点（客の種類ごと。付録B B-1）
        priorityRescue: 優先救済か（#55）。発射（SwingAccepted）したときに弾に保存した二重円の客を、その弾で救えたときだけ true
        blessingMultiplier: 発射したときに弾に保存したご加護倍率（#61）。null なら今のご加護倍率
    */
    public void RegisterCorrectHit(HitZone zone, bool rescued, int rescueBaseScore, bool priorityRescue = false,
        float? blessingMultiplier = null)
    {
        if (IsLocked) return;

        // Miss が来たら当たりにしない（連なりが切れるほうへ）
        if (zone == HitZone.Miss) { RegisterMiss(); return; }

        float now = Now;
        LastZone = zone;

        if (!rescued)
        {
            // とちゅうの当たり: 縁は 0、C は保つ、5秒タイマーだけもどす（7章「福の連なりの判定」）
            _chain.KeepAlive(now);
            LastGain = 0;
            LastBonus = 0;
            LastPriorityBonus = 0;
            Debug.Log($"[Score] 正色命中（救済途中）: 縁は入らない (連なり {Combo} を維持 / 縁 {En})");
            return;
        }

        // 倍率の保存順（7章）: C を +1 してから、その段の倍率を確定する
        _chain.AddRescue(now);
        RescueCount++;
        float chainMultiplier = Multiplier;
        float blessing = Mathf.Max(1f, blessingMultiplier ?? GokagoMultiplier);

        int bonus = AccuracyBonusOf(zone);
        int priorityBonus = priorityRescue ? PriorityRescueBonus : 0;
        int gained = EnFormula.RescueScore(rescueBaseScore, bonus, priorityBonus, chainMultiplier, blessing);
        En += gained;

        LastGain = gained;
        LastBonus = bonus;
        LastPriorityBonus = priorityBonus;
        // 救済得点を足した直後に2つの倍率を保存して、あとから起きる伝播得点にも使う
        LastRescueSnapshot = new EnMultiplierSnapshot(chainMultiplier, blessing);

        Debug.Log($"[Score] 救済完了 {zone} : (基礎 {rescueBaseScore} + 精度 {bonus}" +
                  (priorityBonus > 0 ? $" + 優先救済 {priorityBonus}" : "") +
                  $") x連なり{chainMultiplier:0.00} xご加護{blessing:0.00} = +{gained}  (連なり {Combo} / 縁 {En})");

        NotifyChainChanged();
        onEnChanged?.Invoke(En);
    }

    /*
        古いメソッド（#22）。1発で救える客のときだけ正しい。#54 からは
        RegisterCorrectHit(HitZone,bool,int) を使って、基礎点は客の種類ごとの値を渡すこと
    */
    public void RegisterHit(HitZone zone) => RegisterCorrectHit(zone, rescued: true, rescueBaseScore: hitScore);

    // 誤投擲（相性の合わないお守り）か、黒客への通常弾を記録する。福の連なりが切れる
    public void RegisterMiss()
    {
        if (IsLocked) return;

        int before = _chain.Count;
        if (_chain.Break())
            Debug.Log($"[Score] 福の連なりが切れた (was {before})");

        /*
            「渋る」リアクション（#14）は客ごとの CustomerReluctance が CustomerRescue.onBadHit を
            受け取って再生する（まちがい＝相性✗で当たったときだけ）
        */
        NotifyChainChanged();
        onMiss?.Invoke(); // ミスの効果音やコンボが切れる演出（HUD の点滅など）はここを受け取る
    }

    /*
        地面に落ちた（どの客にも当たらなかった）のを記録する（#61）
        7章で C を切るのは「誤投擲（色ちがい）」「黒客への通常弾」「5秒無命中」だけ。外れだけでは切らない
        （外してばかりなら5秒タイマーで切れる）
    */
    public void RegisterGroundMiss()
    {
        if (IsLocked) return;
        LastZone = HitZone.Miss;
        LastGain = 0;
        LastBonus = 0;
        LastPriorityBonus = 0;
    }

    /*
        笑顔の伝播1回ぶんの縁を足す（#61。伝播する相手を決めるのは #56）
        snapshot: 伝播のもとになった救済で保存した倍率（OmamoriHitInfo.RescueSnapshot / LastRescueSnapshot）
        3:00 以後の接触では入れない（7章「伝播は接触時刻が180.000秒未満のものだけ有効」）
        返す値: 入った点。入らなかったら 0
    */
    public int RegisterPropagation(EnMultiplierSnapshot snapshot) => RegisterPropagation(snapshot, double.NaN);

    /*
        #65: 接触した時刻を渡す版。contactSessionSeconds は GameSession の時計の秒（NaN なら今）
        接触が 180.000秒未満なら、判定が次のフレームにずれても入れる。180.000秒以上なら入れない
    */
    public int RegisterPropagation(EnMultiplierSnapshot snapshot, double contactSessionSeconds)
    {
        if (IsLocked || !snapshot.IsValid) return 0;

        GameSession session = GameSession.Instance;
        if (session != null)
        {
            double contact = double.IsNaN(contactSessionSeconds) ? session.ElapsedTime : contactSessionSeconds;
            if (!session.AcceptsPropagationAt(contact)) return 0;
        }

        int gained = EnFormula.PropagationScore(snapshot.ChainMultiplier, snapshot.BlessingMultiplier, PropagationPoints);
        En += gained;
        LastGain = gained;
        LastBonus = 0;
        LastPriorityBonus = 0;

        Debug.Log($"[Score] 笑顔の伝播 : {PropagationPoints} x連なり{snapshot.ChainMultiplier:0.00} xご加護{snapshot.BlessingMultiplier:0.00} = +{gained}  (縁 {En})");

        onPropagationScored?.Invoke(gained);
        onEnChanged?.Invoke(En);
        return gained;
    }

    // ご加護タイム（#29）から呼ぶ。上乗せする倍率を設定したり、やめたりする
    public void SetGokagoMultiplier(float multiplier)
    {
        GokagoMultiplier = Mathf.Max(1f, multiplier);
        onMultiplierChanged?.Invoke(TotalMultiplier);
    }

    /*
        スコアを固定する（#61）。3:00 のあと、受理済みの弾がぜんぶ落ちたときに GameSession が呼ぶ
        固定したあとは救済・伝播・ミス・5秒タイマーのどれでも縁と C を動かさない
    */
    public void LockScore()
    {
        if (IsLocked) return;
        IsLocked = true;
        Debug.Log($"[Score] スコア固定 : 縁 {En} / 最大の福の連なり {MaxCombo}");
        onScoreLocked?.Invoke(En);
    }

    // スコアと福の連なりを最初にもどす（テスト・リトライ用）。固定も外す
    public void ResetAll()
    {
        En = 0;
        _chain.Reset();
        RescueCount = 0;
        LastZone = HitZone.Miss;
        LastGain = 0;
        LastBonus = 0;
        LastPriorityBonus = 0;
        LastRescueSnapshot = EnMultiplierSnapshot.None;
        GokagoMultiplier = 1f;
        IsLocked = false;
        Debug.Log("[Score] Reset");

        onEnChanged?.Invoke(En);
        onComboChanged?.Invoke(Combo);
        onMultiplierChanged?.Invoke(TotalMultiplier);
        onReset?.Invoke();
    }

    // 命中ゾーンごとの命中精度のボーナス（#60 / 付録B B-2）。数値の表があればその値を使う
    public int AccuracyBonusOf(HitZone zone)
    {
        if (bonusTable != null) return bonusTable.AccuracyBonusOf(zone);

        switch (zone)
        {
            case HitZone.Center: return centerBonus;
            case HitZone.Inner:  return innerBonus;
            case HitZone.Outer:  return outerBonus;
            default:             return 0;
        }
    }

    // C から福の連なり倍率を出す（付録B B-2）。数値の表があればその値を使う
    public float ChainMultiplierOf(int chain)
    {
        return bonusTable != null ? bonusTable.ChainMultiplierOf(chain) : FukuChain.MultiplierOf(chain);
    }

    void NotifyChainChanged()
    {
        onComboChanged?.Invoke(Combo);
        onMultiplierChanged?.Invoke(TotalMultiplier);
    }
}
