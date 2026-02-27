// Polyfill for .NET Framework 4.8 to support init-only properties (records)
// This type is required by the compiler for init accessors in C# 9+

#if !NET5_0_OR_GREATER
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit { }
}
#endif
