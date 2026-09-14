using UnityEngine;

namespace Toufuku.Rescue
{
    /// <summary>
    /// 足元の円の見せ方（表示条件・濃さ・脈打ち）— Issue #55（付録B UI.DANGER／PRIORITY.MARK）
    ///
    /// | 円 | 出る条件 | 根拠 |
    /// |---|---|---|
    /// | 危険円（薄い赤） | D≥50。D=85 から脈打つ | 付録B UI.DANGER：足元円の表示開始 D=50 ／ 脈打ち開始 D=85 |
    /// | 二重円（優先救済） | その時の優先対象 1 人。<b>D&lt;50 でも出す</b> | 付録B PRIORITY.MARK：対象数 1 人／D 閾値なし |
    ///
    /// 二重円に閾値を持たせないので「D を上げてから救うと +50 が付く」という待ちの利益が生まれない（v8 7章）。
    /// MonoBehaviour 非依存。表示条件はエディタテストで検証する（DangerRingDisplayTests）。
    /// </summary>
    public static class DangerRingDisplay
    {
        /// <summary>危険円が出始める D（付録B UI.DANGER）。</summary>
        public const float ShowDanger = 50f;
        /// <summary>危険円が脈打ち始める D（付録B UI.DANGER）。</summary>
        public const float PulseDanger = 85f;

        /// <summary>
        /// 危険円を出すか。D≥50 で、まだ救済対象（active・未救済・非黒客・R&gt;0）の客だけ。
        /// 入場中・退場中の救済客・黒客には出さない（v8 6章）。
        /// </summary>
        public static bool ShowsDangerRing(float danger, bool isRescueTarget)
        {
            return isRescueTarget && danger >= ShowDanger;
        }

        /// <summary>
        /// 二重円を出すか。優先対象の 1 人なら <b>D に関係なく</b> 出す（付録B PRIORITY.MARK：D 閾値なし）。
        /// </summary>
        public static bool ShowsPriorityRing(bool isPriorityTarget, bool isRescueTarget)
        {
            return isPriorityTarget && isRescueTarget;
        }

        /// <summary>危険円の濃さ（0〜1）。D=50 で 0、D=100 で 1。50 未満は 0。</summary>
        public static float DangerAmount01(float danger)
        {
            if (danger <= ShowDanger) return 0f;
            if (danger >= CustomerStateMachine.MaxDanger) return 1f;
            return (danger - ShowDanger) / (CustomerStateMachine.MaxDanger - ShowDanger);
        }

        /// <summary>脈打つか（D≥85）。</summary>
        public static bool Pulses(float danger) => danger >= PulseDanger;

        /// <summary>
        /// 脈打ちの位相（0〜1 を往復）。<paramref name="pulsesPerSecond"/> 回/秒で 1 周する。
        /// 脈打たない D では常に 1（＝縮まない）。
        /// </summary>
        public static float PulseAmount01(float danger, float time, float pulsesPerSecond)
        {
            if (!Pulses(danger) || pulsesPerSecond <= 0f) return 1f;
            return 0.5f + 0.5f * Mathf.Sin(time * pulsesPerSecond * Mathf.PI * 2f);
        }
    }
}
