namespace Toufuku.Rescue.Outline
{
    /*
        天候（雨）が ON かどうかだけを持つクラス（#45 の計測 HUD と CSV 用）

        #59 で、雨や距離で輪郭を暗くする処理は消した
          企画書 v8 6章「天候中も輪郭の明度・色・パターンを維持する」、9章「採用しても輪郭の明度…を変えない」
          #45 のときは v4 のころの「雨天で発光が弱まる」を仮で入れていたけど、v8 ではまちがいになる
        なので RainAmount は「天候オーバーレイがのっているか」の目じるしで、輪郭の見た目には使わない
        計測 HUD は雨のときにまわりのライトだけを暗くして、輪郭の明るさが変わらないことを見られるようにしている
    */
    public static class OutlineWeather
    {
        // 雨の量 0〜1。0 が晴れ、1 が大雨くらい
        public static float RainAmount { get; set; }

        // 雨モードかどうか（RainAmount が 0 より大きい）
        public static bool IsRaining => RainAmount > 0.001f;

        // 晴れと雨を切りかえる（計測 HUD の F キー用）
        public static void ToggleRain()
        {
            RainAmount = IsRaining ? 0f : 1f;
        }
    }
}
