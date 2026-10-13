using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Toufuku.Scenery.EditorTools
{
    /*
        客のアニメーションをモデル（リグ）に関係なく作るための道具
          ・動き（CustomerMotions）は「キャラクターの向きでの回転（度）」で決めておく
              x がプラス: 頭・背骨は前へかがむ／脚・腕は後ろへ（マイナスで前へ）
              z がプラス: 右腕が外へ上がる（左腕はマイナス）
              y がプラス: 上から見て左回り（頭を左右にふる）
          ・CustomerRig が「どのボーンがどの部位か」と「基準の姿勢」を持っていて、
            クリップを書くときに基準の姿勢へ回転を重ねる（ボーンの軸の向きがモデルごとにちがっても同じ動きになる）
          ・Animator Controller のつなぎ方とパラメータ名は CustomerAnimatorBridge と合わせてある
    */
    public enum CustomerBone { Hips, Spine, Chest, Neck, Head, UpperArmR, LowerArmR, UpperArmL, LowerArmL, LegR, LegL }

    public class CustomerRig
    {
        // 部位 → Animator から見たパス
        public readonly Dictionary<CustomerBone, string> Paths = new Dictionary<CustomerBone, string>();
        // 基準の姿勢で、全部のボーン（パス → ローカル回転）。動きがないボーンもこの姿勢で止めておく
        public readonly Dictionary<string, Quaternion> BaseLocal = new Dictionary<string, Quaternion>();
        // 基準の姿勢で、部位ごとのモデル空間の回転
        public readonly Dictionary<CustomerBone, Quaternion> BaseModel = new Dictionary<CustomerBone, Quaternion>();
        public Vector3 HipsBasePosition;

        /*
            今の姿勢を基準にしてリグを作る
            animatorRoot: Animator を付ける Transform（パスの起点）
            skeletonRoot: ボーンのいちばん上（Hips かその親）。この下の全部のボーンを基準の姿勢として持つ
        */
        public static CustomerRig FromCurrentPose(Transform animatorRoot, Transform skeletonRoot, Dictionary<CustomerBone, string> paths)
        {
            var rig = new CustomerRig();
            foreach (var kv in paths) rig.Paths[kv.Key] = kv.Value;

            foreach (Transform t in skeletonRoot.GetComponentsInChildren<Transform>(true))
            {
                rig.BaseLocal[AnimationUtility.CalculateTransformPath(t, animatorRoot)] = t.localRotation;
            }
            Quaternion rootInv = Quaternion.Inverse(animatorRoot.rotation);
            foreach (var kv in paths)
            {
                Transform t = animatorRoot.Find(kv.Value);
                if (t == null) throw new System.ArgumentException($"ボーンが見つかりません: {kv.Value}");
                rig.BaseModel[kv.Key] = rootInv * t.rotation;
                if (kv.Key == CustomerBone.Hips) rig.HipsBasePosition = t.localPosition;
            }
            return rig;
        }
    }

    // 1つの部位の回転の動き（キーの時刻と、そのときの x/y/z の角度）
    public class MotionTrack
    {
        public CustomerBone Bone;
        public float[] Times;
        public float[] X, Y, Z;
    }

    public class CustomerMotion
    {
        public string Name;
        public bool Loop;
        public readonly List<MotionTrack> Tracks = new List<MotionTrack>();
        public float[] HipsTimes;
        public float[] HipsDy;   // 腰の上下（基準の高さからのずれ、m）

        public float Length
        {
            get
            {
                float l = 0f;
                foreach (var t in Tracks) l = Mathf.Max(l, t.Times[t.Times.Length - 1]);
                if (HipsTimes != null) l = Mathf.Max(l, HipsTimes[HipsTimes.Length - 1]);
                return l;
            }
        }

        public CustomerMotion Rot(CustomerBone bone, float[] t, float[] x = null, float[] y = null, float[] z = null)
        {
            Tracks.Add(new MotionTrack { Bone = bone, Times = t, X = x ?? Zeros(t.Length), Y = y ?? Zeros(t.Length), Z = z ?? Zeros(t.Length) });
            return this;
        }

        public CustomerMotion Bob(float[] t, float[] dy)
        {
            HipsTimes = t;
            HipsDy = dy;
            return this;
        }

        public static float[] Zeros(int n) => new float[n];

        public static float[] Const(int n, float v)
        {
            var a = new float[n];
            for (int i = 0; i < n; i++) a[i] = v;
            return a;
        }
    }

    // 客の動き。値は仮モデル（こけし）で見た目を合わせたもの
    public static class CustomerMotions
    {
        static float[] C(int n, float v) => CustomerMotion.Const(n, v);

        // 待機: 呼吸のようなゆれ
        public static CustomerMotion Idle()
        {
            float[] t = { 0f, 1.2f, 2.4f };
            return new CustomerMotion { Name = "Idle", Loop = true }
                .Rot(CustomerBone.Chest, t, x: new[] { 0f, 2f, 0f })
                .Rot(CustomerBone.Head, t, x: new[] { 0f, -1.5f, 0f })
                .Rot(CustomerBone.UpperArmR, t, z: new[] { 3f, 4.5f, 3f })
                .Rot(CustomerBone.UpperArmL, t, z: new[] { -3f, -4.5f, -3f })
                .Bob(t, new[] { 0f, -0.006f, 0f });
        }

        // 焦り: 足ぶみ・まわりを見回す（危険度 0.5→1 で待機から切りかわる）
        public static CustomerMotion Fidget()
        {
            float[] t = { 0f, 0.175f, 0.35f, 0.525f, 0.7f };
            return new CustomerMotion { Name = "Fidget", Loop = true }
                .Rot(CustomerBone.Hips, t, z: new[] { 0f, 3f, 0f, -3f, 0f })
                .Rot(CustomerBone.LegR, t, x: new[] { 0f, -16f, 0f, 0f, 0f })
                .Rot(CustomerBone.LegL, t, x: new[] { 0f, 0f, 0f, -16f, 0f })
                .Rot(CustomerBone.Chest, t, x: C(5, 5f), y: new[] { -6f, 0f, 6f, 0f, -6f })
                .Rot(CustomerBone.Head, t, y: new[] { -14f, 0f, 14f, 0f, -14f })
                .Rot(CustomerBone.UpperArmR, t, z: new[] { 4f, 8f, 4f, 8f, 4f })
                .Rot(CustomerBone.UpperArmL, t, z: new[] { -4f, -8f, -4f, -8f, -4f })
                .Bob(t, new[] { 0f, 0.012f, 0f, 0.012f, 0f });
        }

        // 歩き
        public static CustomerMotion Walk()
        {
            float[] t = { 0f, 0.2f, 0.4f, 0.6f, 0.8f };
            return new CustomerMotion { Name = "Walk", Loop = true }
                .Rot(CustomerBone.LegR, t, x: new[] { -22f, 0f, 22f, 0f, -22f })
                .Rot(CustomerBone.LegL, t, x: new[] { 22f, 0f, -22f, 0f, 22f })
                .Rot(CustomerBone.UpperArmR, t, x: new[] { 18f, 0f, -18f, 0f, 18f }, z: C(5, 4f))
                .Rot(CustomerBone.UpperArmL, t, x: new[] { -18f, 0f, 18f, 0f, -18f }, z: C(5, -4f))
                .Rot(CustomerBone.Chest, t, y: new[] { 5f, 0f, -5f, 0f, 5f })
                .Rot(CustomerBone.Hips, t, y: new[] { -5f, 0f, 5f, 0f, -5f })
                .Bob(t, new[] { 0f, -0.03f, 0f, -0.03f, 0f });
        }

        // 救済: とび上がって喜び、おじぎしてから帰る
        public static CustomerMotion Rescued()
        {
            float[] t = { 0f, 0.15f, 0.3f, 0.45f, 0.6f, 0.85f, 1.15f, 1.45f, 1.8f };
            return new CustomerMotion { Name = "Rescued", Loop = false }
                .Bob(t, new[] { 0f, 0.09f, 0f, 0.06f, 0f, 0f, 0f, 0f, 0f })
                .Rot(CustomerBone.UpperArmR, t, z: new[] { 3f, 70f, 70f, 70f, 70f, 3f, 3f, 3f, 3f })
                .Rot(CustomerBone.UpperArmL, t, z: new[] { -3f, -70f, -70f, -70f, -70f, -3f, -3f, -3f, -3f })
                .Rot(CustomerBone.Spine, t, x: new[] { 0f, 0f, 0f, 0f, 0f, 0f, 35f, 35f, 0f })
                .Rot(CustomerBone.Chest, t, x: new[] { 0f, 0f, 0f, 0f, 0f, 0f, 10f, 10f, 0f })
                .Rot(CustomerBone.Head, t, x: new[] { 0f, -12f, -12f, -6f, 0f, 0f, 10f, 10f, 0f });
        }

        // 黒客: 肩を落として、ときどき足を踏みならす
        public static CustomerMotion Black()
        {
            float[] t = { 0f, 0.25f, 0.4f, 0.55f, 1.25f, 1.4f, 1.55f, 2f };
            return new CustomerMotion { Name = "Black", Loop = true }
                .Rot(CustomerBone.Spine, t, x: C(8, 6f))
                .Rot(CustomerBone.Chest, t, x: C(8, 14f), z: new[] { 0f, 0f, 3f, 0f, 0f, -3f, 0f, 0f })
                .Rot(CustomerBone.Head, t, x: C(8, 18f))
                .Rot(CustomerBone.UpperArmR, t, x: C(8, 6f), z: C(8, 1f))
                .Rot(CustomerBone.UpperArmL, t, x: C(8, 6f), z: C(8, -1f))
                .Rot(CustomerBone.LegR, t, x: new[] { 0f, -18f, 0f, 0f, 0f, 0f, 0f, 0f })
                .Rot(CustomerBone.LegL, t, x: new[] { 0f, 0f, 0f, 0f, -18f, 0f, 0f, 0f })
                .Bob(t, C(8, -0.03f));
        }

        // うなずき・首ふり（加算の層で重ねる。最初と最後は基準の姿勢）
        public static CustomerMotion Nod()
        {
            float[] t = { 0f, 0.15f, 0.45f };
            return new CustomerMotion { Name = "Nod", Loop = false }
                .Rot(CustomerBone.Head, t, x: new[] { 0f, 20f, 0f })
                .Rot(CustomerBone.Chest, t, x: new[] { 0f, 6f, 0f });
        }

        public static CustomerMotion Shake()
        {
            float[] t = { 0f, 0.12f, 0.27f, 0.42f, 0.6f };
            return new CustomerMotion { Name = "Shake", Loop = false }
                .Rot(CustomerBone.Head, t, y: new[] { 0f, 22f, -22f, 14f, 0f });
        }
    }

    public static class CustomerAnimationKit
    {
        const float SampleRate = 30f;

        /*
            動きをリグに合わせてクリップに書く
              ・回転は「基準の姿勢のモデル空間の向き」に、キャラクターの向きでの回転を重ねて、ローカル回転に直す
                  local = 基準のlocal × 基準のmodel⁻¹ × 回転 × 基準のmodel
                親も回ると、子は「親の回転 → 自分の回転」の順に重なる
              ・動きがないボーンも基準の姿勢で書いておく。Write Defaults で別の姿勢（T ポーズなど）に戻らないように
                ただし additive（加算で重ねるクリップ）は動くボーンだけ書く
        */
        public static AnimationClip WriteClip(CustomerMotion motion, CustomerRig rig, string assetPath, bool additive = false)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(assetPath);
            if (clip == null)
            {
                clip = new AnimationClip();
                AssetDatabase.CreateAsset(clip, assetPath);
            }
            clip.ClearCurves();
            clip.name = System.IO.Path.GetFileNameWithoutExtension(assetPath);
            clip.frameRate = SampleRate;
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = motion.Loop;
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            float length = motion.Length;
            int frames = Mathf.Max(1, Mathf.RoundToInt(length * SampleRate));
            var animatedPaths = new HashSet<string>();

            foreach (MotionTrack track in motion.Tracks)
            {
                string path = rig.Paths[track.Bone];
                animatedPaths.Add(path);
                AnimationCurve cx = Smooth(track.Times, track.X), cy = Smooth(track.Times, track.Y), cz = Smooth(track.Times, track.Z);
                Quaternion baseLocal = rig.BaseLocal[path];
                Quaternion baseModel = rig.BaseModel[track.Bone];
                Quaternion baseModelInv = Quaternion.Inverse(baseModel);

                var keys = new Keyframe[4][];
                for (int c = 0; c < 4; c++) keys[c] = new Keyframe[frames + 1];
                Quaternion prev = baseLocal;
                for (int f = 0; f <= frames; f++)
                {
                    float time = f / SampleRate;
                    Quaternion delta = Quaternion.Euler(cx.Evaluate(time), cy.Evaluate(time), cz.Evaluate(time));
                    Quaternion q = baseLocal * baseModelInv * delta * baseModel;
                    if (Quaternion.Dot(prev, q) < 0f) q = new Quaternion(-q.x, -q.y, -q.z, -q.w);   // 4元数の符号を前のキーにそろえる（補間が遠回りしないように）
                    prev = q;
                    keys[0][f] = new Keyframe(time, q.x);
                    keys[1][f] = new Keyframe(time, q.y);
                    keys[2][f] = new Keyframe(time, q.z);
                    keys[3][f] = new Keyframe(time, q.w);
                }
                SetQuaternion(clip, path, keys);
            }

            if (motion.HipsTimes != null)
            {
                string path = rig.Paths[CustomerBone.Hips];
                AnimationCurve dy = Smooth(motion.HipsTimes, motion.HipsDy);
                Vector3 p = rig.HipsBasePosition;
                var ys = new Keyframe[frames + 1];
                for (int f = 0; f <= frames; f++)
                {
                    float time = f / SampleRate;
                    ys[f] = new Keyframe(time, p.y + dy.Evaluate(time));
                }
                clip.SetCurve(path, typeof(Transform), "m_LocalPosition.x", AnimationCurve.Constant(0f, length, p.x));
                clip.SetCurve(path, typeof(Transform), "m_LocalPosition.y", new AnimationCurve(ys));
                clip.SetCurve(path, typeof(Transform), "m_LocalPosition.z", AnimationCurve.Constant(0f, length, p.z));
            }

            if (!additive)
            {
                foreach (var kv in rig.BaseLocal)
                {
                    if (animatedPaths.Contains(kv.Key)) continue;
                    Quaternion q = kv.Value;
                    var keys = new Keyframe[4][];
                    keys[0] = new[] { new Keyframe(0f, q.x), new Keyframe(length, q.x) };
                    keys[1] = new[] { new Keyframe(0f, q.y), new Keyframe(length, q.y) };
                    keys[2] = new[] { new Keyframe(0f, q.z), new Keyframe(length, q.z) };
                    keys[3] = new[] { new Keyframe(0f, q.w), new Keyframe(length, q.w) };
                    SetQuaternion(clip, kv.Key, keys);
                }
            }

            EditorUtility.SetDirty(clip);
            return clip;
        }

        static void SetQuaternion(AnimationClip clip, string path, Keyframe[][] keys)
        {
            clip.SetCurve(path, typeof(Transform), "m_LocalRotation.x", new AnimationCurve(keys[0]));
            clip.SetCurve(path, typeof(Transform), "m_LocalRotation.y", new AnimationCurve(keys[1]));
            clip.SetCurve(path, typeof(Transform), "m_LocalRotation.z", new AnimationCurve(keys[2]));
            clip.SetCurve(path, typeof(Transform), "m_LocalRotation.w", new AnimationCurve(keys[3]));
        }

        static AnimationCurve Smooth(float[] times, float[] values)
        {
            var keys = new Keyframe[times.Length];
            for (int i = 0; i < times.Length; i++) keys[i] = new Keyframe(times[i], values[i]);
            var curve = new AnimationCurve(keys);
            for (int i = 0; i < keys.Length; i++)
            {
                AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.ClampedAuto);
                AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.ClampedAuto);
            }
            return curve;
        }

        /*
            客の Animator Controller を作る（パラメータ名は CustomerAnimatorBridge と同じ）
              Base 層: Stand（Danger で待機→焦り）／Walk／Rescued（喜び→おじぎ→歩き）／Black（終端）
              Reaction 層（加算）: None／Nod（Correct）／Shake（Wrong）
            idle と walk はデザイン班のクリップに差しかえられる
        */
        public static void BuildController(string path, Motion idle, Motion fidget, Motion walk, Motion rescued, Motion black, Motion nod, Motion shake)
        {
            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(path) != null) AssetDatabase.DeleteAsset(path);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            controller.AddParameter("IsWalking", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Danger", AnimatorControllerParameterType.Float);
            controller.AddParameter("Black", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Rescued", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Correct", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Wrong", AnimatorControllerParameterType.Trigger);

            AnimatorStateMachine sm = controller.layers[0].stateMachine;
            AnimatorState stand = controller.CreateBlendTreeInController("Stand", out BlendTree tree, 0);
            tree.blendParameter = "Danger";
            tree.useAutomaticThresholds = false;
            tree.AddChild(idle, 0f);
            tree.AddChild(idle, 0.5f);   // D<50 は落ちついている（D≥50 で危険円が出るのと合わせる）
            tree.AddChild(fidget, 1f);
            AnimatorState walkState = sm.AddState("Walk");
            walkState.motion = walk;
            AnimatorState rescuedState = sm.AddState("Rescued");
            rescuedState.motion = rescued;
            AnimatorState blackState = sm.AddState("Black");
            blackState.motion = black;
            sm.defaultState = stand;

            var toWalk = stand.AddTransition(walkState);
            toWalk.AddCondition(AnimatorConditionMode.If, 0f, "IsWalking");
            toWalk.hasExitTime = false;
            toWalk.duration = 0.15f;
            var toStand = walkState.AddTransition(stand);
            toStand.AddCondition(AnimatorConditionMode.IfNot, 0f, "IsWalking");
            toStand.hasExitTime = false;
            toStand.duration = 0.2f;

            var anyRescued = sm.AddAnyStateTransition(rescuedState);
            anyRescued.AddCondition(AnimatorConditionMode.If, 0f, "Rescued");
            anyRescued.canTransitionToSelf = false;
            anyRescued.hasExitTime = false;
            anyRescued.duration = 0.1f;
            var rescuedToWalk = rescuedState.AddTransition(walkState);
            rescuedToWalk.AddCondition(AnimatorConditionMode.If, 0f, "IsWalking");
            rescuedToWalk.hasExitTime = true;
            rescuedToWalk.exitTime = 1f;
            rescuedToWalk.duration = 0.2f;
            var rescuedToStand = rescuedState.AddTransition(stand);
            rescuedToStand.hasExitTime = true;
            rescuedToStand.exitTime = 1f;
            rescuedToStand.duration = 0.2f;

            var anyBlack = sm.AddAnyStateTransition(blackState);
            anyBlack.AddCondition(AnimatorConditionMode.If, 0f, "Black");
            anyBlack.canTransitionToSelf = false;
            anyBlack.hasExitTime = false;
            anyBlack.duration = 0.3f;

            controller.AddLayer("Reaction");
            AnimatorControllerLayer[] layers = controller.layers;
            layers[1].blendingMode = AnimatorLayerBlendingMode.Additive;
            layers[1].defaultWeight = 1f;
            controller.layers = layers;
            AnimatorStateMachine rsm = controller.layers[1].stateMachine;
            AnimatorState none = rsm.AddState("None");
            AnimatorState nodState = rsm.AddState("Nod");
            nodState.motion = nod;
            AnimatorState shakeState = rsm.AddState("Shake");
            shakeState.motion = shake;
            // 加算の層で既定値を書くと基準の姿勢が2重に足されるので、書かない
            foreach (var s in new[] { none, nodState, shakeState }) s.writeDefaultValues = false;
            rsm.defaultState = none;
            foreach (var (state, trigger) in new[] { (nodState, "Correct"), (shakeState, "Wrong") })
            {
                var any = rsm.AddAnyStateTransition(state);
                any.AddCondition(AnimatorConditionMode.If, 0f, trigger);
                any.hasExitTime = false;
                any.duration = 0.05f;
                var back = state.AddTransition(none);
                back.hasExitTime = true;
                back.exitTime = 1f;
                back.duration = 0.05f;
            }

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
        }
    }
}
