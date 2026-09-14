using System.Globalization;

namespace Toufuku.GameInput
{
    /// <summary>
    /// ESP32 から 1 行ぶん受信したサンプル — Issue #51
    ///
    /// 通信形式: <c>yaw,pitch,roll[,buttons]</c>（改行区切り・小数点はピリオド）
    /// ・buttons は省略可（現行ファームウェアは 3 項目のみ）。
    /// ・buttons は 10 進のビットマスク: bit0〜4 = 色ボタン（OmamoriType の並び）、bit5 = 正面ボタン。
    /// </summary>
    public readonly struct ControllerSample
    {
        public const int FrontButtonBit = 5;

        public readonly float Yaw;
        public readonly float Pitch;
        public readonly float Roll;
        public readonly int Buttons;
        public readonly bool HasButtons;
        /// <summary>Unity 側で受信した時刻（Time.realtimeSinceStartupAsDouble）。</summary>
        public readonly double Time;

        public ControllerSample(float yaw, float pitch, float roll, int buttons, bool hasButtons, double time)
        {
            Yaw = yaw;
            Pitch = pitch;
            Roll = roll;
            Buttons = buttons;
            HasButtons = hasButtons;
            Time = time;
        }

        public bool IsColorHeld(int index) => HasButtons && index >= 0 && index < FrontButtonBit && (Buttons & (1 << index)) != 0;

        public bool IsFrontHeld => HasButtons && (Buttons & (1 << FrontButtonBit)) != 0;

        /// <summary>1 行を解釈する。3 項目または 4 項目の数値行だけを受け付ける。</summary>
        public static bool TryParse(string line, double time, out ControllerSample sample)
        {
            sample = default;
            if (string.IsNullOrEmpty(line)) return false;

            string[] data = line.Trim().Split(',');
            if (data.Length != 3 && data.Length != 4) return false;

            if (!float.TryParse(data[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float yaw) ||
                !float.TryParse(data[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float pitch) ||
                !float.TryParse(data[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float roll))
                return false;

            int buttons = 0;
            bool hasButtons = data.Length == 4;
            if (hasButtons && !int.TryParse(data[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out buttons))
                return false;

            sample = new ControllerSample(yaw, pitch, roll, buttons, hasButtons, time);
            return true;
        }
    }
}
