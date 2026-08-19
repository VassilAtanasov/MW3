namespace System.Runtime.CompilerServices;

// netstandard2.1 does not ship this type, but the compiler requires it for record types and
// init-only setters. Polyfill per the standard workaround for older TFMs.
internal static class IsExternalInit
{
}
