
namespace Ingot.Edge.ConnectorHost.Acquisition;

internal static class AcquisitionSecretReference
{
    public static string? ResolveOptional(IAcquisitionSecretResolver resolver, string? reference, string label)
    {
        if (string.IsNullOrWhiteSpace(reference)) return null;
        return resolver.Resolve(reference) ??
               throw new InvalidOperationException($"{label}引用的现场密钥 {reference} 不存在。");
    }

    public static string ResolveRequired(IAcquisitionSecretResolver resolver, string? reference, string label)
    {
        if (string.IsNullOrWhiteSpace(reference))
            throw new InvalidOperationException($"{label}必须配置现场密钥引用。");
        return ResolveOptional(resolver, reference, label)!;
    }
}
