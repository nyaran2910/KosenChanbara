using NUnit.Framework;
using SchoolFestival.Combat;

public sealed class MatchProgressTests
{
    private static double EnterFight(DuelSeries series, double now)
    {
        if (series.Phase == RoundPhase.Result) { now += 3.41; series.Tick(now); }
        if (series.Phase == RoundPhase.Versus) { now += 3.21; series.Tick(now); }
        now += 1.31; series.Tick(now);
        now += 1.21; series.Tick(now);
        Assert.That(series.Phase, Is.EqualTo(RoundPhase.Fight));
        return now;
    }

    [TestCase(MatchWinner.Player1)]
    [TestCase(MatchWinner.Player2)]
    public void ThreePointsFinishSeriesAndResetClearsEveryScore(MatchWinner player)
    {
        var series = new DuelSeries(); series.Reset(0);
        double now = 0;
        Assert.That(series.EndRound(player, now), Is.False, "No scoring during the entrance.");
        Assert.That(DuelSeries.WinsRequired, Is.EqualTo(3));
        for (int point = 1; point <= 3; point++)
        {
            now = EnterFight(series, now);
            Assert.That(series.RoundNumber, Is.EqualTo(point));
            Assert.That(series.EndRound(player, now), Is.True);
            Assert.That(series.EndRound(player, now), Is.False, "Duplicate ring-out must not award another point.");
            Assert.That(player == MatchWinner.Player1 ? series.FirstScore : series.SecondScore, Is.EqualTo(point));
            Assert.That(series.Winner, Is.EqualTo(point < 3 ? MatchWinner.None : player));
        }
        series.Tick(now + 3.41);
        Assert.That(series.Phase, Is.EqualTo(RoundPhase.Complete));
        Assert.That(series.Tick(now + 1000), Is.False);
        series.Reset(now + 1000);
        Assert.That(series.Phase, Is.EqualTo(RoundPhase.Versus));
        Assert.That(series.FirstScore + series.SecondScore, Is.Zero);
        Assert.That(series.Winner, Is.EqualTo(MatchWinner.None));
        Assert.That(series.RoundNumber, Is.EqualTo(1));
    }

    [Test]
    public void TimeoutDrawAwardsBothOnePointAndSameClockNeverAdvancesPause()
    {
        var series = new DuelSeries(); series.Reset(10);
        double now = EnterFight(series, 10);
        for (int frame = 0; frame < 100; frame++) Assert.That(series.Tick(now), Is.False);
        Assert.That(series.Remaining(now), Is.EqualTo(60));
        Assert.That(series.Tick(now + 60), Is.True);
        Assert.That(series.RoundWinner, Is.EqualTo(MatchWinner.Draw));
        Assert.That(series.FirstScore, Is.EqualTo(1));
        Assert.That(series.SecondScore, Is.EqualTo(1));
        Assert.That(series.EndRound(MatchWinner.Draw, now + 60), Is.False);
        Assert.That(series.Winner, Is.EqualTo(MatchWinner.None));
        now = EnterFight(series, now + 60);
        Assert.That(series.RoundNumber, Is.EqualTo(2));
        Assert.That(series.Remaining(now), Is.EqualTo(60));
    }

    [TestCase(MatchWinner.Player1)]
    [TestCase(MatchWinner.Player2)]
    public void SimultaneousTimeoutThresholdStartsUnlimitedFinalRoundUntilEitherPlayerWins(MatchWinner winner)
    {
        var series = new DuelSeries(); series.Reset(0);
        double now = 0;
        for (int round = 1; round <= 3; round++)
        {
            now = EnterFight(series, now);
            now += DuelSeries.FightSeconds;
            Assert.That(series.Tick(now), Is.True);
            Assert.That(series.RoundWinner, Is.EqualTo(MatchWinner.Draw));
            Assert.That(series.FirstScore, Is.EqualTo(round));
            Assert.That(series.SecondScore, Is.EqualTo(round));
            Assert.That(series.Winner, Is.EqualTo(MatchWinner.None));
            Assert.That(series.IsFinalRound, Is.EqualTo(round == 3));
        }
        now = EnterFight(series, now);
        Assert.That(series.RoundNumber, Is.EqualTo(4));
        Assert.That(series.Remaining(now), Is.EqualTo(double.PositiveInfinity));
        for (int frame = 0; frame < 100; frame++) Assert.That(series.Tick(now), Is.False, "The paused gameplay clock cannot advance the final round.");
        now += 3600;
        Assert.That(series.Tick(now), Is.False, "A final round cannot award a timeout draw.");
        Assert.That(series.Phase, Is.EqualTo(RoundPhase.Fight));
        Assert.That(series.FirstScore, Is.EqualTo(3));
        Assert.That(series.SecondScore, Is.EqualTo(3));
        Assert.That(series.Remaining(now), Is.EqualTo(double.PositiveInfinity));
        Assert.That(series.EndRound(winner, now), Is.True);
        Assert.That(series.EndRound(winner, now), Is.False);
        Assert.That(series.Winner, Is.EqualTo(winner));
        Assert.That(winner == MatchWinner.Player1 ? series.FirstScore : series.SecondScore, Is.EqualTo(4));
        Assert.That(winner == MatchWinner.Player1 ? series.SecondScore : series.FirstScore, Is.EqualTo(3));
        Assert.That(series.Tick(now + 3.41), Is.True);
        Assert.That(series.Phase, Is.EqualTo(RoundPhase.Complete));
        Assert.That(series.EndRound(MatchWinner.Player1, now + 10), Is.False);
        series.Reset(now + 10);
        Assert.That(series.IsFinalRound, Is.False);
        Assert.That(series.Winner, Is.EqualTo(MatchWinner.None));
        Assert.That(series.FirstScore + series.SecondScore, Is.Zero);
        now = EnterFight(series, now + 10);
        Assert.That(series.Remaining(now), Is.EqualTo(DuelSeries.FightSeconds));
        Assert.That(series.Tick(now + DuelSeries.FightSeconds), Is.True, "A rematch restores the ordinary timeout.");
    }

