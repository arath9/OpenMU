// <copyright file="SocialMemoryServiceCollectionExtensions.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Persistence.EntityFramework;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MUnique.OpenMU.Persistence.Contracts;

/// <summary>
/// Registers the persistent social memory used by singleton bot plugins.
/// </summary>
public static class SocialMemoryServiceCollectionExtensions
{
    /// <summary>
    /// Adds a singleton repository with an independently disposed context per operation.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same collection for chaining.</returns>
    public static IServiceCollection AddSocialMemoryRepository(this IServiceCollection services)
    {
        services.AddDbContextFactory<EntityDataContext>();
        services.TryAddSingleton<ISocialMemoryRepository, SocialMemoryRepository>();
        return services;
    }
}
