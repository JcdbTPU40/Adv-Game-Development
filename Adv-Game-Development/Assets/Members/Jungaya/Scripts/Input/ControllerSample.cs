using System.Globalization;

namespace Toufuku.GameInput
{
    /*
        ESP32 から1行ぶん受け取ったデータ（#51）

        送られてくる形: yaw,pitch,roll[,buttons[,millis]]（改行で区切る。小数点はピリオド）
        ・buttons はなくてもいい（今のファームウェアは3つしか送ってこない）
        ・buttons は10進数のビットの集まり。bit0〜4 が色ボタン（OmamoriType の順番）、bit5 が正面ボタン
        ・millis（#63）はコントローラー側でデータを取った時刻（ミリ秒、millis()）。計測ログの「入力時刻」になる。なくてもいい
    */
    public readonly struct ControllerSample
    {
        public const int FrontButtonBit = 5;

        public readonly float Yaw;
        public readonly float Pitch;
        public readonly float Roll;
        public readonly int Buttons;
        public readonly bool HasButtons;
        // Unity 側で受け取った時刻（Time.realtimeSinceStartupAsDouble）
        public readonly double Time;
        // #63: コントローラー側でデータを取った時刻（秒）。5つ目がなければ NaN。Unity の時刻とはスタート地点がちがう
        public readonly double DeviceTime;

        public ControllerSample(float yaw, float pitch, float roll, int buttons, bool hasButtons, double time, double deviceTime = double.NaN)
        {
            Yaw = yaw;
            Pitch = pitch;
            Roll = roll;
            Buttons = buttons;
            HasButtons = hasButtons;
            Time = time;
            DeviceTime = deviceTime;
        }

        public bool IsColorHeld(int index) => HasButtons && index >= 0 && index < FrontButtonBit && (Buttons & (1 << index)) != 0;

        public bool IsFrontHeld => HasButtons && (Buttons & (1 << FrontButtonBit)) != 0;

        public bool HasDeviceTime => !double.IsNaN(DeviceTime);

        // 1行を読み取る。数字が3〜5個ならんでいる行だけを受け付ける
        public static bool TryParse(string line, double time, out ControllerSample sample)
        {
            sample = default;
            if (string.IsNullOrEmpty(line)) return false;

            string[] data = line.Trim().Split(',');
            if (data.Length < 3 || data.Length > 5) return false;

            if (!float.TryParse(data[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float yaw) ||
                !float.TryParse(data[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float pitch) ||
                !float.TryParse(data[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float roll))
                return false;

            int buttons = 0;
            bool hasButtons = data.Length >= 4;
            if (hasButtons && !int.TryParse(data[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out buttons))
                return false;

            double deviceTime = double.NaN;
            if (data.Length == 5)
            {
                if (!double.TryParse(data[4], NumberStyles.Float, CultureInfo.InvariantCulture, out double millis))
                    return false;
                deviceTime = millis / 1000.0;
            }

            sample = new ControllerSample(yaw, pitch, roll, buttons, hasButtons, time, deviceTime);
            return true;
        }
    }
}
