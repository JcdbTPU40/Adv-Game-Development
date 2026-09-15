using System;
using UnityEngine;
using Toufuku.Playtest;
using Toufuku.Rescue;
using Toufuku.Rescue.Mock;

// 客に当たったのを判定し終わった結果（#64 の命中音・救済音・振動用）
public readonly struct OmamoriHitInfo
{
    // 当たった客
    public readonly GameObject Customer;
    public readonly OmamoriType Type;
    // スコアに渡したゾーン。相性✗なら Miss
    public readonly HitZone Zone;
    // 黒客に当たったかどうか
    public readonly bool IsBlackCustomer;
    // この当たりで救えたかどうか（ゲージが 0 になった）
    public readonly bool Rescued;
    // 優先救済（二重円）のボーナスが入ったか（#55）。発射したときに保存した相手のIDの客を救えたときだけ true
    public readonly bool PriorityRescue;
    // スコアに入れたあとの福の連なり（ScoreManager.Combo）
    public readonly int Combo;
    // 着弾をスタートにする時刻（Time.realtimeSinceStartupAsDouble）。フィードバックの遅れの計測に使う
    public readonly double ImpactTime;
    /*
        この当たりで救えたときに確定した2つの倍率（#61 / 7章「倍率の保存順」）。救えていなければ IsValid = false
        この救済客からの笑顔の伝播（#56）は ScoreManager.RegisterPropagation にこの値を渡す
    */
    public readonly EnMultiplierSnapshot RescueSnapshot;

    // 相性◯で当たりとしてスコアに入ったかどうか
    public bool IsGoodHit => Zone != HitZone.Miss;

    public OmamoriHitInfo(GameObject customer, OmamoriType type, HitZone zone, bool isBlackCustomer, bool rescued, int combo, double impactTime,
        bool priorityRescue = false, EnMultiplierSnapshot rescueSnapshot = default)
    {
        Customer = customer;
        Type = type;
        Zone = zone;
        IsBlackCustomer = isBlackCustomer;
        Rescued = rescued;
        Combo = combo;
        ImpactTime = impactTime;
        PriorityRescue = priorityRescue;
        RescueSnapshot = rescueSnapshot;
    }
}

/*
    お守りが客に当たったときの共通の処理（救済の判定 → スコア）（#13 / #22 / #60 / #54）

    物理でぶつけて当てる OmamoriBullet と、着弾点で判定する OmamoriProjectile（#60）の両方から呼ぶ
    #64: 判定し終わった同じ呼び出しの中で HitResolved を呼ぶ（命中音と救済音を判定と同じ時刻に鳴らすため）
*/
public static class OmamoriHitResolver
{
    // 客に当たったのを判定し終わった（スコアに入れたあと）。もう結果が決まっている客に当たったときは呼ばれない
    public static event Action<OmamoriHitInfo> HitResolved;

