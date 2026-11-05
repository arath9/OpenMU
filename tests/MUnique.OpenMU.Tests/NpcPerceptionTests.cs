// <copyright file="NpcPerceptionTests.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Tests;

using MUnique.OpenMU.GameLogic;
using MUnique.OpenMU.GameLogic.Bots;

/// <summary>Checks that perception retains real transitions instead of only logging them.</summary>
[TestFixture]
public class NpcPerceptionTests
{
    /// <summary>Existing humans at the first tick have also arrived for this bot.</summary>
    [Test]
    public async Task NearbyTransitionsAreBufferedOnceAsync()
    {
        await using var human = await PlayerTestHelper.CreatePlayerAsync().ConfigureAwait(false);
        var perception = new NpcPerception();
        Assert.That(perception.UpdateNearbyHumans([human]).Select(e => e.Kind), Is.EqualTo(new[] { WorldEventKind.PlayerArrived }));
        Assert.That(perception.UpdateNearbyHumans([human]), Is.Empty);
        Assert.That(perception.UpdateNearbyHumans(Array.Empty<Player>()).Select(e => e.Kind), Is.EqualTo(new[] { WorldEventKind.PlayerLeft }));
        Assert.That(perception.RecentEvents.Select(e => e.Kind), Is.EqualTo(new[] { WorldEventKind.PlayerArrived, WorldEventKind.PlayerLeft }));
    }

    /// <summary>Perception is bounded and ignores empty noise.</summary>
    [Test]
    public void BufferKeepsOnlyLastTenValidEvents()
    {
        var perception = new NpcPerception();
        perception.AddEvent(WorldEventKind.None, null, "noise");
        perception.AddEvent(WorldEventKind.PlayerDied, null, " ");
        for (var i = 0; i < 12; i++)
        {
            perception.AddEvent(WorldEventKind.PlayerDied, null, i.ToString());
        }

        Assert.That(perception.RecentEvents.Select(e => e.Text), Is.EqualTo(Enumerable.Range(2, 10).Select(i => i.ToString())));
        perception.Reset();
        Assert.That(perception.RecentEvents, Is.Empty);
        Assert.That(perception.HasRecentActivity(), Is.False);
    }
}
