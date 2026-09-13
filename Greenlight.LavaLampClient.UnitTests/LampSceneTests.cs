using Greenlight.LavaLampClient;

namespace Greenlight.LavaLampClient.UnitTests;

/// <summary>
/// The scene, without a window. Everything here is arithmetic somebody would otherwise have to
/// check by dragging a lamp around a desktop and squinting at it — or, for the wax, by watching
/// one for twenty minutes to see whether a blob eventually leaves through the side of the glass.
/// </summary>
public class LampSceneTests
{
    private static LampScene Scene(double size = 0.34, double x = 0.5, double y = 0.5) =>
        new(new LampPlacement { AnchorX = x, AnchorY = y, Size = size }, randomSeed: 7)
        {
            ShowWhenOff = false,
        };

    private static LampScene Sized(double size = 0.34, double x = 0.5, double y = 0.5)
    {
        var scene = Scene(size, x, y);
        scene.Resize(1600, 900);
        return scene;
    }

    private static LampScene Lit(LampState state = LampState.Green, double size = 0.34)
    {
        var scene = Sized(size);
        scene.State = state;
        return scene;
    }

    private static void Run(LampScene scene, double seconds, double step = 1.0 / 60)
    {
        for (var t = 0.0; t < seconds; t += step) scene.Advance(TimeSpan.FromSeconds(step));
    }

    // ── states and the changeover ─────────────────────────────────────────────

    [Fact]
    public void FirstStateArrivesWithoutAChangeover()
    {
        var scene = Sized();

        // Nothing on screen to put away, so the first snapshot after a cold start is simply worn
        // rather than waited for.
        scene.State = LampState.Green;

        Assert.Equal(LampState.Green, scene.Shown);
        Assert.False(scene.IsChangingOver);
    }

    [Fact]
    public void AChangeOfStateFadesTheOldOneOutFirst()
    {
        var scene = Lit();
        Run(scene, 1);

        scene.State = LampState.Red;

        // Still green on the very next frame: the wax must not change colour where it stands.
        scene.Advance(TimeSpan.FromSeconds(1.0 / 60));
        Assert.Equal(LampState.Green, scene.Shown);
        Assert.True(scene.IsChangingOver);

        Run(scene, 2);
        Assert.Equal(LampState.Red, scene.Shown);
        Assert.False(scene.IsChangingOver);
    }

    [Fact]
    public void NothingToShowFadesAllTheWayOut()
    {
        var scene = Lit();
        Run(scene, 1);

        scene.State = LampState.Off;
        Run(scene, 3);

        Assert.Equal(0, scene.Fade);
    }

    [Fact]
    public void AnUnlitLampStaysOnScreenWhenAskedTo()
    {
        var scene = Sized();
        scene.ShowWhenOff = true;

        Run(scene, 2);

        Assert.Equal(1, scene.Fade);
        Assert.Equal(LampState.Off, scene.Shown);

        // And it is genuinely off rather than quietly running: the whole point of drawing it at
        // all is that "no Greenlight" looks different from "nothing wrong".
        Assert.False(scene.IsLit);
        Assert.Equal(0, scene.Warmth);
    }

    // ── the bulb ──────────────────────────────────────────────────────────────

    [Fact]
    public void ALampTakesAMomentToWarmUp()
    {
        var scene = Lit();

        Run(scene, 1);
        var early = scene.Warmth;
        Assert.InRange(early, 0.01, 0.5);

        Run(scene, 10);
        Assert.Equal(1, scene.Warmth);
    }

    [Fact]
    public void LosingGreenlightLetsTheWaxSettleRatherThanDroppingIt()
    {
        var scene = Lit();
        Run(scene, 10);

        scene.State = LampState.Off;

        // A frame later it is on its way down, not already on the floor.
        Run(scene, 0.5);
        Assert.InRange(scene.Warmth, 0.5, 0.99);

        Run(scene, 5);
        Assert.Equal(0, scene.Warmth);
    }

    [Fact]
    public void RedComesUpToARollingBoilAndStaysThere()
    {
        var scene = Lit(LampState.Red);

        Assert.Equal(0, scene.Boil);

        Run(scene, 0.5);
        Assert.InRange(scene.Boil, 0.01, 0.99);

        Run(scene, 4);
        Assert.Equal(1, scene.Boil);
    }

