using System;
using System.Text;

namespace Toufuku.Playtest
{
    /// <summary>
    /// 参加者 1 人分の記録 — Issue #49（仕様書 v8 17章・19章）
    ///
    /// 取る項目は 5 つ。
    /// ・自発投数（45 秒で何回振ったか。言われずに振った回数なので参加者には見せない）
    /// ・表情・声（実施者のメモ）
    /// ・手応え 5 段階（案ごと）
    /// ・「すぐもう一度振りたい」二択（案ごと）
    /// ・採用案の強制二択と理由
    /// あわせて合否に効く「同期ずれの指摘」と安全（痛み・恐怖・ストラップ逸脱）も残す。
    /// MonoBehaviour 非依存。
    /// </summary>
    [Serializable]
    public class AbParticipantRecord
    {
        public const int MinFeel = 1;
        public const int MaxFeel = 5;

        /// <summary>テストID（19章のテスト記録と突き合わせる）。</summary>
        public string testId = "T0-AB-1";
        /// <summary>実施日（yyyy-MM-dd）。</summary>
        public string date = "";
        /// <summary>責任者。</summary>
        public string owner = "";
        /// <summary>参加者番号（1 始まり）。奇数が A→B、偶数が B→A。</summary>
        public int participantNo = 1;
        public AbOrder order = AbOrder.AB;

        /// <summary>45 秒の自発投数（有効スイング）。</summary>
        public int throwsA, throwsB;
        /// <summary>クールダウン中に振った回数（参考値。「もう一度振りたい」の行動側の手がかり）。</summary>
        public int rejectedA, rejectedB;

        /// <summary>手応え 5 段階（1〜5）。0 は未回答。</summary>
        public int feelA, feelB;
        /// <summary>「すぐもう一度振りたい」二択。</summary>
        public bool againA, againB;
        /// <summary>二択に答えたか（未回答と「いいえ」を区別する）。</summary>
        public bool againAnsweredA, againAnsweredB;

        /// <summary>採用案（強制二択）。</summary>
        public VariantId adopted = VariantId.A;
        public bool adoptedAnswered;
        /// <summary>採用の理由（そのままの言葉で残す）。</summary>
        public string reason = "";

        /// <summary>映像・音・振動の同期ずれを指摘したか。</summary>
        public bool syncComplaint;

        /// <summary>安全（1 件でもあればその場で中止して原因を直す）。</summary>
        public bool pain, fear, strapDeviation;

        /// <summary>表情・声のメモ。</summary>
        public string note = "";

        public int ThrowsOf(VariantId id) => id == VariantId.A ? throwsA : throwsB;
        public int RejectedOf(VariantId id) => id == VariantId.A ? rejectedA : rejectedB;
        public int FeelOf(VariantId id) => id == VariantId.A ? feelA : feelB;
        public bool AgainOf(VariantId id) => id == VariantId.A ? againA : againB;
        public bool AgainAnsweredOf(VariantId id) => id == VariantId.A ? againAnsweredA : againAnsweredB;

        /// <summary>採用案の手応え。</summary>
        public int AdoptedFeel => FeelOf(adopted);
        /// <summary>採用案の「すぐもう一度振りたい」。</summary>
        public bool AdoptedAgain => AgainOf(adopted);
        /// <summary>採用案の手応えが 4 以上か。</summary>
        public bool AdoptedFeelOk => AdoptedFeel >= AbTestPlan.MinFeel;
        public bool HasSafetyIncident => pain || fear || strapDeviation;

        public void SetThrows(VariantId id, int accepted, int rejected)
        {
            if (id == VariantId.A) { throwsA = accepted; rejectedA = rejected; }
            else { throwsB = accepted; rejectedB = rejected; }
        }

        public void SetFeel(VariantId id, int feel)
        {
            feel = Clamp(feel);
            if (id == VariantId.A) feelA = feel; else feelB = feel;
        }

        public void SetAgain(VariantId id, bool again)
        {
            if (id == VariantId.A) { againA = again; againAnsweredA = true; }
            else { againB = again; againAnsweredB = true; }
        }

        public void SetAdopted(VariantId id)
        {
            adopted = id;
            adoptedAnswered = true;
        }

        /// <summary>合否の集計に入れられる記録か。</summary>
        public bool IsComplete =>
            participantNo > 0 &&
            IsValidFeel(feelA) && IsValidFeel(feelB) &&
            againAnsweredA && againAnsweredB &&
            adoptedAnswered;

        /// <summary>足りない項目（保存ボタンの横に出す）。すべてそろっていれば空文字。</summary>
        public string MissingFields()
        {
            var sb = new StringBuilder();
            if (participantNo <= 0) Append(sb, "参加者番号");
            if (!IsValidFeel(feelA)) Append(sb, "案A手応え");
            if (!IsValidFeel(feelB)) Append(sb, "案B手応え");
            if (!againAnsweredA) Append(sb, "案Aもう一度");
            if (!againAnsweredB) Append(sb, "案Bもう一度");
            if (!adoptedAnswered) Append(sb, "採用案");
            return sb.ToString();
        }

        public AbParticipantRecord Clone() => (AbParticipantRecord)MemberwiseClone();

        public static bool IsValidFeel(int feel) => feel >= MinFeel && feel <= MaxFeel;

        static int Clamp(int feel) => feel < MinFeel ? MinFeel : feel > MaxFeel ? MaxFeel : feel;

        static void Append(StringBuilder sb, string label)
        {
            if (sb.Length > 0) sb.Append('・');
            sb.Append(label);
        }
    }
}
