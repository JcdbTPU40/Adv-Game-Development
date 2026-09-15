using DG.Tweening;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

/*
    元素爆発っぽいカットインから、鬼滅の刃っぽい抜刀の一閃につなげる演出のプロトタイプ
    Tキーで発動する:
      時間停止 → 詠唱 → カットインカメラ（回りこみズーム＋ゆがみ） → バースト＋フラッシュ＋集中線 → 帯
      → スロー（構えてしずむ） → 一閃ダッシュ（剣のトレイル＋三日月の斬撃） → 残心（すごくスロー＋Vignette強め）
      → 敵がななめに真っ二つになってくずれる（フラッシュライン＋シェイク） → もとにもどる
    timeScale をいじって演出するので、Tween はぜんぶ SetUpdate(true)、
    CinemachineBrain は IgnoreTimeScale、ParticleSystem は useUnscaledTime を使っている
*/
public class CutInController : MonoBehaviour
{
    [Header("カメラ")]
    [SerializeField] private CinemachineCamera cutInCamera;
    [SerializeField] private CinemachineCamera slashSideCamera;
    [SerializeField] private CinemachineCamera zanshinCamera;
    [SerializeField] private Transform cutInRig;
    [SerializeField] private Transform player;
    [SerializeField] private float startYaw = 20f;
    [SerializeField] private float endYaw = 130f;
    [SerializeField] private float startFov = 50f;
    [SerializeField] private float endFov = 28f;
    [SerializeField] private float cameraMoveDuration = 1.0f;
    [SerializeField] private float returnBlendDuration = 0.8f;

    [Header("UI")]
    [SerializeField] private RectTransform band;
    [SerializeField] private Image flash;
    [SerializeField] private RawImage speedLines;
    [SerializeField] private Color accentColor = new Color(1f, 0.78f, 0.2f);
    [SerializeField] private float bandOffscreenX = 2600f;
    [SerializeField] private float bandSlideDuration = 0.35f;
    [SerializeField] private float holdDuration = 0.6f;

    [Header("カットインエフェクト")]
    [SerializeField] private Volume cutInVolume;
    [SerializeField] private ParticleSystem chargeRise;
    [SerializeField] private ParticleSystem chargeRing;
    [SerializeField] private ParticleSystem burstFx;
    [SerializeField] private float chargeDuration = 0.55f;

    [Header("斬撃")]
    [SerializeField] private Transform enemyRoot;
    [SerializeField] private Transform enemyTop;
    [SerializeField] private Transform cutFlashLine;
    [SerializeField] private TrailRenderer swordTrail;
    [SerializeField] private ParticleSystem slashArc;
    [SerializeField] private float slowTimeScale = 0.1f;
    [SerializeField] private float zanshinTimeScale = 0.02f;
    [SerializeField] private float crouchDuration = 0.45f;
    [SerializeField] private float dashDuration = 0.35f;
    [SerializeField] private float zanshinHold = 0.5f;
    [SerializeField] private float dashOverrun = 3f;

    private Sequence _sequence;
    private bool _isPlaying;

    private ChromaticAberration _chromatic;
    private LensDistortion _lensDistortion;
    private Vignette _vignette;
    private float _baseChromatic;
    private float _baseVignette;

    private CinemachineBrain _brain;
    private CinemachineBlendDefinition _defaultBlend;

    private Vector3 _playerStartPos;
    private Vector3 _playerStartScale;
    private Vector3 _enemyTopStartLocalPos;
    private Quaternion _enemyTopStartLocalRot;
    private Material _enemyTopMat;
    private Material _cutFlashMat;

    private void Start()
    {
        ResetUi();

        if (cutInVolume != null)
        {
            cutInVolume.weight = 0f;
            // volume.profile は実行中にコピーが作られるので、アセット本体は変わらない
            cutInVolume.profile.TryGet(out _chromatic);
            cutInVolume.profile.TryGet(out _lensDistortion);
            cutInVolume.profile.TryGet(out _vignette);
            if (_chromatic != null)
            {
                _baseChromatic = _chromatic.intensity.value;
            }
            if (_vignette != null)
            {
                _baseVignette = _vignette.intensity.value;
            }
        }

        _brain = FindFirstObjectByType<CinemachineBrain>();
        if (_brain != null)
        {
            _defaultBlend = _brain.DefaultBlend;
        }

        _playerStartPos = player.position;
        _playerStartScale = player.localScale;
        if (enemyTop != null)
        {
            _enemyTopStartLocalPos = enemyTop.localPosition;
            _enemyTopStartLocalRot = enemyTop.localRotation;
            // フェード用に、マテリアルの実行中のコピーを取る（アセットをよごさないため）
            _enemyTopMat = enemyTop.GetComponent<Renderer>().material;
        }
        if (cutFlashLine != null)
        {
            _cutFlashMat = cutFlashLine.GetComponent<Renderer>().material;
        }

        // timeScale をいじっている間もパーティクルが動くように、念のためここでも設定しておく
        SetUnscaled(chargeRise);
        SetUnscaled(chargeRing);
        SetUnscaled(burstFx);
        SetUnscaled(slashArc);

        ResetSlashState();
    }

