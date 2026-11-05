// <copyright file="AutonomousIntentTests.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Tests;

using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using Microsoft.Extensions.Logging.Abstractions;
using MUnique.OpenMU.GameLogic.Bots;
using MUnique.OpenMU.GameLogic.Bots.LLMIntegration;

/// <summary>Verifies the actual HTTP contract without contacting a model.</summary>
[TestFixture]
public class AutonomousIntentTests
{
    /// <summary>The autonomous schema restricts targets to visible humans and supports silence.</summary>
    [Test]
    public async Task AutonomousSchemaHasDynamicTargetsAsync()
    {
        using var client = new LocalLLMClient(new LocalLLMClient.LLMConfig(), NullLogger<LocalLLMClient>.Instance);
        using var handler = new IntentHandler("{\"say\":\"\",\"action\":\"follow\",\"place\":\"\",\"target\":\"Visitor\"}");
        using var http = new HttpClient(handler);
        typeof(LocalLLMClient).GetField("_httpClient", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(client, http);
        var method = typeof(LocalLLMClient).GetMethods().SingleOrDefault(m => m.Name == "ChatIntentAsync" && m.GetParameters().Length == 5);
        Assert.That(method, Is.Not.Null, "Missing autonomous intent overload with dynamic targets.");
        var intent = await ((Task<NpcIntent?>)method!.Invoke(client, new object[] { "situation\nnearby", Array.Empty<ChatTurn>(), "Mandolion", new[] { "Visitor", "Other" }, CancellationToken.None })!).ConfigureAwait(false);
        Assert.That(intent, Is.Not.Null);
        Assert.That(intent!.Say, Is.Empty);
        Assert.That(typeof(NpcIntent).GetProperty("Target")?.GetValue(intent), Is.EqualTo("Visitor"));
        using var request = JsonDocument.Parse(handler.Body!);
        var schema = request.RootElement.GetProperty("response_format").GetProperty("json_schema");
        Assert.That(schema.GetProperty("strict").GetBoolean(), Is.True);
        Assert.That(schema.GetProperty("schema").GetProperty("properties").GetProperty("target").GetProperty("enum").EnumerateArray().Select(e => e.GetString()), Is.EquivalentTo(new[] { "", "Visitor", "Other" }));
        Assert.That(schema.GetProperty("schema").GetProperty("required").EnumerateArray().Select(e => e.GetString()), Does.Contain("target"));
        Assert.That(request.RootElement.GetProperty("reasoning_effort").GetString(), Is.EqualTo("none"));
    }

    /// <summary>Adding autonomy must leave the three-field chat schema unchanged.</summary>
    [Test]
    public async Task ChatSchemaRemainsCompatibleAsync()
    {
        using var client = new LocalLLMClient(new LocalLLMClient.LLMConfig(), NullLogger<LocalLLMClient>.Instance);
        using var handler = new IntentHandler("{\"say\":\"Buenas\",\"action\":\"none\",\"place\":\"\"}");
        using var http = new HttpClient(handler);
        typeof(LocalLLMClient).GetField("_httpClient", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(client, http);
        var intent = await client.ChatIntentAsync("chat", [], "Mandolion").ConfigureAwait(false);
        Assert.That(intent!.Say, Is.EqualTo("Buenas"));
        using var request = JsonDocument.Parse(handler.Body!);
        Assert.That(request.RootElement.GetProperty("response_format").GetProperty("json_schema").GetProperty("schema").GetProperty("properties").TryGetProperty("target", out _), Is.False);
    }

    private sealed class IntentHandler(string reply) : HttpMessageHandler
    {
        internal string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            this.Body = await request.Content!.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new { choices = new[] { new { message = new { content = reply }, finish_reason = "stop" } } })),
            };
        }
    }
}
