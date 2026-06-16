using System;

namespace Fluence.Core.Models.Settings;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class SettingsSectionAttribute(string sectionName, bool useGlobalSettings = true) : Attribute
{
    public string SectionName { get; } = sectionName;

    public bool UseGlobalSettings { get; } = useGlobalSettings;
}
