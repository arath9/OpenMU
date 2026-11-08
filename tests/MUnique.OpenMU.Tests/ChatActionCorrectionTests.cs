// <copyright file="ChatActionCorrectionTests.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Tests;

using MUnique.OpenMU.GameLogic.Bots;

/// <summary>La red de seguridad sobre las acciones que elige el modelo en el chat.</summary>
[TestFixture]
public class ChatActionCorrectionTests
{
    /// <summary>"vení" no es guiar: se corrige a seguir.</summary>
    [TestCase("veni")]
    [TestCase("acercate que te muestro algo")]
    [TestCase("vamos juntos")]
    public void GuideWithoutPlaceBecomesFollow(string message)
    {
        Assert.That(BotProtagonist.CorrectChatAction(NpcActionKind.Guide, message), Is.EqualTo(NpcActionKind.Follow));
    }

    /// <summary>Si nombra un lugar, guiar es correcto.</summary>
    [TestCase("llevame a Devias")]
    [TestCase("como voy a noria?")]
    public void GuideWithPlaceStaysGuide(string message)
    {
        Assert.That(BotProtagonist.CorrectChatAction(NpcActionKind.Guide, message), Is.EqualTo(NpcActionKind.Guide));
    }

    /// <summary>Las demás acciones no se tocan.</summary>
    [TestCase(NpcActionKind.Follow)]
    [TestCase(NpcActionKind.Fight)]
    [TestCase(NpcActionKind.Stop)]
    [TestCase(NpcActionKind.None)]
    public void OtherActionsAreUntouched(NpcActionKind action)
    {
        Assert.That(BotProtagonist.CorrectChatAction(action, "veni"), Is.EqualTo(action));
    }
}
