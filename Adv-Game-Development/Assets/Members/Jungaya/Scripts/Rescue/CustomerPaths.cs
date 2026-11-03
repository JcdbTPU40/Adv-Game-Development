using System.Collections.Generic;
using UnityEngine;

namespace Toufuku.Rescue
{
    /*
        移動客の往復の道（企画書 v8 10章「移動客：定位置に着いたら、今の距離の帯の中を左右3mの往復の道で動く。奥行きの帯は変えない」
        、付録B MOVE.SPEED 1.0m/s）（#62）

          ・道は定位置を真ん中にした左右（X方向）の線。奥行き（Z）は変えない
          ・「左右3m」は、はしからはしまでのはばとしてあつかう（真ん中から ±1.5m）
          ・真ん中から歩き出して、はしで折り返す三角波。位置は「歩いた道のり」だけで決まるので、
            同じ速さで同じ向きなら何回やっても同じ道を通る（決まったシードで同じにできる）

        MonoBehaviour は使っていない。さかい目はエディタのテストで確かめる（CustomerPathsTests）
    */
    public static class PatrolPath
    {
        /*
            道の真ん中から見た、左右のずれ
            width: 往復のはば（はしからはし、m）
            travelled: 歩き始めてからの道のり（m）
            startSign: +1 なら右（+X）へ、-1 なら左（-X）へ歩き出す
        */
        public static float Offset(float width, float travelled, int startSign)
        {
            float half = Mathf.Max(0f, width) * 0.5f;
            if (half <= 0.0001f) return 0f;

            float period = 4f * half;
            float s = Mathf.Repeat(Mathf.Max(0f, travelled), period);

            float offset;
            if (s < half) offset = s;                         // 真ん中 → 片方のはし
            else if (s < 3f * half) offset = 2f * half - s;   // 片方のはし → 反対のはし
            else offset = s - 4f * half;                      // 反対のはし → 真ん中

            return startSign >= 0 ? offset : -offset;
        }

        // 道が帯の左右のはんいからはみ出さないように、真ん中を内側に寄せる。帯が道よりせまければ帯の真ん中
        public static float ClampCenter(float center, float width, float minX, float maxX)
        {
            float lo = Mathf.Min(minX, maxX);
            float hi = Mathf.Max(minX, maxX);
            float half = Mathf.Max(0f, width) * 0.5f;
            if (hi - lo < 2f * half) return (lo + hi) * 0.5f;
            return Mathf.Clamp(center, lo + half, hi - half);
        }

        /*
            左右のレーン2本（XZ平面の水平な線）のいちばん近い距離。はばが0なら点としてあつかう
            移動客の道の上にほかの客を立たせない（通りぬけて重ならない）ための間かくの判定に使う
        */
        public static float LaneToLane(Vector3 centerA, float widthA, Vector3 centerB, float widthB)
        {
            float gapX = Mathf.Max(0f, Mathf.Abs(centerA.x - centerB.x) - Mathf.Max(0f, widthA) * 0.5f - Mathf.Max(0f, widthB) * 0.5f);
            float dz = centerA.z - centerB.z;
            return Mathf.Sqrt(gapX * gapX + dz * dz);
        }
    }

    /*
        帰り道（企画書 v8 6章「救済成功／失敗の演出」: 救済は3秒、黒客は4秒かけて参道を歩いて帰る）（#62）

          ・出口は手前（プレイヤーのほう）の左右に置く。客は左右の位置が近いほうの出口にまっすぐ歩く
          ・奥の客ほど帰り道が長くて、手前の客の間を通りぬけるので、すれちがう人数が増える
            （6章「参道の奥にいる参拝客ほど、すれ違う人数が多くなる」、7章 遠方客「帰路が長く伝播人数が多い」）
          ・歩く時間は帰る秒数で決まっているので、帰り道が長い客ほど速く歩く
    */
    public static class ExitRoute
    {
        // 左右の位置（X）がいちばん近い出口を選ぶ。同じ近さなら番号が小さいほう。出口がなければ -1
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

        // 出口までの水平距離（帰り道の長さ）
        public static float Length(Vector3 from, Vector3 exit)
        {
            float dx = exit.x - from.x;
            float dz = exit.z - from.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }
    }
    /*
        置き物（灯籠・おみくじ・手水舎・狛犬など）をさけるための、床の上（XZ平面）の距離
        置き物は見た目の大きさを床に落とした四角（Rect の x が X、y が Z）であつかう

        MonoBehaviour は使っていない。エディタのテストで確かめる（CustomerPathsTests）
    */
    public static class ObstacleGeometry
    {
        // 点から四角までの距離。中に入っていたら 0
        public static float PointToRect(Vector2 p, Rect r)
        {
            float dx = Mathf.Max(r.xMin - p.x, 0f, p.x - r.xMax);
            float dy = Mathf.Max(r.yMin - p.y, 0f, p.y - r.yMax);
            return Mathf.Sqrt(dx * dx + dy * dy);
        }

        // 線分から四角までの距離。線分が四角を通っていたら 0
        public static float SegmentToRect(Vector2 a, Vector2 b, Rect r)
        {
            if (SegmentCrossesRect(a, b, r)) return 0f;

            float d = Mathf.Min(PointToRect(a, r), PointToRect(b, r));
            d = Mathf.Min(d, PointToSegment(new Vector2(r.xMin, r.yMin), a, b));
            d = Mathf.Min(d, PointToSegment(new Vector2(r.xMax, r.yMin), a, b));
            d = Mathf.Min(d, PointToSegment(new Vector2(r.xMin, r.yMax), a, b));
            d = Mathf.Min(d, PointToSegment(new Vector2(r.xMax, r.yMax), a, b));
            return d;
        }

        // 点から線分までの距離
        public static float PointToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float len2 = ab.sqrMagnitude;
            float t = len2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2) : 0f;
            return Vector2.Distance(p, a + ab * t);
        }

        // 線分が四角を通るか（Liang-Barsky で四角の中に残る部分があるかを見る）
        static bool SegmentCrossesRect(Vector2 a, Vector2 b, Rect r)
        {
            Vector2 d = b - a;
            float t0 = 0f, t1 = 1f;
            return Clip(-d.x, a.x - r.xMin, ref t0, ref t1)
                   && Clip(d.x, r.xMax - a.x, ref t0, ref t1)
                   && Clip(-d.y, a.y - r.yMin, ref t0, ref t1)
                   && Clip(d.y, r.yMax - a.y, ref t0, ref t1);
        }

        static bool Clip(float p, float q, ref float t0, ref float t1)
        {
            if (Mathf.Abs(p) < 1e-8f) return q >= 0f;
            float t = q / p;
            if (p < 0f)
            {
                if (t > t1) return false;
                if (t > t0) t0 = t;
            }
            else
            {
                if (t < t0) return false;
                if (t < t1) t1 = t;
            }
            return true;
        }
    }
}
