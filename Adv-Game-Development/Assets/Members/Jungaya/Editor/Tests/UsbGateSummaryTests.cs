using System.Collections.Generic;
using NUnit.Framework;
using Toufuku.Playtest;

namespace Toufuku.Playtest.Tests
{
    /// <summary>#52 17章 T6-USB の完了条件の判定（区間ごとに最後まで行った最後の回を使う）。</summary>
    public class UsbGateSummaryTests
    {
        sealed class Builder
        {
            public readonly List<UsbGateEvent> Events = new List<UsbGateEvent>();
            int _run;

            public UsbGateEvent Add(UsbGateSection section, string type, double t = 0.0, int participant = 0)
            {
                var e = new UsbGateEvent
                {
                    Run = _run,
                    Section = UsbGatePlan.KeyOf(section),
                    Participant = participant,
                    Event = type,
                    T = t
                };
                Events.Add(e);
                return e;
            }

            public void Begin(UsbGateSection section, int participant = 0, bool connected = true)
            {
                _run++;
                Add(section, UsbGateEventType.SectionStart, 0.0, participant);
                if (section == UsbGateSection.Safety) return;
                UsbGateEvent m = Add(section, UsbGateEventType.Metric, 0.0, participant);
                m.Label = UsbGateMetric.Connected;
                m.Value = connected ? 1 : 0;
            }

            public void End(UsbGateSection section, bool completed = true, int participant = 0)
            {
                UsbGateEvent e = Add(section, UsbGateEventType.SectionEnd, 999.0, participant);
                e.Flag = completed;
            }

            public void Metric(UsbGateSection section, string label, double value)
            {
                UsbGateEvent e = Add(section, UsbGateEventType.Metric);
                e.Label = label;
                e.Value = value;
            }

            public void Safety(int okItems)
            {
                Begin(UsbGateSection.Safety);
                for (int i = 0; i < UsbGatePlan.SafetyItems.Length; i++)
                {
                    UsbGateEvent e = Add(UsbGateSection.Safety, UsbGateEventType.SafetyItem);
                    e.Label = UsbGatePlan.SafetyItems[i].Id;
                    e.Flag = i < okItems;
                }
                End(UsbGateSection.Safety);
            }

            /// <summary>意図的 100 投。latencyMs = 入力 → 画面。missing = 応答しない合図の数。extra = 余分な発射。</summary>
            public void Throws(double latencyMs = 45.0, int missing = 0, int extra = 0, bool withInput = true,
                double? slowestMs = null, bool completed = true)
            {
                Begin(UsbGateSection.Throws100);
                for (int k = 0; k < UsbGatePlan.IntendedThrows; k++)
                {
                    double cue = 3.0 + k * 2.0;
                    UsbGateEvent c = Add(UsbGateSection.Throws100, UsbGateEventType.Cue, cue);
                    c.Seq = k + 1;
                    if (k < missing) continue;

                    double ms = slowestMs.HasValue && k == UsbGatePlan.IntendedThrows - 1 ? slowestMs.Value : latencyMs;
                    AddFire(UsbGateSection.Throws100, cue + 0.4, 1000.0 + k * 2.0, ms, withInput);
                }
                // 合図の窓（-0.3〜+1.2 秒）の外 = どの合図にも当たらない発射
                for (int i = 0; i < extra; i++) AddFire(UsbGateSection.Throws100, 3.0 + i * 2.0 + 1.5, 5000.0 + i, latencyMs, withInput);
                End(UsbGateSection.Throws100, completed);
            }

            void AddFire(UsbGateSection section, double t, double inputUnity, double latencyMs, bool withInput, string label = "", int participant = 0)
            {
                UsbGateEvent f = Add(section, UsbGateEventType.Fire, t, participant);
                f.Label = label;
                if (withInput)
                {
                    f.InputTime = inputUnity - 900.0;
                    f.InputUnity = inputUnity;
                }
                f.ReceiveTime = inputUnity + 0.008;
                f.FireTime = f.ReceiveTime + 0.0005;
                f.VisibleTime = inputUnity + latencyMs / 1000.0;
            }

