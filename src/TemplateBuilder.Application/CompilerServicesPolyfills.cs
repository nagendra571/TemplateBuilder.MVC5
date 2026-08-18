namespace System.Runtime.CompilerServices;

internal static class IsExternalInit { }

[AttributeUsage(AttributeTargets.All, Inherited = false, AllowMultiple = true)]
internal sealed class CompilerFeatureRequiredAttribute : Attribute
{
    public const string RefStructs = nameof(RefStructs);
    public const string RequiredMembers = nameof(RequiredMembers);

    public CompilerFeatureRequiredAttribute(string featureName) => FeatureName = featureName;

    public string FeatureName { get; }
    public bool IsOptional { get; init; }
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Field | AttributeTargets.Property, Inherited = false, AllowMultiple = false)]
internal sealed class RequiredMemberAttribute : Attribute { }