    [Fact]
    public void FixingThePipelineLetsTheBoilDieDown()
    {
        var scene = Lit(LampState.Red);
        Run(scene, 4);

        scene.State = LampState.Green;
        Run(scene, 4);

        Assert.Equal(0, scene.Boil);
        Assert.Equal(LampState.Green, scene.Shown);
    }

    [Fact]
    public void ABoilingLampRunsItsWaxHarderThanAPassingOne()
    {
        var calm = Lit();
        var broken = Lit(LampState.Red);

        Run(calm, 12);
        Run(broken, 12);

        var calmPhase = calm.Blobs[0].Phase;
        var brokenPhase = broken.Blobs[0].Phase;

        // Same seed, same blob, same amount of time: the only difference is the heat under it.
        Assert.NotEqual(calmPhase, brokenPhase, 4);
    }

    // ── the build pulse ───────────────────────────────────────────────────────

    [Fact]
    public void NothingBreathesUnlessABuildIsRunning()
    {
        var scene = Lit();
        Run(scene, 1);

        Assert.Equal(1, scene.Pulse);
        Assert.Equal(1, scene.Brightness, 6);
    }

    [Fact]
    public void ABuildMakesTheGlowSwellAndSettle()
    {
        var scene = Lit();
        scene.IsBuilding = true;

        var low = double.MaxValue;
        var high = double.MinValue;

        // Two full breaths, sampled every frame. The glow carries most of the pulse, so this is
        // the number that has to actually move.
        for (var i = 0; i < 240; i++)
        {
            scene.Advance(TimeSpan.FromSeconds(1.0 / 60));
            low = Math.Min(low, scene.Halo);
            high = Math.Max(high, scene.Halo);
        }

        Assert.True(high - low > 0.3, $"the glow barely moved: {low:F2} to {high:F2}");
        Assert.InRange(scene.Brightness, 0.7, 1.0);
    }

    // ── the wax ───────────────────────────────────────────────────────────────

    [Fact]
    public void ColdWaxLiesOnTheFloorOfTheBottle()
    {
        var scene = Sized();
        scene.ShowWhenOff = true;
        Run(scene, 2);

        var layout = scene.Measure();

        foreach (var blob in scene.Blobs)
        {
            var placed = scene.Place(blob, layout);

            // Resting on the bottom of the glass, whatever part of its own cycle it happens to be
            // in: a lamp that is off has no heat to lift anything with.
            Assert.Equal(layout.Glass.Bottom, placed.Bottom, 3);
        }
    }

    [Fact]
    public void WarmWaxActuallyGetsUpTheBottle()
    {
        var scene = Lit();
        Run(scene, 20);

        var layout = scene.Measure();
        var highest = scene.Blobs.Min(blob => scene.Place(blob, layout).Top);

        // Something has to have left the floor. Every blob is on its own cycle, so this is not
        // about any one of them — it is about the lamp being a lava lamp at all.
        Assert.True(
            highest < layout.Glass.Bottom - layout.Glass.Height * 0.25,
            $"nothing rose: the highest wax reached {highest:F1} in a bottle from {layout.Glass.Top:F1}");
    }

    [Fact]
    public void NoBlobEverLeavesTheGlass()
    {
        // The one that matters. The bottle tapers, so a blob that fits near the foot is wider
        // than the neck it is climbing towards — and wax poking out through the side of the glass
        // is the single fault that would make the whole toy look broken.
        var scene = Lit(LampState.Red, size: 0.5);

        for (var frame = 0; frame < 60 * 120; frame++)
        {
            scene.Advance(TimeSpan.FromSeconds(1.0 / 60));
            if (frame % 7 != 0) continue;

            var layout = scene.Measure();
            var glass = layout.Glass;

            foreach (var blob in scene.Blobs)
            {
                var placed = scene.Place(blob, layout);

                Assert.True(placed.Top >= glass.Top - 0.001, $"wax above the neck at {placed.Top:F2}");
                Assert.True(placed.Bottom <= glass.Bottom + 0.001, $"wax below the floor at {placed.Bottom:F2}");

                // Measured at the blob's own middle, which is where it is widest.
                var half = glass.HalfWidthAt(placed.Centre.Y);
                var reach = Math.Abs(placed.Centre.X - glass.CentreX) + placed.RadiusX;

                Assert.True(reach <= half + 0.001, $"wax through the side of the glass: {reach:F2} of {half:F2}");
            }
        }
    }