    private void Update()
    {
        if (Keyboard.current != null && Keyboard.current.tKey.wasPressedThisFrame)
        {
            Play();
        }
    }

    // カットインと一閃の演出を始める（スキルを使ったときに呼ぶつもり）
    public void Play()
    {
        if (_isPlaying)
        {
            return;
        }
        _isPlaying = true;

        Time.timeScale = 0f;

        // 最初の状態にもどす
        cutInRig.position = player.position;
        cutInRig.rotation = Quaternion.Euler(0f, startYaw, 0f);
        SetFov(startFov);
        ResetUi();
        ResetSlashState();

        // 詠唱のパーティクルを始める
        if (chargeRise != null) chargeRise.Play();
        if (chargeRing != null) chargeRing.Play();

        // ---- タイムライン（開始からの時刻、実時間） ----
        float tCam = chargeDuration;                            // カットインカメラに切りかえ
        float tBurst = tCam + 0.35f;                            // バースト＋フラッシュ
        float tBand = tBurst + 0.05f;                           // 帯が入ってくる
        float tBandOut = tBand + bandSlideDuration + holdDuration;
        float tSlow = tBandOut + bandSlideDuration * 0.5f;      // スローに入る（帯が出ていく途中）
        float tDash = tSlow + crouchDuration;                   // 一閃スタート
        float tPass = tDash + dashDuration * 0.6f;              // すれちがい（斬撃）
        float tZanshin = tDash + dashDuration + 0.05f;          // 残心
        float tCut = tZanshin + zanshinHold;                    // 敵が切れる
        float tRestore = tCut + 1.1f;                           // もとにもどし始める
        float tEnd = tRestore + returnBlendDuration + 0.15f;

        _sequence = DOTween.Sequence().SetUpdate(true);

        // ---- ポストプロセスを立ち上げる ----
        if (cutInVolume != null)
        {
            _sequence.Insert(0f, DOTween
                .To(() => cutInVolume.weight, w => cutInVolume.weight = w, 1f, 0.3f));
        }

        // ---- カットイン: カメラの回りこみズームとレンズのゆがみ ----
        _sequence.InsertCallback(tCam, () => cutInCamera.Priority = 20);
        _sequence.Insert(tCam, cutInRig
            .DORotate(new Vector3(0f, endYaw, 0f), cameraMoveDuration)
            .SetEase(Ease.OutCubic));
        _sequence.Insert(tCam, DOTween
            .To(GetFov, SetFov, endFov, cameraMoveDuration)
            .SetEase(Ease.OutCubic));
        if (_lensDistortion != null)
        {
            _sequence.Insert(tCam, DOTween
                .To(() => _lensDistortion.intensity.value, v => _lensDistortion.intensity.value = v, -0.45f, 0.15f)
                .SetEase(Ease.OutQuad));
            _sequence.Insert(tCam + 0.15f, DOTween
                .To(() => _lensDistortion.intensity.value, v => _lensDistortion.intensity.value = v, 0f, 0.35f)
                .SetEase(Ease.InOutSine));
        }

        // ---- バーストと色収差を一気に上げる ----
        _sequence.InsertCallback(tBurst, () =>
        {
            if (chargeRise != null) chargeRise.Stop();
            if (chargeRing != null) chargeRing.Stop();
            if (burstFx != null) burstFx.Play();
            if (_chromatic != null) _chromatic.intensity.value = 1f;
        });
        if (_chromatic != null)
        {
            _sequence.Insert(tBurst + 0.05f, DOTween
                .To(() => _chromatic.intensity.value, v => _chromatic.intensity.value = v, _baseChromatic, 0.6f)
                .SetEase(Ease.OutQuad));
        }

        // ---- フラッシュ: 白 → アクセント色 → 透明 ----
        Color accentFlash = accentColor;
        accentFlash.a = 0.7f;
        _sequence.Insert(tBurst, flash.DOFade(0.9f, 0.06f));
        _sequence.Insert(tBurst + 0.06f, flash.DOColor(accentFlash, 0.15f));
        _sequence.Insert(tBurst + 0.21f, flash.DOFade(0f, 0.45f));

        // ---- 集中線 ----
        if (speedLines != null)
        {
            _sequence.Insert(tBurst, speedLines.DOFade(0.85f, 0.05f));
            _sequence.Insert(tBurst + 0.05f, speedLines
                .DOFade(0.35f, 0.07f)
                .SetLoops(10, LoopType.Yoyo));
            _sequence.Insert(tBurst, speedLines.rectTransform
                .DORotate(new Vector3(0f, 0f, -90f), tBandOut - tBurst, RotateMode.FastBeyond360)
                .SetEase(Ease.Linear));
            _sequence.Insert(tBandOut, speedLines.DOFade(0f, 0.2f));
        }

        // ---- 帯が入る → 止まる → 出ていく ----
        _sequence.Insert(tBand, band
            .DOAnchorPosX(0f, bandSlideDuration)
            .SetEase(Ease.OutCubic));
        _sequence.Insert(tBandOut, band
            .DOAnchorPosX(bandOffscreenX, bandSlideDuration)
            .SetEase(Ease.InCubic));

        // ---- 構え: スローに入って、しずみこんでためる ----
        _sequence.InsertCallback(tSlow, () => Time.timeScale = slowTimeScale);
        _sequence.Insert(tSlow, player
            .DOScaleY(_playerStartScale.y * 0.85f, crouchDuration * 0.7f)
            .SetEase(Ease.OutQuad));
        _sequence.Insert(tSlow, player
            .DOMoveY(_playerStartPos.y - 0.12f, crouchDuration * 0.7f)
            .SetEase(Ease.OutQuad));

        // ---- 一閃: 横から撮るカメラに切りかえて、敵をつきぬけてうしろへ ----
        Vector3 dashDir = enemyRoot != null
            ? (enemyRoot.position + Vector3.up * _playerStartPos.y - _playerStartPos).normalized
            : Vector3.forward;
        Vector3 dashTarget = enemyRoot != null
            ? enemyRoot.position + Vector3.up * _playerStartPos.y + dashDir * dashOverrun
            : _playerStartPos + Vector3.forward * (5f + dashOverrun);
        dashTarget.y = _playerStartPos.y;

        _sequence.InsertCallback(tDash, () =>
        {
            if (_brain != null)
            {
                _brain.DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.Cut, 0f);
            }
            if (slashSideCamera != null) slashSideCamera.Priority = 30;
            cutInCamera.Priority = 0;
            if (swordTrail != null)
            {
                swordTrail.Clear();
                swordTrail.emitting = true;
            }
        });
        _sequence.Insert(tDash, player.DOScaleY(_playerStartScale.y, 0.1f));
        _sequence.Insert(tDash, player
            .DOMove(dashTarget, dashDuration)
            .SetEase(Ease.InExpo));

