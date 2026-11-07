// <copyright file="ProtagonistChatVerificationTests.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Tests;

using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MUnique.OpenMU.GameLogic;
using MUnique.OpenMU.GameLogic.Bots;
using MUnique.OpenMU.GameLogic.Bots.PlugIns;
using MUnique.OpenMU.GameLogic.PlugIns;
using MUnique.OpenMU.Persistence.Contracts;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// Verifies real chat-policy methods and actual plugin-point membership, not log messages.
/// Does not assert that these policies are connected to live world actions.
/// </summary>
[TestFixture]
[NonParallelizable]
public class ProtagonistChatVerificationTests
{
    private static readonly string[] Protagonists = ["elmandolion", "bertoSalamin", "LeilaMasa", "elBichiCome"];

    /// <summary>Every requested protagonist must have a profile.</summary>
    [TestCaseSource(nameof(Protagonists))]
    public void ExactCharacterNameResolvesPersonality(string name)
    {
        var chat = CreateChat();
        var profile = Invoke(chat, "GetPersonality", name);
        Assert.That(profile, Is.Not.Null, name);
    }

    /// <summary>Generated text must not disclose sensitive patterns.</summary>
    [TestCase("password")]
    [TestCase("TOKEN")]
    [TestCase("secret")]
    [TestCase("api_key")]
    public void SensitiveResponseIsRejected(string response)
    {
        var chat = CreateChat();
        var profile = Invoke(chat, "GetPersonality", Protagonists[0]);
        Assert.That(Invoke(chat, "ValidateChatResponse", response, profile!), Is.Null);
    }

    /// <summary>Oversized responses are bounded.</summary>
    [Test]
    public void OversizedResponseIsTruncated()
    {
        var chat = CreateChat();
        var profile = Invoke(chat, "GetPersonality", Protagonists[0]);
        var response = (string)Invoke(chat, "ValidateChatResponse", new string('x', 501), profile!)!;
        Assert.That(response, Has.Length.EqualTo(200));
    }

    /// <summary>Generic response generation needs neither Ollama nor database access.</summary>
    [Test]
    public async Task GenericGreetingDoesNotCallPersistence()
    {
        var memory = new Mock<ISocialMemoryRepository>(MockBehavior.Strict);
        var chat = new ChatResponsePlugIn(NullLogger<ChatResponsePlugIn>.Instance, memory.Object);
        var response = await chat.GenerateChatResponseAsync("unprofiledBot", "verificationTarget", "hola").ConfigureAwait(false);
        Assert.That(response, Does.Contain("Hola"));
        memory.VerifyNoOtherCalls();
    }

    /// <summary>Both policies must have unique, explicitly declared stable IDs.</summary>
    [Test]
    public void SocialPlugInsDeclareUniqueGuids()
    {
        var assembly = typeof(GameContext).Assembly;
        var types = new[] { typeof(ChatResponsePlugIn), typeof(EventDirector) };
        foreach (var type in types)
        {
            Assert.That(type.GetCustomAttribute<System.Runtime.InteropServices.GuidAttribute>(false), Is.Not.Null);
            Assert.That(PlugInManager.DiscoverPlugInTypes(assembly), Does.Contain(type));
            Assert.That(PlugInManager.DiscoverPlugInTypes(assembly).Count(other => other.GUID == type.GUID), Is.EqualTo(1));
        }
    }

    /// <summary>Checks instances in containers, avoiding the known-type-only partial-registration trap.</summary>
    [Test]
    public void SocialPlugInsAreActualActiveInstances()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(new Mock<ISocialMemoryRepository>().Object);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        var manager = new PlugInManager(null, NullLoggerFactory.Instance, provider, null);
        manager.DiscoverAndRegisterPlugIns(typeof(GameContext).Assembly);
        try
        {
            Assert.That(manager.GetActivePlugInsOf<IChatMessageReceivedPlugIn>(), Has.Some.InstanceOf<ChatResponsePlugIn>());
            Assert.That(manager.GetActivePlugInsOf<IChatMessageSentPlugIn>(), Has.Some.InstanceOf<ChatResponsePlugIn>());
            Assert.That(manager.GetActivePlugInsOf<IPeriodicTaskPlugIn>(), Has.Some.InstanceOf<EventDirector>());
        }
        finally
        {
            foreach (var director in manager.GetActivePlugInsOf<IPeriodicTaskPlugIn>().OfType<EventDirector>())
            {
                director.Dispose();
            }
        }
    }

    private static ChatResponsePlugIn CreateChat()
        => new(NullLogger<ChatResponsePlugIn>.Instance, new Mock<ISocialMemoryRepository>().Object);

    private static object? Invoke(object target, string method, params object[] arguments)
        => target.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(target, arguments);
}