    [Fact]
    public void TheWaxKeepsMovingWhileTheLampIsCold()
    {
        // Not the same thing as rising. A bottle frozen absolutely still reads as the app having
        // hung, so the pool on the floor still shifts about even with no heat under it.
        var scene = Sized();
        scene.ShowWhenOff = true;

        var before = scene.Blobs[0].SwayPhase;
        Run(scene, 4);

        Assert.NotEqual(before, scene.Blobs[0].SwayPhase, 4);
    }

    [Fact]
    public void EveryBlobIsItsOwnBlob()
    {
        var scene = Sized();

        Assert.True(scene.Blobs.Count > 1);
        Assert.True(
            scene.Blobs.Select(b => b.Speed).Distinct().Count() > 1,
            "every blob has the same speed, so they will rise and fall as one lump");
    }

    // ── where it sits ─────────────────────────────────────────────────────────

    [Fact]
    public void TheLampIsTallerThanItIsWideAndCentredOnTheAnchor()
    {
        var scene = Sized(size: 0.4, x: 0.25, y: 0.75);
        var box = scene.Measure().Box;

        Assert.Equal(360, box.Height, 6);                            // 0.4 of 900
        Assert.Equal(360 * LampScene.Aspect, box.Width, 6);
        Assert.Equal(400, box.Centre.X, 6);                          // 0.25 of 1600
        Assert.Equal(675, box.Centre.Y, 6);                          // 0.75 of 900
    }

    [Fact]
    public void TheLampIsCapThenBottleThenBaseWithNoGaps()
    {
        var layout = Sized().Measure();

        Assert.Equal(layout.Box.Y, layout.Cap.Top, 6);
        Assert.Equal(layout.Cap.Bottom, layout.Glass.Top, 6);
        Assert.Equal(layout.Glass.Bottom, layout.Stand.Top, 6);
        Assert.Equal(layout.Box.Bottom, layout.Stand.Bottom, 6);

        // And it is a bottle rather than a tube: narrow at the neck, wide at the foot.
        Assert.True(layout.Glass.HalfTop < layout.Glass.HalfBottom);
    }

    [Fact]
    public void DraggingKeepsTheWholeLampOnTheDesktop()
    {
        var scene = Sized();

        scene.MoveTo(new ScenePoint(-500, -500));
        var box = scene.Measure().Box;

        Assert.True(box.X >= -0.001, $"left edge off screen at {box.X}");
        Assert.True(box.Y >= -0.001, $"top edge off screen at {box.Y}");

        scene.MoveTo(new ScenePoint(9000, 9000));
        box = scene.Measure().Box;

        Assert.True(box.Right <= 1600.001, $"right edge off screen at {box.Right}");
        Assert.True(box.Bottom <= 900.001, $"bottom edge off screen at {box.Bottom}");
    }

    [Fact]
    public void ResizingHoldsTheOppositeCornerStill()
    {
        var scene = Sized(size: 0.3, x: 0.5, y: 0.5);
        var before = scene.Measure().Box;

        // Dragged mostly downwards: the height the mouse asks for directly is the taller of the
        // two readings, so it is the one the lamp takes.
        scene.ResizeTo(LampGrip.ResizeBottomRight, new ScenePoint(before.X + 120, before.Y + 400));
        var after = scene.Measure().Box;

        Assert.Equal(before.X, after.X, 3);
        Assert.Equal(before.Y, after.Y, 3);
        Assert.Equal(400, after.Height, 3);

        // And it is still a lava lamp rather than whatever shape the mouse was making.
        Assert.Equal(after.Height * LampScene.Aspect, after.Width, 6);
    }

    [Fact]
    public void ResizingTheOtherWayHoldsItsOwnCorner()
    {
        var scene = Sized(size: 0.3, x: 0.5, y: 0.5);
        var before = scene.Measure().Box;

        scene.ResizeTo(LampGrip.ResizeTopLeft, new ScenePoint(before.Right - 100, before.Bottom - 250));
        var after = scene.Measure().Box;

        Assert.Equal(before.Right, after.Right, 3);
        Assert.Equal(before.Bottom, after.Bottom, 3);
        Assert.Equal(250, after.Height, 3);
    }