    [TestCase(MatchWinner.Player1)]
    [TestCase(MatchWinner.Player2)]
    public void SimultaneousRingOutRepeatsFinalRoundUntilThereIsASingleWinner(MatchWinner winner)
    {
        var series = new DuelSeries(); series.Reset(0);
        double now = 0;
        for (int round = 1; round <= 4; round++)
        {
            now = EnterFight(series, now);
            Assert.That(series.EndRound(MatchWinner.Draw, now), Is.True);
            Assert.That(series.FirstScore, Is.EqualTo(round));
            Assert.That(series.SecondScore, Is.EqualTo(round));
            Assert.That(series.Winner, Is.EqualTo(MatchWinner.None));
            Assert.That(series.IsFinalRound, Is.EqualTo(round >= 3));
        }
        now = EnterFight(series, now);
        Assert.That(series.RoundNumber, Is.EqualTo(5));
        Assert.That(series.Remaining(now), Is.EqualTo(double.PositiveInfinity));
        Assert.That(series.EndRound(winner, now), Is.True);
        Assert.That(series.Winner, Is.EqualTo(winner));
        series.Tick(now + 3.41);
        Assert.That(series.Phase, Is.EqualTo(RoundPhase.Complete));
    }

    [TestCase(MatchWinner.Player1)]
    [TestCase(MatchWinner.Player2)]
    public void DrawCanGiveOnlyTheLeadingPlayerTheirThirdPoint(MatchWinner leading)
    {
        var series = new DuelSeries(); series.Reset(0);
        double now = EnterFight(series, 0);
        series.EndRound(leading, now);
        now = EnterFight(series, now); series.EndRound(leading, now);
        now = EnterFight(series, now); series.EndRound(MatchWinner.Draw, now);
        Assert.That(leading == MatchWinner.Player1 ? series.FirstScore : series.SecondScore, Is.EqualTo(3));
        Assert.That(leading == MatchWinner.Player1 ? series.SecondScore : series.FirstScore, Is.EqualTo(1));
        Assert.That(series.Winner, Is.EqualTo(leading));
        Assert.That(series.IsFinalRound, Is.False, "Only a simultaneous threshold starts the final round.");
        series.Tick(now + 3.41);
        Assert.That(series.Phase, Is.EqualTo(RoundPhase.Complete));
    }

    [Test]
    public void ExactlyOneHundredDefeatsClearAndNoFurtherSpawnIsPossible()
    {
        var run = new ShowdownProgress(); run.Reset();
        Assert.That(run.Defeat(), Is.False);
        for (int i = 0; i < 100; i++)
        {
            Assert.That(run.TrySpawn(), Is.True);
            Assert.That(run.Defeat(), Is.True);
            Assert.That(run.Defeat(), Is.False, "Cannot credit the same challenger twice.");
            Assert.That(run.Cleared, Is.EqualTo(i == 99));
        }
        Assert.That(run.Finished, Is.True);
        Assert.That(run.TrySpawn(), Is.False);
        Assert.That(run.Damage(200), Is.False, "Clear result cannot become a loss.");
        Assert.That(run.Hearts, Is.EqualTo(ShowdownProgress.MaxHearts));
        run.Reset();
        Assert.That(run.Spawned + run.Defeated, Is.Zero);
        Assert.That(run.Finished, Is.False);
    }

    [Test]
    public void FiveDirectHitsCanLoseDuringStaggerAndRetryRestoresTenLife()
    {
        var run = new ShowdownProgress(); run.Reset();
        Assert.That(run.Damage(0), Is.True);
        Assert.That(run.IsRecovering(.1d), Is.True);
        Assert.That(run.Damage(.1d), Is.False, "The common damage immunity lasts 0.2 seconds.");
        Assert.That(run.Damage(.2d), Is.True);
        Assert.That(run.Damage(.4d), Is.True);
        Assert.That(run.Damage(.6d), Is.True);
        Assert.That(run.Damage(.8d), Is.True);
        Assert.That(run.Hearts, Is.Zero);
        Assert.That(run.Finished, Is.True);
        Assert.That(run.Cleared, Is.False);
        Assert.That(run.TrySpawn(), Is.False);
        Assert.That(run.Damage(5), Is.False);
        run.Reset();
        Assert.That(run.Hearts, Is.EqualTo(ShowdownProgress.MaxHearts));
        Assert.That(run.TrySpawn(), Is.True);
        Assert.That(run.Damage(0), Is.True);
    }
    [Test]
    public void FailedGuardDealsOneAndLethalDirectHitClampsToZero()
    {
        var run=new ShowdownProgress();run.Reset();
        Assert.That(run.Hearts,Is.EqualTo(10));
        for(int hit=0;hit<9;hit++)Assert.That(run.Damage(hit*.2,true),Is.True);
        Assert.That(run.Hearts,Is.EqualTo(1));
        Assert.That(run.Damage(1.8,false),Is.True);
        Assert.That(run.Hearts,Is.Zero);
        Assert.That(run.Damage(2,true),Is.False);
        run.Reset();Assert.That(run.Hearts,Is.EqualTo(10));
        for(int hit=0;hit<10;hit++)Assert.That(run.Damage(hit*.2,true),Is.True);
        Assert.That(run.Hearts,Is.Zero);
    }

}