            public void Drift(UsbGateSection section, double start, double end)
            {
                Begin(section);
                UsbGateEvent s = Add(section, UsbGateEventType.DriftSample, 0.0);
                s.Label = UsbGateBlock.DriftStart;
                s.Value = start;
                UsbGateEvent e = Add(section, UsbGateEventType.DriftSample, 180.0);
                e.Label = UsbGateBlock.DriftEnd;
                e.Value = end;
                End(section);
            }

            public void Load(double avg = 60.0, double low1 = 57.0, double renderedMin = 30)
            {
                Begin(UsbGateSection.Load);
                Metric(UsbGateSection.Load, UsbGateMetric.AverageFps, avg);
                Metric(UsbGateSection.Load, UsbGateMetric.OnePercentLowFps, low1);
                Metric(UsbGateSection.Load, UsbGateMetric.RenderedMin, renderedMin);
                End(UsbGateSection.Load);
            }

            public void Child(int participant, int nearHits = 8, int farHits = 7)
            {
                Begin(UsbGateSection.Children, participant);
                for (int i = 0; i < 10; i++)
                {
                    AddFire(UsbGateSection.Children, 130.0 + i, 2000.0 + i, 40.0, true, UsbGateBlock.Near, participant);
                    UsbGateEvent l = Add(UsbGateSection.Children, UsbGateEventType.Landing, 130.5 + i, participant);
                    l.Label = UsbGateBlock.Near;
                    l.Flag = i < nearHits;
                }
                for (int i = 0; i < 10; i++)
                {
                    AddFire(UsbGateSection.Children, 150.0 + i, 3000.0 + i, 40.0, true, UsbGateBlock.Far, participant);
                    UsbGateEvent l = Add(UsbGateSection.Children, UsbGateEventType.Landing, 150.5 + i, participant);
                    l.Label = UsbGateBlock.Far;
                    l.Flag = i < farHits;
                }
                End(UsbGateSection.Children, true, participant);
            }

            public Builder Passing()
            {
                Safety(UsbGatePlan.SafetyItems.Length);
                Throws();
                Drift(UsbGateSection.DriftStatic, 0.500, 0.520);
                Drift(UsbGateSection.DriftOperate, 0.500, 0.470);
                Load();
                for (int p = 1; p <= 5; p++) Child(p);
                return this;
            }
        }

        static UsbGateSummary Of(Builder b) => UsbGateSummary.Of(b.Events);

        static AbCriterion Criterion(UsbGateSummary s, string name)
        {
            foreach (UsbGateCriterion c in s.Criteria)
            {
                if (c.Criterion.Name == name) return c.Criterion;
            }
            Assert.Fail($"判定「{name}」がありません");
            return default;
        }

        [Test]
        public void 典型的な一回分は合格()
        {
            UsbGateSummary s = Of(new Builder().Passing());

            Assert.IsTrue(s.Passed, s.FailureSummary());
            Assert.AreEqual(100, s.Latency.Count);
            Assert.IsTrue(s.Latency.InputBased);
            Assert.AreEqual(45.0, s.Latency.P95Ms.Value, 1e-6);
            Assert.AreEqual(0, s.Intent.Value.Missed);
            Assert.AreEqual(0.02, s.DriftStatic.Value, 1e-9);
            Assert.AreEqual(-0.03, s.DriftOperate.Value, 1e-9);
            Assert.AreEqual(5, s.Children);
            Assert.AreEqual(40, s.NearHits);
            Assert.AreEqual(50, s.NearThrows);
            Assert.AreEqual(35, s.FarHits);
            Assert.AreEqual(9, s.ControllerRuns);
        }

        [Test]
        public void 何もしていなければ全部不合格()
        {
            UsbGateSummary s = UsbGateSummary.Of(new List<UsbGateEvent>());
            Assert.IsFalse(s.Passed);
            foreach (UsbGateCriterion c in s.Criteria)
            {
                if (c.Criterion.Name == "接触／逸脱") continue; // 事象 0 件は満たしている
                Assert.IsFalse(c.Criterion.Passed, c.Criterion.Name);
            }
        }