    [Fact]
    public void DraggingSidewaysMakesItTallerToo()
    {
        // The lamp keeps its proportions, so a corner dragged out sideways has to grow the height
        // as well — otherwise the box stalls the moment somebody moves along only one axis, which
        // reads as the handle having been dropped.
        var scene = Sized(size: 0.3, x: 0.5, y: 0.5);
        var before = scene.Measure().Box;

        scene.ResizeTo(LampGrip.ResizeBottomRight, new ScenePoint(before.X + 300, before.Y + 10));
        var after = scene.Measure().Box;

        Assert.Equal(300, after.Width, 3);
        Assert.Equal(300 / LampScene.Aspect, after.Height, 3);
    }

    [Fact]
    public void ALampCannotBeDraggedDownToNothing()
    {
        var scene = Sized();
        var box = scene.Measure().Box;

        scene.ResizeTo(LampGrip.ResizeBottomRight, new ScenePoint(box.X + 2, box.Y + 2));

        Assert.Equal(LampScene.MinimumHeight, scene.Measure().Box.Height, 3);
    }

    [Fact]
    public void ALampCannotBeDraggedTallerThanTheDesktop()
    {
        var scene = Sized();
        var box = scene.Measure().Box;

        scene.ResizeTo(LampGrip.ResizeBottomRight, new ScenePoint(box.X + 5000, box.Y + 5000));

        Assert.Equal(scene.MaximumHeight, scene.Measure().Box.Height, 3);
    }

    [Fact]
    public void ALampWrittenOnABiggerScreenStillFitsOnThisOne()
    {
        // A placement written on a 4K monitor, opened on something much smaller. The size clamp is
        // what keeps this honest — without it the box would be taller than the desktop, and MoveTo
        // would be asked to clamp a position between a low above its own high.
        var scene = Scene(size: 4.0);
        scene.Resize(200, 120);

        scene.MoveTo(new ScenePoint(10, 10));
        var box = scene.Measure().Box;

        Assert.True(box.Height <= 120, $"the lamp is taller than the desktop at {box.Height}");
        Assert.True(box.X >= -0.001 && box.Right <= 200.001, $"off the side: {box.X} to {box.Right}");
        Assert.True(box.Y >= -0.001 && box.Bottom <= 120.001, $"off the top or bottom: {box.Y} to {box.Bottom}");
    }

    [Fact]
    public void AnOverlayWithNoRoomAtAllCentresRatherThanThrowing()
    {
        // Asked to lay out before the window has been given a size. The minimum height is taller
        // than the whole overlay here, so there is no legal position — Math.Clamp would throw on a
        // low above its high, and this is the frame that would take the app down with it.
        var scene = Scene();
        scene.Resize(0, 0);

        scene.MoveTo(new ScenePoint(10, 10));

        Assert.Equal(0.5, scene.Placement.AnchorX, 6);
        Assert.Equal(0.5, scene.Placement.AnchorY, 6);
    }

    // ── what the mouse is over ────────────────────────────────────────────────

    [Fact]
    public void TheMiddleOfTheLampIsTheThingYouDrag()
    {
        var scene = Sized();
        Assert.Equal(LampGrip.Body, scene.HitTest(scene.Measure().Centre));
    }

    [Fact]
    public void BareDesktopIsNotTheLamp()
    {
        var scene = Sized(size: 0.34, x: 0.5, y: 0.5);
        Assert.Equal(LampGrip.None, scene.HitTest(new ScenePoint(10, 10)));
    }

    [Theory]
    [InlineData(LampGrip.ResizeTopLeft)]
    [InlineData(LampGrip.ResizeTopRight)]
    [InlineData(LampGrip.ResizeBottomLeft)]
    [InlineData(LampGrip.ResizeBottomRight)]
    public void EveryCornerAnswersItsOwnHandle(LampGrip corner)
    {
        var scene = Sized();
        var layout = scene.Measure();

        Assert.Equal(corner, scene.HitTest(layout.Handle(corner).Centre));
    }

    [Fact]
    public void TheButtonsWinAgainstAnythingUnderneathThem()
    {
        var scene = Sized();
        var layout = scene.Measure();

        Assert.Equal(LampGrip.Save, scene.HitTest(layout.SaveButton.Centre));
        Assert.Equal(LampGrip.Cancel, scene.HitTest(layout.CancelButton.Centre));
    }