    /*
        客にお守りが当たったのを処理する
        customer: 当たった客
        type: お守りの種類
        zone: 命中精度から決めたゾーン
        impactTime: 着弾をスタートにする時刻（realtimeSinceStartupAsDouble）。書かなければ呼んだ時刻
        priorityTargetId: 発射（SwingAccepted）したときにこの弾に保存した優先の相手のID（#55）。0 なら優先救済のボーナスはない
          飛んでいる間に二重円が動いても弾に保存した値は変えないので、ここに来るのは「発射したとき」の判断
        blessingMultiplier: 発射（SwingAccepted）したときにこの弾に保存したご加護倍率（#61）。null なら今のご加護倍率
        返す値: スコアに渡したゾーン。相性✗なら Miss。もう結果が決まっている客なら何も入れないで Miss
    */
    public static HitZone ApplyHit(GameObject customer, OmamoriType type, HitZone zone, double? impactTime = null,
        int priorityTargetId = PriorityRescue.NoTarget, float? blessingMultiplier = null)
    {
        double impact = impactTime ?? Time.realtimeSinceStartupAsDouble;
        bool rescued = false;
        bool isBlack = IsBlackCustomer(customer);

        CustomerState state = customer != null ? customer.GetComponent<CustomerState>() : null;

        /*
            黒客にふつうの弾が当たった（企画書 v8 6章）: D も R も動かない終わりの状態。縁は入らないで、
            福の連なりが切れるのと、評価 −15（7章 評価値の増減）のペナルティが起きる（当たり判定は残してあるので、ここに来るのは仕様どおり）
        */
        if (isBlack)
        {
            if (ScoreManager.Instance != null)
                ScoreManager.Instance.RegisterMiss();
            if (ShrineRating.Instance != null)
                ShrineRating.Instance.RegisterBlackShot();

            int blackCombo = ScoreManager.Instance != null ? ScoreManager.Instance.Combo : 0;
            HitResolved?.Invoke(new OmamoriHitInfo(customer, type, HitZone.Miss, true, false, blackCombo, impact));
            return HitZone.Miss;
        }

        // 救済の判定（#13）: お守りの種類を客に渡して、相性◯/✗と D・R の更新をやってもらう
        CustomerRescue rescue = customer != null ? customer.GetComponent<CustomerRescue>() : null;
        if (rescue != null)
        {
            /*
                もう救われた客にもう一回当たったときのための、念のためのガード
                過剰押し売り（#33 案B）は企画書 v3 §16【B】でボツになった。救えたときに当たり判定を
                消す仕様（CustomerState.DisableHitDetection）なので、ふつうはここには来ない
                来たときはコンポーネントの設定もれなので、スコアもミスも何も入れない
            */
            if (rescue.IsFinished)
            {
                Debug.LogWarning("[OmamoriHitResolver] 救済済みの客に命中しました（当たり判定の無効化漏れの疑い）", customer);
                return HitZone.Miss;
            }

            Affinity affinity = rescue.ApplyHit(type);

            /*
                まちがった色（相性✗）は Miss にして、福の連なり C とご加護の進み G を切る（v8 の変更点2）
                D も R も変わらないので、まちがった色を連打しても黒客になるのを1秒も遅らせられない
            */
            if (affinity == Affinity.Bad)
                zone = HitZone.Miss;

            // さっきまで active だったので、ここで救われていたら「この当たりで R が 0 になった」ということ
            rescued = state != null && state.IsRescued;
        }

        /*
            優先救済（#55）: 発射したときに保存した二重円の客を、この弾で救えたときだけ +50（付録B B-2）
            とちゅうの当たりや別の客を救ったときは入らない。IDをくらべるだけなので、飛んでいる間の表示の変化にはえいきょうされない
        */
        bool priorityRescue = rescued
            && priorityTargetId > PriorityRescue.NoTarget
            && PriorityRescue.IsBonusHit(priorityTargetId, CustomerSpawnId.Of(customer), true);

        EnMultiplierSnapshot snapshot = EnMultiplierSnapshot.None;
        if (ScoreManager.Instance != null)
        {
            // 縁は救えたときだけ入る。とちゅうの当たり（欲張り客の1発目など）は 0 点で、福の連なりも保つだけ（付録B B-2 / 7章）
            int baseScore = state != null ? state.RescueBaseScore : 0;
            ScoreManager.Instance.RegisterCorrectHit(zone, rescued, baseScore, priorityRescue, blessingMultiplier);
            if (rescued && zone != HitZone.Miss) snapshot = ScoreManager.Instance.LastRescueSnapshot;
        }

        /*
            笑顔の伝播（#56）: 救えた客に、この瞬間の倍率を持たせて帰らせる
            福の連なり倍率は RegisterCorrectHit が +1 したあとの値、ご加護倍率は発射したときの保存値（7章「倍率の保存順」）
            だれに伝わるかは、帰り道で実際にすれちがったときに SmileCarrier が決める（相手をここで予約はしない）
        */
        if (rescued && snapshot.IsValid) SmileCarrier.AttachTo(customer, snapshot);

        int combo = ScoreManager.Instance != null ? ScoreManager.Instance.Combo : 0;
        HitResolved?.Invoke(new OmamoriHitInfo(customer, type, zone, false, rescued, combo, impact, priorityRescue, snapshot));

        return zone;
    }

    /*
        客以外（地面など）に落ちた＝外れ（#61）
        7章で福の連なりを切るのは誤投擲（色ちがい）・黒客への通常弾・5秒無命中だけなので、外れだけでは切らない
    */
    public static void ApplyMiss()
    {
        if (ScoreManager.Instance != null)
            ScoreManager.Instance.RegisterGroundMiss();
    }

    /*
        黒客かどうか。本番の客は CustomerState（#54）の状態で判定して、
        CustomerState を持っていない視認性モック（#44）の客だけ名札（MockCustomerTag）で判定する
    */
    public static bool IsBlackCustomer(GameObject customer)
    {
        if (customer == null) return false;

        CustomerState state = customer.GetComponent<CustomerState>();
        if (state != null) return state.IsBlack;

        MockCustomerTag tag = customer.GetComponent<MockCustomerTag>();
        return tag != null && tag.IsBlack;
    }
}