        [Test]
        public void 安全チェックに不適合が一つでもあれば不合格()
        {
            var b = new Builder();
            b.Safety(UsbGatePlan.SafetyItems.Length - 1);
            Assert.IsFalse(Criterion(Of(b), "安全チェック全項目適合").Passed);
        }

        [Test]
        public void 中断した回の安全事象も数える()
        {
            Builder b = new Builder().Passing();
            b.Begin(UsbGateSection.Children, 6);
            UsbGateEvent e = b.Add(UsbGateSection.Children, UsbGateEventType.Incident, 10.0, 6);
            e.Label = UsbGateBlock.Deviation;
            b.End(UsbGateSection.Children, false, 6);

            UsbGateSummary s = Of(b);
            Assert.AreEqual(1, s.Incidents);
            Assert.IsFalse(s.Passed);
            Assert.AreEqual(5, s.Children, "中断した参加者は命中の集計に入れない");
        }

        [Test]
        public void 遅延の境目_p95と最大()
        {
            var ok = new Builder();
            ok.Throws(latencyMs: 80.0, slowestMs: 100.0);
            UsbGateSummary s = Of(ok);
            Assert.IsTrue(Criterion(s, "入力遅延 p95").Passed, "80ms ちょうどは合格");
            Assert.IsTrue(Criterion(s, "入力遅延 最大").Passed, "100ms ちょうどは合格");

            var slow = new Builder();
            slow.Throws(latencyMs: 81.0);
            Assert.IsFalse(Criterion(Of(slow), "入力遅延 p95").Passed);

            var spike = new Builder();
            spike.Throws(latencyMs: 50.0, slowestMs: 101.0);
            UsbGateSummary sp = Of(spike);
            Assert.IsTrue(Criterion(sp, "入力遅延 p95").Passed);
            Assert.IsFalse(Criterion(sp, "入力遅延 最大").Passed);
        }

        [Test]
        public void 入力時刻が無ければ受信時刻から測って暫定と出す()
        {
            var b = new Builder();
            b.Throws(latencyMs: 45.0, withInput: false);
            UsbGateSummary s = Of(b);

            Assert.IsFalse(s.Latency.InputBased);
            Assert.AreEqual(37.0, s.Latency.P95Ms.Value, 1e-6); // 受信は入力の 8ms 後
            StringAssert.Contains("暫定", Criterion(s, "入力遅延 p95").Actual);
        }

        [Test]
        public void 欠落は二パーセント未満_誤発射は二パーセント以下()
        {
            var one = new Builder();
            one.Throws(missing: 1, extra: 2);
            UsbGateSummary s1 = Of(one);
            Assert.IsTrue(Criterion(s1, "意図的入力の欠落").Passed);
            Assert.IsTrue(Criterion(s1, "誤発射").Passed);

            var two = new Builder();
            two.Throws(missing: 2, extra: 3);
            UsbGateSummary s2 = Of(two);
            Assert.IsFalse(Criterion(s2, "意図的入力の欠落").Passed);
            Assert.IsFalse(Criterion(s2, "誤発射").Passed);
        }

        [Test]
        public void やり直した区間は最後まで行った最後の回で判定する()
        {
            var b = new Builder();
            b.Throws(latencyMs: 120.0);                 // 1 回目: 遅い
            b.Throws(latencyMs: 40.0, completed: false); // 2 回目: 中断（使わない）
            b.Throws(latencyMs: 50.0);                  // 3 回目: これを使う
            Assert.AreEqual(50.0, Of(b).Latency.P95Ms.Value, 1e-6);
        }

