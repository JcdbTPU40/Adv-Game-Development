using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Toufuku.Playtest;

namespace Toufuku.Rescue.Tests
{
    /// <summary>
    /// 候補集めから対象決定までを、実際の客（CustomerState + HitZoneTarget）で通す — Issue #55
    ///
    /// <see cref="PriorityTargetTests"/> が順序の規則そのものを見るのに対し、ここは
    /// 「誰を候補に入れるか」（active・未救済・非黒客・R&gt;0 だけ）と「必ず 1 人に定まるか」を見る。
    /// 画面内判定にはこのテスト専用のカメラを使う（開いているシーンのカメラに結果を左右されないため）。
    /// 再生していないエディタでは <c>OnEnable</c> が走らず <c>HitZoneTarget.Active</c> に登録されないので、
    /// 候補の一覧だけ <c>PriorityRescue.TargetSource</c> で差し込む（絞り込みと順序は本番と同じ道を通る）。
    /// </summary>
    public class PriorityRescueSelectionTests
    {
        readonly List<GameObject> _spawned = new List<GameObject>();
        readonly List<HitZoneTarget> _targets = new List<HitZoneTarget>();
        GameObject _cameraGo;

        [SetUp]
        public void SetUp()
        {
            CustomerSpawnId.ResetSequence();

            // 画面内判定に使うカメラをこのテスト専用に用意する
            // （開いているシーンのカメラに引きずられないようにする）。原点から +Z を見る。
            _cameraGo = new GameObject("PriorityRescueTestCamera");
            Camera camera = _cameraGo.AddComponent<Camera>();
            camera.transform.SetPositionAndRotation(new Vector3(0f, 1.5f, -2f), Quaternion.identity);
            camera.fieldOfView = 70f;
            camera.enabled = false;   // 描画は要らない。WorldToViewportPoint だけ使う

            PriorityRescue.CameraOverride = camera;
            PriorityRescue.OriginOverride = Vector3.zero;
            PriorityRescue.TargetSource = () => _targets;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in _spawned)
            {
                if (go != null) Object.DestroyImmediate(go);
            }
            _spawned.Clear();

            _targets.Clear();

            if (_cameraGo != null) Object.DestroyImmediate(_cameraGo);
            PriorityRescue.CameraOverride = null;
            PriorityRescue.OriginOverride = null;
            PriorityRescue.TargetSource = null;
        }

        /// <summary>原点から distance だけ離れた位置に、危険度 danger の客を 1 人置く。</summary>
        CustomerState Spawn(float danger, float distance)
        {
            var go = new GameObject($"Customer_{_spawned.Count + 1}");
            go.transform.position = new Vector3(0f, 0f, distance);
            _spawned.Add(go);

            _targets.Add(go.AddComponent<HitZoneTarget>());
            var state = go.AddComponent<CustomerState>();
            state.SetDangerForDebug(danger);

            CustomerSpawnId.Assign(go);
            return state;
        }

        [Test]
        public void 候補が居なければ対象は決まらない()
        {
            Assert.IsNull(PriorityRescue.Refresh());
        }

        [Test]
        public void Dが最大の客が二重円になる()
        {
            Spawn(danger: 20f, distance: 6f);   // ID 1
            Spawn(danger: 70f, distance: 6f);   // ID 2
            Spawn(danger: 45f, distance: 6f);   // ID 3

            Assert.AreEqual(2, PriorityRescue.Refresh());
        }

        [Test]
        public void 同じDの客が3人でも必ず1人に定まる()
        {
            // #55 完了条件「同時刻に同じ D の客が複数いても二重円が必ず 1 人に定まる」。
            // D も距離も active 化時刻も同じなので、最後の条件（生成ID 昇順）で決まる。
            Spawn(danger: 64f, distance: 8f);   // ID 1
            Spawn(danger: 64f, distance: 8f);   // ID 2
            Spawn(danger: 64f, distance: 8f);   // ID 3

            int? first = PriorityRescue.Refresh();
            int? second = PriorityRescue.Refresh();

            Assert.AreEqual(1, first);
            Assert.AreEqual(first, second, "同じ状況なら何度数え直しても同じ 1 人");
        }

        [Test]
        public void D同値なら遠い客が二重円になる()
        {
            Spawn(danger: 50f, distance: 5f);    // ID 1（近い）
            Spawn(danger: 50f, distance: 14f);   // ID 2（遠い）

            Assert.AreEqual(2, PriorityRescue.Refresh());
        }

        [Test]
        public void 救済済みの客は候補から外れる()
        {
            CustomerState high = Spawn(danger: 80f, distance: 6f);   // ID 1
            Spawn(danger: 30f, distance: 6f);                        // ID 2

            // 退場コルーチンは再生中でないと回せないので、状態遷移だけを進める
            // （CustomerState.ApplyCorrectColorHit と同じ遷移。R=1 → 0 で救済完了）。
            high.Machine.HitCorrectColor();

            Assert.IsTrue(high.IsRescued);
            Assert.AreEqual(2, PriorityRescue.Refresh(), "退場中の救済客ではなく、生きている客に移ること");
        }

        [Test]
        public void 黒客は候補から外れる()
        {
            CustomerState doomed = Spawn(danger: CustomerStateMachine.MaxDanger, distance: 6f);  // ID 1
            Spawn(danger: 30f, distance: 6f);                                                    // ID 2

            doomed.Machine.ResolveBlackout();

            Assert.IsTrue(doomed.IsBlack);
            Assert.AreEqual(2, PriorityRescue.Refresh(), "黒客を候補に残すと「黒客が多いほど得」になる");
        }

        [Test]
        public void D閾値は無い_全員が低くても二重円は1人に出る()
        {
            // 付録B PRIORITY.MARK：D 閾値なし。ここに閾値を作ると「待って稼ぐ」が復活する。
            Spawn(danger: 3f, distance: 6f);    // ID 1
            Spawn(danger: 9f, distance: 6f);    // ID 2

            Assert.AreEqual(2, PriorityRescue.Refresh());
        }

        [Test]
        public void 優先対象かどうかはIDで答える()
        {
            Spawn(danger: 10f, distance: 6f);   // ID 1
            Spawn(danger: 90f, distance: 6f);   // ID 2

            PriorityRescue.Refresh();

            Assert.IsTrue(PriorityRescue.IsPriority(2));
            Assert.IsFalse(PriorityRescue.IsPriority(1));
            Assert.IsFalse(PriorityRescue.IsPriority(PriorityRescue.NoTarget));
        }
    }
}
