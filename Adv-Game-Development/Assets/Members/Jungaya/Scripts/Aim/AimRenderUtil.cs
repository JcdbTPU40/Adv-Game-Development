using UnityEngine;

namespace Toufuku.Aim
{
    /*
        照準と軌跡を仮で表示するためのマテリアルを、実行中に作るクラス（#60）
        ちゃんとした素材が来るまでの代わり
    */
    public static class AimRenderUtil
    {
        // 頂点カラーの透明度がそのまま使える、両面描画の半透明マテリアルを作る。作れなかったら null を返す
        public static Material CreateTransparentMaterial(string name)
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) return null;
            return new Material(shader) { name = name };
        }
    }
}
