// <copyright file="NpcMoodSystemTests.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Tests;

using MUnique.OpenMU.GameLogic.Bots;

/// <summary>Humor y energía de los protagonistas.</summary>
[TestFixture]
public class NpcMoodSystemTests
{
    /// <summary>Tres ataques lo ponen molesto.</summary>
    [Test]
    public void BeingAttackedMakesItAngry()
    {
        var mood = new NpcMoodSystem();
        mood.Apply(-15, 0);
        mood.Apply(-15, 0);
        mood.Apply(-15, 0);
        Assert.That(mood.MoodScore, Is.EqualTo(-45));
        Assert.That(mood.MoodText, Is.EqualTo("molesto"));
    }

    /// <summary>Los valores no se salen de rango.</summary>
    [Test]
    public void ValuesAreClamped()
    {
        var mood = new NpcMoodSystem();
        mood.Apply(-500, -500);
        Assert.That(mood.MoodScore, Is.EqualTo(-100));
        Assert.That(mood.Energy, Is.EqualTo(0));
    }

    /// <summary>Descansar recupera energía y el humor vuelve hacia neutro.</summary>
    [Test]
    public void RestingRecoversEnergyAndMoodDriftsToNeutral()
    {
        var start = DateTimeOffset.UtcNow;
        var mood = new NpcMoodSystem(start);
        mood.Apply(-30, -50);
        mood.Tick(start.AddMinutes(5), NpcActivityKind.Rest);
        Assert.That(mood.Energy, Is.EqualTo(35));
        Assert.That(mood.MoodScore, Is.EqualTo(-25));
    }

    /// <summary>Cazar cansa.</summary>
    [Test]
    public void HuntingTiresIt()
    {
        var start = DateTimeOffset.UtcNow;
        var mood = new NpcMoodSystem(start);
        mood.Tick(start.AddMinutes(5), NpcActivityKind.Hunt);
        Assert.That(mood.Energy, Is.EqualTo(65));
    }
}
