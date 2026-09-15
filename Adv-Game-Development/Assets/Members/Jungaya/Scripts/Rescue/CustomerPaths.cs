using System.Collections.Generic;
using UnityEngine;

namespace Toufuku.Rescue
{
    /// <summary>
    /// 移動客の往復経路（企画書 v8 10章「移動客：定位置到着後、現在の距離帯の中を左右3mの往復経路で移動。奥行帯は変えない」
    /// ／付録B MOVE.SPEED 1.0m/s）— Issue #62
    ///
    ///   ・経路は定位置を中心にした左右（X方向）の線分。奥行き（Z）は変えない。
    ///   ・「左右3m」は端から端までの幅として扱う（中心から ±1.5m）。
    ///   ・中心から歩き出し、片端で折り返す三角波。位置は「歩いた道のり」だけで決まるので、
    ///     同じ速度・同じ向きなら何度やっても同じ経路をたどる（固定シードで再現できる）。
    ///
    /// MonoBehaviour 非依存。境界はエディタテストで検証する（CustomerPathsTests）。
    /// </summary>
    public static class PatrolPath
    {
        /// <summary>
        /// 経路の中心から見た左右のずれ。
        /// </summary>
        /// <param name="width">往復の幅（端から端、m）。</param>
        /// <param name="travelled">歩き始めてからの道のり（m）。</param>
        /// <param name="startSign">+1 なら右（+X）へ、-1 なら左（-X）へ歩き出す。</param>
        public static float Offset(float width, float travelled, int startSign)
        {
            float half = Mathf.Max(0f, width) * 0.5f;
            if (half <= 0.0001f) return 0f;

            float period = 4f * half;
            float s = Mathf.Repeat(Mathf.Max(0f, travelled), period);

            float offset;
            if (s < half) offset = s;                         // 中心 → 片端
            else if (s < 3f * half) offset = 2f * half - s;   // 片端 → 反対の端
            else offset = s - 4f * half;                      // 反対の端 → 中心

            return startSign >= 0 ? offset : -offset;
        }

        /// <summary>経路が帯の左右範囲からはみ出さないよう、中心を内側へ寄せる。帯が経路より狭ければ帯の中央。</summary>
        public static float ClampCenter(float center, float width, float minX, float maxX)
        {
            float lo = Mathf.Min(minX, maxX);
            float hi = Mathf.Max(minX, maxX);
            float half = Mathf.Max(0f, width) * 0.5f;
            if (hi - lo < 2f * half) return (lo + hi) * 0.5f;
            return Mathf.Clamp(center, lo + half, hi - half);
        }

        /// <summary>
        /// 2本の左右レーン（XZ平面の水平な線分）の最短距離。幅0なら点として扱う。
        /// 移動客の経路の上に別の客を立たせない（通り抜けて重ならない）ための間隔の判定に使う。
        /// </summary>
        public static float LaneToLane(Vector3 centerA, float widthA, Vector3 centerB, float widthB)
        {
            float gapX = Mathf.Max(0f, Mathf.Abs(centerA.x - centerB.x) - Mathf.Max(0f, widthA) * 0.5f - Mathf.Max(0f, widthB) * 0.5f);
            float dz = centerA.z - centerB.z;
            return Mathf.Sqrt(gapX * gapX + dz * dz);
        }
    }

    /// <summary>
    /// 退場経路（企画書 v8 6章「救済成功／失敗の演出」：救済3秒／黒客4秒かけて参道を歩いて退場）— Issue #62
    ///
    ///   ・出口は手前（プレイヤー側）の左右に置く。客は左右位置が近い方の出口へまっすぐ歩く。
    ///   ・奥の客ほど帰路が長く、手前の客の間を通り抜けるので、すれ違う人数が増える
    ///     （6章「参道の奥にいる参拝客ほど、すれ違う人数が多くなる」／7章 遠方客「帰路が長く伝播人数が多い」）。
    ///   ・歩く時間は退場秒数で固定なので、帰路が長い客ほど速く歩く。
    /// </summary>
    public static class ExitRoute
    {
        /// <summary>左右位置（X）が最も近い出口を選ぶ。同じ近さなら添字の小さい方。出口が無ければ -1。</summary>
        public static int Choose(Vector3 from, IReadOnlyList<Vector3> exits)
        {
            if (exits == null || exits.Count == 0) return -1;

            int best = 0;
            float bestGap = Mathf.Abs(exits[0].x - from.x);
            for (int i = 1; i < exits.Count; i++)
            {
                float gap = Mathf.Abs(exits[i].x - from.x);
                if (gap < bestGap - 0.0001f)
                {
                    best = i;
                    bestGap = gap;
                }
            }
            return best;
        }

        /// <summary>出口までの水平距離（帰路の長さ）。</summary>
        public static float Length(Vector3 from, Vector3 exit)
        {
            float dx = exit.x - from.x;
            float dz = exit.z - from.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }
    }
}