        [Test]
        public void 切断が一回でも_未接続の区間があっても不合格()
        {
            Builder cut = new Builder().Passing();
            cut.Begin(UsbGateSection.Load);
            cut.Add(UsbGateSection.Load, UsbGateEventType.Disconnect, 20.0).Value = 0.7;
            cut.Metric(UsbGateSection.Load, UsbGateMetric.AverageFps, 60.0);
            cut.Metric(UsbGateSection.Load, UsbGateMetric.OnePercentLowFps, 57.0);
            cut.Metric(UsbGateSection.Load, UsbGateMetric.RenderedMin, 30);
            cut.End(UsbGateSection.Load);
            UsbGateSummary s = Of(cut);
            Assert.AreEqual(1, s.Disconnects);
            Assert.IsFalse(Criterion(s, "切断").Passed);

            var desk = new Builder();
            desk.Begin(UsbGateSection.Throws100, connected: false);
            desk.End(UsbGateSection.Throws100);
            UsbGateSummary d = Of(desk);
            Assert.AreEqual(1, d.UnconnectedRuns);
            Assert.IsFalse(Criterion(d, "切断").Passed);
        }

        [Test]
        public void ドリフトは静止と操作の両方が五パーセント以内()
        {
            var b = new Builder();
            b.Drift(UsbGateSection.DriftStatic, 0.50, 0.55);
            b.Drift(UsbGateSection.DriftOperate, 0.50, 0.449);
            UsbGateSummary s = Of(b);
            Assert.IsFalse(Criterion(s, "3 分ドリフト（静止／操作）").Passed, "操作 5.1% で不合格");

            var only = new Builder();
            only.Drift(UsbGateSection.DriftStatic, 0.50, 0.50);
            Assert.IsFalse(Criterion(Of(only), "3 分ドリフト（静止／操作）").Passed, "片方だけでは判定できない");
        }

        [Test]
        public void 描画の境目()
        {
            var ok = new Builder();
            ok.Load(avg: 59.0, low1: 55.0, renderedMin: 30);
            UsbGateSummary s = Of(ok);
            Assert.IsTrue(Criterion(s, "平均 fps（30 体負荷）").Passed, "59.94Hz 相当の丸めを許容");
            Assert.IsTrue(Criterion(s, "1% low（30 体負荷）").Passed);
            Assert.IsTrue(Criterion(s, "負荷条件（描画体数の最小）").Passed);

            var bad = new Builder();
            bad.Load(avg: 58.9, low1: 54.9, renderedMin: 29);
            UsbGateSummary b = Of(bad);
            Assert.IsFalse(Criterion(b, "平均 fps（30 体負荷）").Passed);
            Assert.IsFalse(Criterion(b, "1% low（30 体負荷）").Passed);
            Assert.IsFalse(Criterion(b, "負荷条件（描画体数の最小）").Passed);
        }

        [Test]
        public void 子どもは合計で近七割_遠六割_五人に満たなければ不合格()
        {
            var edge = new Builder();
            for (int p = 1; p <= 5; p++) edge.Child(p, nearHits: 7, farHits: 6);
            UsbGateSummary s = Of(edge);
            Assert.IsTrue(Criterion(s, "近の命中").Passed, "35/50 ちょうどは合格");
            Assert.IsTrue(Criterion(s, "遠の命中").Passed, "30/50 ちょうどは合格");

            var low = new Builder();
            for (int p = 1; p <= 5; p++) low.Child(p, nearHits: p == 1 ? 6 : 7, farHits: 6);
            Assert.IsFalse(Criterion(Of(low), "近の命中").Passed, "34/50 は不合格");

            var few = new Builder();
            for (int p = 1; p <= 4; p++) few.Child(p, nearHits: 10, farHits: 10);
            Assert.IsFalse(Criterion(Of(few), "近の命中").Passed);
        }

        [Test]
        public void 不合格の判定には仕様書の処置が付く()
        {
            var b = new Builder();
            b.Drift(UsbGateSection.DriftStatic, 0.5, 0.6);
            b.Drift(UsbGateSection.DriftOperate, 0.5, 0.5);
            foreach (UsbGateCriterion c in Of(b).Criteria)
            {
                if (c.Criterion.Name != "3 分ドリフト（静止／操作）") continue;
                StringAssert.Contains("相対姿勢", c.Remedy);
                StringAssert.Contains("相対姿勢", c.ToString());
            }
        }
    }
}
