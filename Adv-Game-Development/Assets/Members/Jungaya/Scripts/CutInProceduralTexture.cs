using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// カットインUI用のテクスチャを実行時に動的生成してRawImageへ割り当てる。
/// SpeedLines: 放射状の集中線 / EdgeGradient: 帯の縁用の発光風グラデーション。
/// </summary>
[RequireComponent(typeof(RawImage))]
public class CutInProceduralTexture : MonoBehaviour
{
    public enum TextureMode
    {
        SpeedLines,
        EdgeGradient,
    }

    [SerializeField] private TextureMode mode = TextureMode.SpeedLines;
    [SerializeField] private Color accentColor = new Color(1f, 0.78f, 0.2f, 1f);

    private Texture2D _texture;

    private void Awake()
    {
        _texture = mode == TextureMode.SpeedLines
            ? CreateSpeedLines(512, 96)
            : CreateEdgeGradient(256);
        GetComponent<RawImage>().texture = _texture;
    }

    private void OnDestroy()
    {
        if (_texture != null)
        {
            Destroy(_texture);
        }
    }

    /// <summary>中心が透明で外周へ向かって伸びる放射状の集中線。</summary>
    private static Texture2D CreateSpeedLines(int size, int lineCount)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var pixels = new Color32[size * size];
        var lengths = new float[lineCount];
        var widths = new float[lineCount];
        var rand = new System.Random(12345);
        for (int i = 0; i < lineCount; i++)
        {
            lengths[i] = 0.30f + (float)rand.NextDouble() * 0.35f;
            widths[i] = 0.18f + (float)rand.NextDouble() * 0.22f;
        }

        float half = size * 0.5f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x - half) / half;
                float dy = (y - half) / half;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float angle01 = (Mathf.Atan2(dy, dx) + Mathf.PI) / (2f * Mathf.PI);
                int sector = Mathf.Min(lineCount - 1, (int)(angle01 * lineCount));
                // 0=線の中心 1=セクター境界
                float inSector = Mathf.Abs(angle01 * lineCount - sector - 0.5f) * 2f;
                float lineAlpha = Mathf.Clamp01((widths[sector] - inSector) / 0.15f);
                float radial = Mathf.Clamp01((r - lengths[sector]) / 0.25f);
                byte a = (byte)(255f * lineAlpha * radial);
                pixels[y * size + x] = new Color32(255, 255, 255, a);
            }
        }

        tex.SetPixels32(pixels);
        tex.Apply();
        tex.wrapMode = TextureWrapMode.Clamp;
        return tex;
    }

    /// <summary>中央が白熱して見える横方向グラデーション(帯の縁用)。</summary>
    private Texture2D CreateEdgeGradient(int width)
    {
        var tex = new Texture2D(width, 1, TextureFormat.RGBA32, false);
        for (int x = 0; x < width; x++)
        {
            float t = x / (float)(width - 1);
            float glow = Mathf.Pow(Mathf.Sin(t * Mathf.PI), 0.75f);
            Color c = Color.Lerp(accentColor, Color.white, glow * 0.65f);
            c.a = 0.25f + 0.75f * glow;
            tex.SetPixel(x, 0, c);
        }

        tex.Apply();
        tex.wrapMode = TextureWrapMode.Clamp;
        return tex;
    }
}
