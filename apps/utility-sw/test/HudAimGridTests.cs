using FiveStack.Utilities;
using Xunit;

namespace FiveStack.Tests;

public class HudAimGridTests
{
    private const float Tolerance = 0.35f;
    private const int Columns = 25;
    private const int Rows = 17;
    private const int CentreColumn = 12;
    private const int CentreRow = 8;

    [Fact]
    public void OnTheAngleSitsInTheCentreCell()
    {
        Assert.Equal(CentreColumn, HudAimGrid.Column(90f, 90f, Tolerance, Columns));
        Assert.Equal(CentreRow, HudAimGrid.Row(-12f, -12f, Tolerance, Rows));
    }

    [Fact]
    public void TargetToTheRightPutsTheDotLeft()
    {
        Assert.True(HudAimGrid.Column(90f, 89f, Tolerance, Columns) < CentreColumn);
        Assert.True(HudAimGrid.Column(90f, 91f, Tolerance, Columns) > CentreColumn);
    }

    [Fact]
    public void LookingBelowTheTargetPutsTheDotLow()
    {
        Assert.True(HudAimGrid.Row(5f, 0f, Tolerance, Rows) > CentreRow);
        Assert.True(HudAimGrid.Row(-5f, 0f, Tolerance, Rows) < CentreRow);
    }

    // The whole point of the rework: the drawn box is one tolerance, so exactly
    // at tolerance the dot sits on its edge whatever the lineup's tolerance is.
    [Theory]
    [InlineData(0.1f)]
    [InlineData(0.35f)]
    [InlineData(2.0f)]
    public void OneToleranceOutLandsOnTheBoxEdge(float tolerance)
    {
        Assert.Equal(HudAimGrid.ToleranceExtent, HudAimGrid.Offset(tolerance, tolerance), 4);
        Assert.Equal(-HudAimGrid.ToleranceExtent, HudAimGrid.Offset(-tolerance, tolerance), 4);
    }

    [Theory]
    [InlineData(0.1f)]
    [InlineData(0.35f)]
    [InlineData(2.0f)]
    public void InsideToleranceIsAlwaysInsideTheBox(float tolerance)
    {
        for (float fraction = 0f; fraction < 1f; fraction += 0.05f)
        {
            float offset = Math.Abs(HudAimGrid.Offset(tolerance * fraction, tolerance));

            Assert.True(offset < HudAimGrid.ToleranceExtent + 0.0001f);
        }
    }

    [Fact]
    public void OutsideToleranceIsAlwaysOutsideTheBox()
    {
        foreach (float multiple in new[] { 1.2f, 2f, 5f, 40f })
        {
            float offset = Math.Abs(HudAimGrid.Offset(Tolerance * multiple, Tolerance));

            Assert.True(offset > HudAimGrid.ToleranceExtent);
        }
    }

    // Linear inside the tolerance: half the tolerance is half the travel, so
    // the range the player is working in gets every cell it can.
    [Fact]
    public void TravelIsLinearInsideTheTolerance()
    {
        Assert.Equal(HudAimGrid.ToleranceExtent / 2f, HudAimGrid.Offset(Tolerance / 2f, Tolerance), 4);
        Assert.Equal(HudAimGrid.ToleranceExtent / 4f, HudAimGrid.Offset(Tolerance / 4f, Tolerance), 4);
    }

    [Fact]
    public void FarOutPinsToTheEdgeWithoutOvershooting()
    {
        Assert.Equal(Columns - 1, HudAimGrid.Column(90f, 90f + 400f * Tolerance, Tolerance, Columns));
        Assert.Equal(0, HudAimGrid.Column(90f, 90f - 400f * Tolerance, Tolerance, Columns));
        Assert.True(Math.Abs(HudAimGrid.Offset(9999f, Tolerance)) <= 1f);
    }

    [Fact]
    public void ZeroToleranceFallsBackToTheDefault()
    {
        Assert.Equal(
            HudAimGrid.Offset(PracticeLineupUtility.DefaultAimTolerance, PracticeLineupUtility.DefaultAimTolerance),
            HudAimGrid.Offset(PracticeLineupUtility.DefaultAimTolerance, 0f),
            4
        );
    }

    [Fact]
    public void WrapsTheShortWayRound()
    {
        Assert.Equal(0f, HudAimGrid.Delta(359f, 359f));
        Assert.Equal(2f, HudAimGrid.Delta(1f, 359f));
        Assert.Equal(-2f, HudAimGrid.Delta(359f, 1f));

        Assert.Equal(
            HudAimGrid.Column(1f, 359f, Tolerance, Columns),
            HudAimGrid.Column(11f, 9f, Tolerance, Columns)
        );
    }

    [Fact]
    public void EveryCellIsInRange()
    {
        for (float degrees = -30f; degrees <= 30f; degrees += 0.05f)
        {
            Assert.InRange(HudAimGrid.Column(90f + degrees, 90f, Tolerance, Columns), 0, Columns - 1);
            Assert.InRange(HudAimGrid.Row(degrees, 0f, Tolerance, Rows), 0, Rows - 1);
        }
    }

    // The words and the dot must never disagree: the dot sits where the player
    // is aiming, so a target to the right puts the dot LEFT and the text has to
    // say LOOK RIGHT. Source yaw increases anticlockwise, so a target BELOW the
    // player's yaw is the one on their right.
    [Fact]
    public void DirectionAgreesWithTheDot()
    {
        Assert.True(HudAimGrid.Column(90f, 89f, Tolerance, Columns) < CentreColumn);
        Assert.Equal("LOOK RIGHT", HudAimGrid.Direction(90f, 0f, 89f, 0f, Tolerance));

        Assert.True(HudAimGrid.Column(90f, 91f, Tolerance, Columns) > CentreColumn);
        Assert.Equal("LOOK LEFT", HudAimGrid.Direction(90f, 0f, 91f, 0f, Tolerance));

        Assert.True(HudAimGrid.Row(5f, 0f, Tolerance, Rows) > CentreRow);
        Assert.Equal("LOOK UP", HudAimGrid.Direction(90f, 5f, 90f, 0f, Tolerance));

        Assert.True(HudAimGrid.Row(-5f, 0f, Tolerance, Rows) < CentreRow);
        Assert.Equal("LOOK DOWN", HudAimGrid.Direction(90f, -5f, 90f, 0f, Tolerance));
    }

    [Fact]
    public void DirectionNamesBothAxesWhenBothAreOut()
    {
        Assert.Equal("LOOK RIGHT AND DOWN", HudAimGrid.Direction(90f, -5f, 89f, 0f, Tolerance));
    }

    [Fact]
    public void OnTheAngleSaysSo()
    {
        Assert.Equal("LINED UP - THROW IT", HudAimGrid.Direction(90f, -12f, 90f, -12f, Tolerance));
    }
}
