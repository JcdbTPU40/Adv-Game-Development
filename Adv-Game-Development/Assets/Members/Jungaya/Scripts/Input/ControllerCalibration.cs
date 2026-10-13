using System;

namespace Toufuku.GameInput
{
    /*
        正面ボタン長押しで合わせた「正面のヨー角」を、シーンをまたいで覚えておく場所（メインメニュー）

        ・メインメニューで正面を合わせたら、プレイのシーンの ThrowInputController がその値から始める
        ・プレイ中に正面を合わせなおしたときも、ここに書く（リザルト → タイトル → 次のプレイで引きつぐ）
        ・アプリを起動している間だけ覚える。BNO055 のヨー角は電源を入れなおすと基準が変わるので、PlayerPrefs には残さない
    */
    public static class ControllerCalibration
    {
        // 一度でも正面を合わせたか
        public static bool HasYawOffset { get; private set; }

        // 合わせた正面のヨー角（入力そのままの値・度）
        public static float YawOffset { get; private set; }

        // 正面を合わせた時刻（Time.realtimeSinceStartup・秒）。合わせていなければ NaN
        public static double CalibratedAt { get; private set; } = double.NaN;

        // 正面が変わった（引数: 新しい正面のヨー角）
        public static event Action<float> YawOffsetChanged;

        public static void SetYawOffset(float yaw, double time)
        {
            YawOffset = yaw;
            HasYawOffset = true;
            CalibratedAt = time;
            YawOffsetChanged?.Invoke(yaw);
        }

        // 合わせていなければ fallback を返す
        public static float YawOffsetOr(float fallback) => HasYawOffset ? YawOffset : fallback;

        // テスト用
        public static void Clear()
        {
            HasYawOffset = false;
            YawOffset = 0f;
            CalibratedAt = double.NaN;
        }
    }
}
