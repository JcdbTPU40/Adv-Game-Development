using Toufuku.Aim;
using UnityEngine;

namespace Toufuku.Rescue.Outline
{
    /*
        照準が乗っている客の輪郭を一段明るくするクラス（#59。企画書 v8 3章「照準表示」）

        「乗っている」は、今の着弾予測点に弾が落ちたら当たる客にする（OmamoriProjectile.FindTarget と同じ判定）
        画面の照準マークと着弾予測点は同じ場所なので、明るくなった客＝今投げたら当たる客、になる
        ロック表示ではないので、照準が外れたらすぐもどす（ねばらせたり、形や太さを変えたりしない）

        シーンに1つだけ置く。aim が空ならシーンから OnusaAimController をさがす
    */
    [DisallowMultipleComponent]
    public class OutlineAimHighlighter : MonoBehaviour
    {
        [Tooltip("照準。未設定ならシーンから探す。")]
        [SerializeField] OnusaAimController aim;

        OutlineTarget _current;

        // 今明るくしている客（だれにも乗っていなければ null）
        public OutlineTarget Current => _current;

        void Awake()
        {
            if (aim == null) aim = FindFirstObjectByType<OnusaAimController>();
        }

        void LateUpdate()
        {
            OutlineTarget next = null;
            if (aim != null && aim.isActiveAndEnabled && aim.HasAim)
            {
                HitZoneTarget hit = OmamoriProjectile.FindTarget(aim.TargetPoint, out _);
                if (hit != null)
                {
                    next = hit.GetComponentInChildren<OutlineTarget>();
                    if (next == null) next = hit.GetComponentInParent<OutlineTarget>();
                }
            }
            SetCurrent(next);
        }

        void OnDisable()
        {
            SetCurrent(null);
        }

        void SetCurrent(OutlineTarget next)
        {
            if (next == _current) return;
            if (_current != null) _current.SetHighlighted(false);
            _current = next;
            if (_current != null) _current.SetHighlighted(true);
        }
    }
}