        // すれちがうときの三日月の斬撃
        _sequence.InsertCallback(tPass, () =>
        {
            if (slashArc != null) slashArc.Play();
        });

        // ---- 残心: すごくスロー＋背中ごしに寄る＋Vignette強め ----
        _sequence.InsertCallback(tZanshin, () =>
        {
            Time.timeScale = zanshinTimeScale;
            if (zanshinCamera != null) zanshinCamera.Priority = 40;
            if (swordTrail != null) swordTrail.emitting = false;
        });
        if (_vignette != null)
        {
            _sequence.Insert(tZanshin, DOTween
                .To(() => _vignette.intensity.value, v => _vignette.intensity.value = v, 0.6f, 0.3f));
        }

        // ---- ちょっと遅れて敵が切れる ----
        _sequence.InsertCallback(tCut, () =>
        {
            if (_cutFlashMat != null)
            {
                var c = _cutFlashMat.GetColor("_BaseColor");
                c.a = 1f;
                _cutFlashMat.SetColor("_BaseColor", c);
            }
            if (_chromatic != null) _chromatic.intensity.value = 0.6f;
        });
        if (cutFlashLine != null)
        {
            _sequence.Insert(tCut, cutFlashLine
                .DOScaleX(4f, 0.1f)
                .SetEase(Ease.OutQuart));
        }
        if (_cutFlashMat != null)
        {
            _sequence.Insert(tCut + 0.12f, _cutFlashMat.DOFade(0f, "_BaseColor", 0.3f));
        }
        if (_chromatic != null)
        {
            _sequence.Insert(tCut + 0.05f, DOTween
                .To(() => _chromatic.intensity.value, v => _chromatic.intensity.value = v, _baseChromatic, 0.4f));
        }
        if (zanshinCamera != null)
        {
            _sequence.Insert(tCut, zanshinCamera.transform
                .DOShakePosition(0.3f, 0.12f, 20, 90f, false, true));
        }
        if (enemyTop != null)
        {
            // ななめ上にずれてから落ちて、フェードで消える
            _sequence.Insert(tCut + 0.05f, enemyTop
                .DOLocalMove(_enemyTopStartLocalPos + new Vector3(0.35f, 0.12f, 0f), 0.25f)
                .SetEase(Ease.OutCubic));
            _sequence.Insert(tCut + 0.05f, enemyTop
                .DOLocalRotate(new Vector3(0f, 0f, -18f), 0.25f));
            _sequence.Insert(tCut + 0.32f, enemyTop
                .DOLocalMove(_enemyTopStartLocalPos + new Vector3(0.7f, -0.85f, 0f), 0.6f)
                .SetEase(Ease.InQuad));
            _sequence.Insert(tCut + 0.32f, enemyTop
                .DOLocalRotate(new Vector3(0f, 0f, -65f), 0.6f));
        }
        if (_enemyTopMat != null)
        {
            _sequence.Insert(tCut + 0.45f, _enemyTopMat.DOFade(0f, "_BaseColor", 0.55f));
        }