    [Fact]
    public void TheButtonsMoveBelowTheLampWhenThereIsNoRoomAbove()
    {
        var scene = Sized(size: 0.34, x: 0.5, y: 0.5);
        Assert.True(scene.Measure().SaveButton.Bottom < scene.Measure().Box.Y);

        // Dragged to the very top of the desktop, where buttons drawn above it would be off the
        // edge and unpressable.
        scene.MoveTo(new ScenePoint(800, 0));
        var layout = scene.Measure();

        Assert.True(layout.SaveButton.Y > layout.Box.Bottom, "the buttons stayed off the top of the screen");
    }

    // ── save and cancel ───────────────────────────────────────────────────────

    [Fact]
    public void CancelPutsItBackWhereItStarted()
    {
        var scene = Sized(size: 0.34, x: 0.5, y: 0.5);
        scene.BeginEdit();

        scene.MoveTo(new ScenePoint(200, 200));
        scene.ResizeTo(LampGrip.ResizeBottomRight, new ScenePoint(600, 600));
        scene.CancelEdit();

        Assert.Equal(0.5, scene.Placement.AnchorX, 6);
        Assert.Equal(0.5, scene.Placement.AnchorY, 6);
        Assert.Equal(0.34, scene.Placement.Size, 6);
        Assert.False(scene.IsEditing);
    }

    [Fact]
    public void SaveKeepsWhereItWasDraggedTo()
    {
        var scene = Sized(size: 0.34, x: 0.5, y: 0.5);
        scene.BeginEdit();

        scene.MoveTo(new ScenePoint(200, 300));
        scene.CommitEdit();

        Assert.Equal(200.0 / 1600, scene.Placement.AnchorX, 6);
        Assert.Equal(300.0 / 900, scene.Placement.AnchorY, 6);
        Assert.False(scene.IsEditing);

        // And a cancel afterwards has nothing left to undo, which is what stops the cross rolling
        // the lamp back to somewhere it was ten minutes ago.
        scene.CancelEdit();
        Assert.Equal(200.0 / 1600, scene.Placement.AnchorX, 6);
    }

    [Fact]
    public void EnteringEditModeTwiceDoesNotMoveTheGoalposts()
    {
        var scene = Sized(size: 0.34, x: 0.5, y: 0.5);
        scene.BeginEdit();

        scene.MoveTo(new ScenePoint(100, 100));
        scene.BeginEdit();      // the tray asking for a mode that is already on
        scene.CancelEdit();

        Assert.Equal(0.5, scene.Placement.AnchorX, 6);
        Assert.Equal(0.5, scene.Placement.AnchorY, 6);
    }

    [Fact]
    public void EditModeShowsTheLampWhateverGreenlightSays()
    {
        var scene = Sized();
        scene.ForceVisible = true;
        scene.SnapVisible();

        Assert.Equal(1, scene.Fade);
    }

    [Fact]
    public void EditingALitLampDoesNotMakeAnybodyWaitForTheWax()
    {
        var scene = Lit(LampState.Red);
        scene.SnapVisible();

        // Straight to a warm, boiling lamp. Nobody wants to watch a six-second warm-up because
        // they wanted to nudge the thing two inches to the left.
        Assert.Equal(1, scene.Warmth);
        Assert.Equal(1, scene.Boil);
    }

    // ── the long grass ────────────────────────────────────────────────────────

    [Fact]
    public void AFrameFromAfterTheMachineWokeUpIsClamped()
    {
        var scene = Lit();

        // The lid was shut for two minutes. Unclamped, this would run the whole warm-up inside one
        // frame and the wax would be at full height before it was drawn once.
        scene.Advance(TimeSpan.FromMinutes(2));

        Assert.True(scene.Warmth < 0.2, $"the lamp jumped to {scene.Warmth:F2} warm in one frame");
    }

    [Fact]
    public void AZeroSizedOverlayDoesNotProduceNonsense()
    {
        var scene = Scene();
        scene.Resize(0, 0);

        var layout = scene.Measure();

        Assert.True(double.IsFinite(layout.Box.Height));
        Assert.True(layout.Box.Height >= LampScene.MinimumHeight);

        // And the wax still lands somewhere real rather than at NaN, which is the frame that
        // would take the whole app down.
        foreach (var blob in scene.Blobs)
        {
            var placed = scene.Place(blob, layout);
            Assert.True(double.IsFinite(placed.Centre.X) && double.IsFinite(placed.Centre.Y));
        }
    }
}
