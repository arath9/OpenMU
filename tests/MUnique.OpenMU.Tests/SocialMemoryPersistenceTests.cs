// <copyright file="SocialMemoryPersistenceTests.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Tests;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MUnique.OpenMU.Persistence.Contracts;
using MUnique.OpenMU.Persistence.EntityFramework;

/// <summary>
/// Verifies that singleton bot plugins can safely resolve social-memory persistence.
/// </summary>
[TestFixture]
public class SocialMemoryPersistenceTests
{
    /// <summary>
    /// A singleton repository must not capture a scoped, non-thread-safe data context.
    /// </summary>
    [Test]
    public void RepositoryCanBeResolvedFromRootWithScopeValidation()
    {
        var services = new ServiceCollection();
        services.AddSocialMemoryRepository();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true,
        });

        var repository = provider.GetRequiredService<ISocialMemoryRepository>();
        Assert.That(repository, Is.TypeOf<SocialMemoryRepository>());
        Assert.That(provider.GetRequiredService<ISocialMemoryRepository>(), Is.SameAs(repository));

        var factory = provider.GetRequiredService<IDbContextFactory<EntityDataContext>>();
        using var first = factory.CreateDbContext();
        using var second = factory.CreateDbContext();
        Assert.That(first, Is.Not.SameAs(second));
    }
}
