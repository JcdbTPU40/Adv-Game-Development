using UnityEngine;

namespace Toufuku.Rescue
{
    /*
        足元の円の見せ方（いつ出すか・濃さ・ドクドク）を決めるクラス（#55 / 付録B UI.DANGER・PRIORITY.MARK）

        円ごとの出る条件と理由:
        ・危険円（うすい赤）: D が 50 以上で出る。D=85 からドクドクする
          → 付録B UI.DANGER: 足元の円が出始めるのは D=50、ドクドクし始めるのは D=85
        ・二重円（優先救済）: そのときの優先の相手1人に出る。D が 50 より小さくても出す
          → 付録B PRIORITY.MARK: 相手は1人、D のしきい値なし

        二重円にしきい値を持たせないので、「D を上げてから救うと +50 が付く」という、待ったほうが得になることがない（v8 7章）
        MonoBehaviour は使っていない。出る条件はエディタのテストで確かめる（DangerRingDisplayTests）
    */
    public static class DangerRingDisplay
    {
        // 危険円が出始める D（付録B UI.DANGER）
        public const float ShowDanger = 50f;
        // 危険円がドクドクし始める D（付録B UI.DANGER）
        public const float PulseDanger = 85f;

        /*
            危険円を出すかどうか。D が 50 以上で、まだ救える対象（active・まだ救われていない・黒客じゃない・R>0）の客だけ
            入ってくる途中や、帰っている途中の救われた客や、黒客には出さない（v8 6章）
        */
        public static bool ShowsDangerRing(float danger, bool isRescueTarget)
        {
            return isRescueTarget && danger >= ShowDanger;
        }

        // 二重円を出すかどうか。優先の相手1人なら、D に関係なく出す（付録B PRIORITY.MARK: D のしきい値なし）
        public static bool ShowsPriorityRing(bool isPriorityTarget, bool isRescueTarget)
        {
            return isPriorityTarget && isRescueTarget;
        }

        // 危険円の濃さ（0〜1）。D=50 で 0、D=100 で 1。50 より小さいときは 0
        public static float DangerAmount01(float danger)
        {
            if (danger <= ShowDanger) return 0f;
            if (danger >= CustomerStateMachine.MaxDanger) return 1f;
            return (danger - ShowDanger) / (CustomerStateMachine.MaxDanger - ShowDanger);
        }

        // ドクドクするかどうか（D が 85 以上）
        public static bool Pulses(float danger) => danger >= PulseDanger;

        /*
            ドクドクの進み具合（0〜1 を行ったり来たり）。pulsesPerSecond 回/秒で1周する
            ドクドクしない D のときはいつも 1（＝ちぢまない）
        */
        public static float PulseAmount01(float danger, float time, float pulsesPerSecond)
        {
            if (!Pulses(danger) || pulsesPerSecond <= 0f) return 1f;
            return 0.5f + 0.5f * Mathf.Sin(time * pulsesPerSecond * Mathf.PI * 2f);
        }
    }
}
