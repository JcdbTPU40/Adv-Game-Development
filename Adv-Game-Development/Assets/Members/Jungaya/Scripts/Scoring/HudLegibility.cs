using UnityEngine;

/*
    HUD の文字が何m先から読めるかを計算するクラス（#61 / 企画書 v8 13章「縁と現在ランクはMVPでも画面外から読める大きさにする」）

    展示のモニターは会場の備品でサイズが決まっていない（16章）。なので HUD の文字の大きさは「画面の高さの何割か」で持って、
    モニターのインチ数と見る人の距離から、足りているかをここで確かめる

    読める距離の目安:
      文字の高さが見る人の目から 22分角（0.367度）以上に見えれば、楽に読める（ANSI/HFES 100 の推奨 20〜22分角）
      距離 = 文字の高さ ÷ (2 × tan(11分角))  → 文字の高さ 1cm ≒ 1.56m

    文字の高さ:
      数字と英大文字（C/B/A/S）の高さはフォントサイズの約 72%（Noto Sans JP の cap height ≒ 0.733em を少し小さめに見た値）
*/
public static class HudLegibility
{
    // 楽に読める見た目の大きさ（分角）
    public const float ComfortArcMinutes = 22f;
    // 数字と英大文字の高さ ÷ フォントサイズ
    public const float GlyphHeightPerFontSize = 0.72f;

    // 13章: 観客とアテンドは、プレイヤーの後ろ 2m 以上。プレイヤーは画面から約 2.5m、そのうしろに 5〜10人の列ができる
    // 列のうしろから読む距離の目安（画面から）
    public const float SpectatorDistanceMeters = 7f;
    // 目安にする小さめのモニター（会場備品。これより大きければもっと遠くから読める）
    public const float ReferenceDiagonalInches = 50f;

    // モニターの画面の高さ（cm）
    public static float ScreenHeightCm(float diagonalInches, float aspectWidth = 16f, float aspectHeight = 9f)
    {
        if (diagonalInches <= 0f || aspectWidth <= 0f || aspectHeight <= 0f) return 0f;
        float diagonalRatio = Mathf.Sqrt(aspectWidth * aspectWidth + aspectHeight * aspectHeight);
        return diagonalInches * 2.54f * aspectHeight / diagonalRatio;
    }

    // フォントサイズ（画面の高さに対するわりあい）から、数字の高さ（cm）を出す
    public static float GlyphHeightCm(float fontSizeScreenRatio, float diagonalInches)
    {
        return Mathf.Max(0f, fontSizeScreenRatio) * GlyphHeightPerFontSize * ScreenHeightCm(diagonalInches);
    }

    // 文字の高さ（cm）から、楽に読める最大の距離（m）
    public static float ReadableDistanceMeters(float glyphHeightCm, float arcMinutes = ComfortArcMinutes)
    {
        if (glyphHeightCm <= 0f || arcMinutes <= 0f) return 0f;
        float halfAngleRad = arcMinutes / 60f * 0.5f * Mathf.Deg2Rad;
        return glyphHeightCm / 100f / (2f * Mathf.Tan(halfAngleRad));
    }

    /*
        フォントサイズのわりあいとモニターから、楽に読める最大の距離（m）
        ※ ReadableDistanceMeters(文字の高さ, 分角) と引数の数が同じになって取りちがえるので、名前を分けている
    */
    public static float ReadableDistanceMetersForFont(float fontSizeScreenRatio, float diagonalInches, float arcMinutes = ComfortArcMinutes)
    {
        return ReadableDistanceMeters(GlyphHeightCm(fontSizeScreenRatio, diagonalInches), arcMinutes);
    }

    // この距離から読むのに必要なフォントサイズのわりあい
    public static float RequiredFontSizeRatio(float distanceMeters, float diagonalInches, float arcMinutes = ComfortArcMinutes)
    {
        float screenCm = ScreenHeightCm(diagonalInches);
        if (screenCm <= 0f || distanceMeters <= 0f) return 0f;
        float halfAngleRad = arcMinutes / 60f * 0.5f * Mathf.Deg2Rad;
        float glyphCm = distanceMeters * 100f * 2f * Mathf.Tan(halfAngleRad);
        return glyphCm / GlyphHeightPerFontSize / screenCm;
    }
}
