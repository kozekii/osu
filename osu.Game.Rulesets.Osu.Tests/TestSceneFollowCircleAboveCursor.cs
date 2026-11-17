// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Types;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Rulesets.Osu.Objects.Drawables;
using osu.Game.Rulesets.Osu.Skinning;
using osu.Game.Rulesets.Osu.UI;
using osu.Game.Skinning;
using osu.Game.Tests.Beatmaps;
using osu.Game.Tests.Visual;
using osuTK;

namespace osu.Game.Rulesets.Osu.Tests
{
    public partial class TestSceneFollowCircleAboveCursor : PlayerTestScene
    {
        protected override Ruleset CreatePlayerRuleset() => new OsuRuleset();

        protected override bool Autoplay => true;

        [Resolved]
        private FollowCircleManager followCircleManager { get; set; } = null!;

        private bool originalAboveCursor;

        protected override IBeatmap CreateBeatmap(RulesetInfo ruleset) => new TestBeatmap(ruleset)
        {
            HitObjects =
            {
                new Slider
                {
                    StartTime = 1500,
                    Position = new Vector2(100, 192),
                    SliderVelocityMultiplier = 0.25,
                    Path = new SliderPath(PathType.LINEAR, new[]
                    {
                        Vector2.Zero,
                        new Vector2(300, 0),
                    })
                },
                new Slider
                {
                    StartTime = 5000,
                    Position = new Vector2(400, 192),
                    SliderVelocityMultiplier = 0.25,
                    Path = new SliderPath(PathType.LINEAR, new[]
                    {
                        Vector2.Zero,
                        new Vector2(-300, 0),
                    })
                },
            }
        };

        [Test]
        public void TestFollowCircleLayerOrderAboveCursor()
        {
            AddStep("store original setting", () => originalAboveCursor = followCircleManager.AboveCursor.Value);
            AddStep("enable follow circle above cursor", () => followCircleManager.AboveCursor.Value = true);

            AddUntilStep("wait until first slider follow circle is visible", () =>
                Player.ChildrenOfType<DrawableSlider>().Any(s =>
                    s.Time.Current >= 1600
                    && s.Ball.Alpha > 0
                    && s.Tracking.Value
                    && s.Ball.FollowCircle.ChildrenOfType<FollowCircle>().Any(f => f.IsPresent)));

            AddAssert("FollowCircle draws after Cursor in Player DrawNode tree (slider 1)", () =>
                assertFollowCircleDrawnAfterCursorInPlayerTree(sentinelFrame: 999_001UL));

            AddStep("disable follow circle above cursor", () => followCircleManager.AboveCursor.Value = false);

            AddAssert("follow circle moved back under SliderBall", () =>
            {
                var slider = Player.ChildrenOfType<DrawableSlider>().First(s => s.Ball.Alpha > 0);
                var followCircle = slider.Ball.FollowCircle;

                return ((Container)followCircle.ProxiedLayer).Count == 0
                       && followCircle.ChildrenOfType<FollowCircle>().Any();
            });

            AddStep("re-enable for second slider", () => followCircleManager.AboveCursor.Value = true);

            AddUntilStep("wait until second slider follow circle is visible", () =>
                Player.ChildrenOfType<DrawableSlider>().Any(s =>
                    s.HitObject?.StartTime >= 5000
                    && s.Time.Current >= 5100
                    && s.Alpha > 0
                    && s.Ball.Alpha > 0
                    && s.Tracking.Value
                    && s.Ball.FollowCircle.ChildrenOfType<FollowCircle>().Any(f => f.IsPresent)));

            AddAssert("FollowCircle draws after Cursor in Player DrawNode tree (slider 2)", () =>
                assertFollowCircleDrawnAfterCursorInPlayerTree(sentinelFrame: 999_002UL, minStartTime: 5000));

            AddStep("restore original setting", () => followCircleManager.AboveCursor.Value = originalAboveCursor);
        }

        private bool assertFollowCircleDrawnAfterCursorInPlayerTree(ulong sentinelFrame, double minStartTime = 0)
        {
            var playfield = Player.ChildrenOfType<OsuPlayfield>().Single();
            var cursor = playfield.Cursor!;
            var overlay = playfield.CursorOverlay!;
            var slider = Player.ChildrenOfType<DrawableSlider>().First(s =>
                s.Alpha > 0
                && s.Ball.Alpha > 0
                && (s.HitObject?.StartTime ?? 0) >= minStartTime);
            var followCircle = slider.Ball.FollowCircle;
            var proxiedLayer = followCircle.ProxiedLayer;
            var followCircleDrawable = ((Container)proxiedLayer).Children[0].ChildrenOfType<FollowCircle>().First();

            Assert.That(cursor.HasProxy, Is.True);
            Assert.That(overlay.HasProxy, Is.True);
            Assert.That(proxiedLayer.HasProxy, Is.True);
            Assert.That(followCircleDrawable.IsPresent, Is.True);

            // Generate the full Player DrawNode subtree at treeIndex = 2 just like DrawThread does.
            const int tree_index = 2;
            var genMethod = typeof(Drawable).GetMethod("GenerateDrawNodeSubtree", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(genMethod, Is.Not.Null);

            var rootNode = (DrawNode?)genMethod!.Invoke(Player, new object[] { sentinelFrame, tree_index, false });
            Assert.That(rootNode, Is.Not.Null);

            // Flatten Player's effective draw order, resolving valid ProxyDrawNodes via getCurrentFrameSource().
            var drawOrder = new List<DrawNode>();
            collectEffectiveDrawOrder(rootNode!, drawOrder);

            var drawNodesField = typeof(Drawable).GetField("drawNodes", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var cursorNode = ((DrawNode?[])drawNodesField.GetValue(cursor)!)[tree_index];
            var overlayNode = ((DrawNode?[])drawNodesField.GetValue(overlay)!)[tree_index];
            var proxiedLayerNode = ((DrawNode?[])drawNodesField.GetValue(proxiedLayer)!)[tree_index];
            var fcNode = ((DrawNode?[])drawNodesField.GetValue(followCircleDrawable)!)[tree_index];

            Assert.That(cursorNode, Is.Not.Null);
            Assert.That(overlayNode, Is.Not.Null);
            Assert.That(proxiedLayerNode, Is.Not.Null);
            Assert.That(fcNode, Is.Not.Null);

            int cursorDrawPos = drawOrder.IndexOf(cursorNode!);
            int fcDrawPos = drawOrder.IndexOf(fcNode!);

            Assert.That(cursorDrawPos, Is.GreaterThanOrEqualTo(0), "Cursor DrawNode was not visited in Player's draw order.");
            Assert.That(fcDrawPos, Is.GreaterThanOrEqualTo(0), "FollowCircle DrawNode was not visited via Player's CursorOverlay proxy.");
            Assert.That(fcDrawPos, Is.GreaterThan(cursorDrawPos), "FollowCircle must draw AFTER Cursor in Player's render order.");

            return true;
        }

        private static void collectEffectiveDrawOrder(DrawNode node, List<DrawNode> output)
        {
            var type = node.GetType();

            if (type.Name == "ProxyDrawNode")
            {
                var getSource = type.GetMethod("getCurrentFrameSource", BindingFlags.Instance | BindingFlags.NonPublic);
                var resolved = getSource?.Invoke(node, null) as DrawNode;
                if (resolved != null)
                    collectEffectiveDrawOrder(resolved, output);
                return;
            }

            output.Add(node);

            if (node is ICompositeDrawNode composite && composite.Children != null)
            {
                foreach (var child in composite.Children)
                    collectEffectiveDrawOrder(child, output);
            }
        }
    }
}
