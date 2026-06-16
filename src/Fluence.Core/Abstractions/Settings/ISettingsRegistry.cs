using System;
using System.Collections.Generic;
using System.Text.Json.Serialization.Metadata;
using Fluence.Core.Models.Settings;

namespace Fluence.Core.Abstractions.Settings;

public interface ISettingsRegistry
{
    void Register<TSettings>(JsonTypeInfo<TSettings> typeInfo)
        where TSettings : class, new();

    IReadOnlyList<SettingsSectionDescriptor> Sections { get; }

    SettingsSectionDescriptor GetSection(Type settingsType);
}