        // ---- もとにもどす ----
        _sequence.InsertCallback(tRestore, () =>
        {
            if (_brain != null) _brain.DefaultBlend = _defaultBlend;
            if (slashSideCamera != null) slashSideCamera.Priority = 0;
            if (zanshinCamera != null) zanshinCamera.Priority = 0;
        });
        _sequence.Insert(tRestore, DOTween
            .To(() => Time.timeScale, v => Time.timeScale = v, 1f, 0.4f)
            .SetEase(Ease.OutQuad));
        if (cutInVolume != null)
        {
            _sequence.Insert(tRestore, DOTween
                .To(() => cutInVolume.weight, w => cutInVolume.weight = w, 0f, returnBlendDuration));
        }
        if (_vignette != null)
        {
            _sequence.Insert(tRestore, DOTween
                .To(() => _vignette.intensity.value, v => _vignette.intensity.value = v, _baseVignette, 0.5f));
        }

        _sequence.InsertCallback(tEnd, () => { });
        _sequence.OnComplete(Finish);
    }

    private float GetFov()
    {
        return cutInCamera.Lens.FieldOfView;
    }

    private void SetFov(float value)
    {
        var lens = cutInCamera.Lens;
        lens.FieldOfView = value;
        cutInCamera.Lens = lens;
    }

    private void ResetUi()
    {
        if (band != null)
        {
            band.anchoredPosition = new Vector2(-bandOffscreenX, band.anchoredPosition.y);
        }
        if (flash != null)
        {
            flash.color = new Color(1f, 1f, 1f, 0f);
        }
        if (speedLines != null)
        {
            var c = speedLines.color;
            c.a = 0f;
            speedLines.color = c;
            speedLines.rectTransform.localRotation = Quaternion.identity;
        }
    }

    // プレイヤーと敵とトレイルを、演出の前の状態にもどす
    private void ResetSlashState()
    {
        if (player != null)
        {
            player.position = _playerStartPos;
            player.localScale = _playerStartScale;
        }
        if (enemyTop != null)
        {
            enemyTop.localPosition = _enemyTopStartLocalPos;
            enemyTop.localRotation = _enemyTopStartLocalRot;
        }
        if (_enemyTopMat != null)
        {
            var c = _enemyTopMat.GetColor("_BaseColor");
            c.a = 1f;
            _enemyTopMat.SetColor("_BaseColor", c);
        }
        if (cutFlashLine != null)
        {
            var s = cutFlashLine.localScale;
            s.x = 0f;
            cutFlashLine.localScale = s;
        }
        if (swordTrail != null)
        {
            swordTrail.emitting = false;
            swordTrail.Clear();
        }
    }

    private static void SetUnscaled(ParticleSystem ps)
    {
        if (ps == null)
        {
            return;
        }
        var main = ps.main;
        main.useUnscaledTime = true;
    }

    private void Finish()
    {
        Time.timeScale = 1f;
        _isPlaying = false;

        // カメラとポストプロセスを完全にもとにもどす
        if (_brain != null) _brain.DefaultBlend = _defaultBlend;
        cutInCamera.Priority = 0;
        if (slashSideCamera != null) slashSideCamera.Priority = 0;
        if (zanshinCamera != null) zanshinCamera.Priority = 0;
        if (cutInVolume != null)
        {
            cutInVolume.weight = 0f;
        }
        if (_chromatic != null)
        {
            _chromatic.intensity.value = _baseChromatic;
        }
        if (_lensDistortion != null)
        {
            _lensDistortion.intensity.value = 0f;
        }
        if (_vignette != null)
        {
            _vignette.intensity.value = _baseVignette;
        }

        ResetSlashState();
    }

    private void OnDestroy()
    {
        _sequence?.Kill();
        if (_isPlaying)
        {
            Time.timeScale = 1f;
        }
    }
}
