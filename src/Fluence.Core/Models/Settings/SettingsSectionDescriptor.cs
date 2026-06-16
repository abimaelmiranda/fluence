using System;
using System.Text.Json.Serialization.Metadata;

namespace Fluence.Core.Models.Settings;

public sealed record SettingsSectionDescriptor(
    string SectionName,
    Type SettingsType,
    bool UseGlobalSettings,
    JsonTypeInfo TypeInfo);
