using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Toufuku.Menu
{
    // 1つのステージの記録
    [Serializable]
    public class StageRecord
    {
        public string id;
        public bool cleared;
        public int bestScore;
    }

    // セーブ枠1つぶんの中身（JSON にそのまま書く）
    [Serializable]
    public class SaveData
    {
        public int version = 1;
        public string createdAt;
        public string lastPlayedAt;
        public string lastStageId;
        public List<StageRecord> stages = new List<StageRecord>();

        public StageRecord Find(string stageId)
        {
            foreach (StageRecord r in stages)
                if (r.id == stageId) return r;
            return null;
        }

        public bool IsCleared(string stageId) => Find(stageId)?.cleared ?? false;

        public int TotalScore
        {
            get
            {
                int sum = 0;
                foreach (StageRecord r in stages) sum += r.bestScore;
                return sum;
            }
        }

        public int ClearedCount
        {
            get
            {
                int n = 0;
                foreach (StageRecord r in stages) if (r.cleared) n++;
                return n;
            }
        }
    }

    /*
        セーブ枠（メインメニューの「絵馬を選ぶ」）

        ・枠は SlotCount こ。1枠 = persistentDataPath/Saves/slot{番号}.json
        ・メニューで選んだ枠を Current に持っておき、プレイのシーンから RecordResult で記録できるようにする
          （リザルトから記録する呼び出しは、まだどこにもつないでいない）
        ・壊れたファイルは読めなかったことにする（白紙あつかい。上書きはしない）
    */
    public static class SaveSlots
    {
        public const int SlotCount = 3;

        // 今えらばれている枠（0〜）。えらばれていなければ -1
        public static int CurrentSlot { get; private set; } = -1;
        public static SaveData Current { get; private set; }

        // メニューで決めた、これから遊ぶステージ
        public static StageInfo SelectedStage { get; set; }

        static string Directory => Path.Combine(Application.persistentDataPath, "Saves");
        static string PathOf(int slot) => Path.Combine(Directory, $"slot{slot + 1}.json");

        public static bool Exists(int slot) => File.Exists(PathOf(slot));

        // 読めなければ null
        public static SaveData Load(int slot)
        {
            string path = PathOf(slot);
            if (!File.Exists(path)) return null;
            try
            {
                SaveData data = JsonUtility.FromJson<SaveData>(File.ReadAllText(path));
                if (data != null && data.stages == null) data.stages = new List<StageRecord>();
                return data;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SaveSlots] 枠{slot + 1} を読めませんでした: {e.Message}");
                return null;
            }
        }

        // 枠をえらぶ。白紙なら新しく作って書く
        public static SaveData Select(int slot)
        {
            SaveData data = Load(slot);
            if (data == null)
            {
                data = new SaveData { createdAt = Now() };
                data.lastPlayedAt = data.createdAt;
                Write(slot, data);
            }
            CurrentSlot = slot;
            Current = data;
            return data;
        }

        // 今の枠に、ステージの結果を書く（最高の縁だけ残す）
        public static void RecordResult(string stageId, int score, bool cleared)
        {
            if (Current == null || CurrentSlot < 0 || string.IsNullOrEmpty(stageId)) return;
            StageRecord r = Current.Find(stageId);
            if (r == null)
            {
                r = new StageRecord { id = stageId };
                Current.stages.Add(r);
            }
            r.cleared |= cleared;
            r.bestScore = Mathf.Max(r.bestScore, score);
            Current.lastStageId = stageId;
            Current.lastPlayedAt = Now();
            Write(CurrentSlot, Current);
        }

        // ステージを始めるときに呼ぶ（最後に遊んだ日時とステージ）
        public static void MarkPlayed(string stageId)
        {
            if (Current == null || CurrentSlot < 0) return;
            Current.lastStageId = stageId;
            Current.lastPlayedAt = Now();
            Write(CurrentSlot, Current);
        }

        static void Write(int slot, SaveData data)
        {
            try
            {
                System.IO.Directory.CreateDirectory(Directory);
                File.WriteAllText(PathOf(slot), JsonUtility.ToJson(data, true));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SaveSlots] 枠{slot + 1} に書けませんでした: {e.Message}");
            }
        }

        static string Now() => DateTime.Now.ToString("yyyy-MM-dd HH:mm");
    }
}
