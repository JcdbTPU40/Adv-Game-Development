using UnityEngine;

namespace Toufuku.Aim
{
    /// <summary>照準・軌跡の仮表示に使うマテリアルを実行時に作る（素材が届くまでの代用）— Issue #60</summary>
    public static class AimRenderUtil
    {
        /// <summary>頂点カラーの透明度がそのまま効く、両面描画の半透明マテリアル。作れなければ null。</summary>
        public static Material CreateTransparentMaterial(string name)
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) return null;
            return new Material(shader) { name = name };
        }
    }
}
