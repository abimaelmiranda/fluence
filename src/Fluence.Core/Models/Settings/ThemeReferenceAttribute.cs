using System;

namespace Fluence.Core.Models.Settings;

[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class ThemeReferenceAttribute : Attribute { }
