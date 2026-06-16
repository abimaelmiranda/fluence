using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization.Metadata;
using Fluence.Core.Abstractions.Settings;
using Fluence.Core.Models.Settings;

namespace Fluence.Core.Services.Settings;

public sealed class SettingsRegistry : ISettingsRegistry
{
    private readonly Dictionary<Type, SettingsSectionDescriptor> _sections = new();

    public IReadOnlyList<SettingsSectionDescriptor> Sections => _sections.Values
        .OrderBy(section => section.SectionName, StringComparer.Ordinal)
        .ToArray();

    public void Register<TSettings>(JsonTypeInfo<TSettings> typeInfo)
        where TSettings : class, new()
    {
        var type = typeof(TSettings);
        var attribute = (SettingsSectionAttribute?)Attribute.GetCustomAttribute(type, typeof(SettingsSectionAttribute));
        if (attribute is null)
            throw new InvalidOperationException($"{type.FullName} must be marked with SettingsSectionAttribute.");

        _sections[type] = new SettingsSectionDescriptor(
            attribute.SectionName,
            type,
            attribute.UseGlobalSettings,
            typeInfo);
    }

    public SettingsSectionDescriptor GetSection(Type settingsType)
    {
        if (_sections.TryGetValue(settingsType, out var section))
            return section;

        throw new InvalidOperationException($"{settingsType.FullName} is not registered as a settings section.");
    }
}
