using System;
using System.Text;

namespace Toufuku.Playtest
{
    /*
        参加者1人ぶんの記録（#49 / 企画書 v8 17章・19章）

        取る項目は5つ
        ・自分から振った回数（45秒で何回振ったか。言われずに振った回数なので、参加者には見せない）
        ・表情や声（やる人のメモ）
        ・手ごたえの5段階（案ごと）
        ・「すぐもう一回振りたい」の2択（案ごと）
        ・どっちの案がいいか、むりやり2択で選んでもらったのと、その理由
        合格かどうかに関係する「タイミングずれの指摘」と、安全（痛い・こわい・ストラップが外れた）も残す
        MonoBehaviour は使っていない
    */
    [Serializable]
    public class AbParticipantRecord
    {
        public const int MinFeel = 1;
        public const int MaxFeel = 5;

        // テストID（19章のテスト記録と照らし合わせる）
        public string testId = "T0-AB-1";
        // やった日（yyyy-MM-dd）
        public string date = "";
        // 責任者
        public string owner = "";
        // 参加者の番号（1から）。奇数は A→B、偶数は B→A
        public int participantNo = 1;
        public AbOrder order = AbOrder.AB;

        // 45秒で自分から振った回数（有効スイング）
        public int throwsA, throwsB;
        // クールダウン中に振った回数（参考の値。「もう一回振りたい」を行動から見るヒント）
        public int rejectedA, rejectedB;

        // 手ごたえの5段階（1〜5）。0 はまだ答えていない
        public int feelA, feelB;
        // 「すぐもう一回振りたい」の2択
        public bool againA, againB;
        // 2択に答えたかどうか（まだ答えていないのと「いいえ」を分けるため）
        public bool againAnsweredA, againAnsweredB;

        // 選んだ案（むりやり2択）
        public VariantId adopted = VariantId.A;
        public bool adoptedAnswered;
        // 選んだ理由（言った言葉のまま残す）
        public string reason = "";

        // 映像・音・振動のタイミングがずれていると言ったかどうか
        public bool syncComplaint;

        // 安全（1つでもあったら、その場でやめて原因を直す）
        public bool pain, fear, strapDeviation;

        // 表情や声のメモ
        public string note = "";

        public int ThrowsOf(VariantId id) => id == VariantId.A ? throwsA : throwsB;
        public int RejectedOf(VariantId id) => id == VariantId.A ? rejectedA : rejectedB;
        public int FeelOf(VariantId id) => id == VariantId.A ? feelA : feelB;
        public bool AgainOf(VariantId id) => id == VariantId.A ? againA : againB;
        public bool AgainAnsweredOf(VariantId id) => id == VariantId.A ? againAnsweredA : againAnsweredB;

        // 選んだ案の手ごたえ
        public int AdoptedFeel => FeelOf(adopted);
        // 選んだ案の「すぐもう一回振りたい」
        public bool AdoptedAgain => AgainOf(adopted);
        // 選んだ案の手ごたえが4以上かどうか
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

        // 合格かどうかの集計に入れていい記録かどうか
        public bool IsComplete =>
            participantNo > 0 &&
            IsValidFeel(feelA) && IsValidFeel(feelB) &&
            againAnsweredA && againAnsweredB &&
            adoptedAnswered;

        // 足りない項目（保存ボタンのとなりに出す）。ぜんぶそろっていれば空の文字
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
