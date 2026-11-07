// <copyright file="SocialMemoryExtensions.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Persistence.EntityFramework.Extensions.ModelBuilder;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MUnique.OpenMU.Persistence.EntityFramework.Model;

/// <summary>Configures persisted social-memory entities, not repository contract DTOs.</summary>
internal static class SocialMemoryExtensions
{
    /// <summary>Applies interaction model settings.</summary>
    public static void Apply(this EntityTypeBuilder<BotSocialInteraction> builder)
    {
        builder.Property(i => i.BotName).IsRequired().HasMaxLength(100);
        builder.Property(i => i.TargetName).IsRequired().HasMaxLength(100);
        builder.HasIndex(i => new { i.BotName, i.Timestamp });
    }

    /// <summary>Applies reputation model settings.</summary>
    public static void Apply(this EntityTypeBuilder<BotReputationRecord> builder)
    {
        builder.Property(r => r.BotName).IsRequired().HasMaxLength(100);
        builder.Property(r => r.TargetName).IsRequired().HasMaxLength(100);
        builder.HasIndex(r => new { r.BotName, r.TargetName }).IsUnique();
    }

    /// <summary>Applies event log model settings.</summary>
    public static void Apply(this EntityTypeBuilder<BotEventLog> builder)
    {
        builder.Property(l => l.BotName).IsRequired().HasMaxLength(100);
        builder.Property(l => l.Context).HasMaxLength(500);
        builder.HasIndex(l => new { l.BotName, l.Timestamp });
    }
}

