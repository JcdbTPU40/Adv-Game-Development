using UnityEngine;
using UnityEngine.UI;

namespace Toufuku.Menu
{
    /*
        メインメニューの背景（夜明けの境内）の、寄り引きと暗さ

        ・絵はシーンに置いてある（MainMenuCanvas/ShrineBackdrop の下。空・星・月・山・桜・参道・鳥居・灯籠・花びら）
          ふだんどおり Scene ビューで動かす・色を変える・画像に差しかえる
        ・寄り引きは Scene（1920×1080 の板）の拡大率で出す。画面を暗くするのは Dim の板
          どちらも MainMenu が、画面（MenuPage）ごとの値を SetCamera / SetDim で入れてくる
        ・灯籠のゆらぎは AnimationClip（Menu_LanternFlicker）。星は MenuTwinkle、花びらは MenuPetals
    */
    public class ShrineBackdrop : MonoBehaviour
    {
        [Tooltip("絵をのせた 1920×1080 の板（これを拡大して寄る）")]
        [SerializeField] RectTransform scene;
        [Tooltip("画面を暗くする板")]
        [SerializeField] Image dim;
        [Tooltip("寄り引きの速さ（大きいほど早く落ちつく）")]
        [SerializeField, Min(0.1f)] float cameraSpeed = 1.8f;
        [Tooltip("暗さが変わる速さ（1秒あたり）")]
        [SerializeField, Min(0.1f)] float dimSpeed = 1.2f;

        float _zoom = 1f, _zoomTarget = 1f;
        float _dimAlpha, _dimTarget;
        Vector2 _offset, _offsetTarget;

        // 寄り（1 で全景、大きいほど鳥居に寄る）。offsetY はピクセル（マイナスで下を見る）
        public void SetCamera(float zoom, float offsetY = 0f, bool instant = false)
        {
            _zoomTarget = zoom;
            _offsetTarget = new Vector2(0f, offsetY);
            if (!instant) return;
            _zoom = _zoomTarget;
            _offset = _offsetTarget;
            Apply();
        }

        public void SetDim(float alpha, bool instant = false)
        {
            _dimTarget = alpha;
            if (!instant) return;
            _dimAlpha = alpha;
            Apply();
        }

        void Update()
        {
            float dt = Time.deltaTime;
            float k = 1f - Mathf.Exp(-dt * cameraSpeed);
            _zoom = Mathf.Lerp(_zoom, _zoomTarget, k);
            _offset = Vector2.Lerp(_offset, _offsetTarget, k);
            _dimAlpha = Mathf.MoveTowards(_dimAlpha, _dimTarget, dt * dimSpeed);
            Apply();
        }

        void Apply()
        {
            if (scene != null)
            {
                scene.localScale = Vector3.one * _zoom;
                scene.anchoredPosition = _offset * _zoom;
            }
            if (dim != null)
            {
                Color c = dim.color;
                c.a = _dimAlpha;
                dim.color = c;
            }
        }
    }
}
