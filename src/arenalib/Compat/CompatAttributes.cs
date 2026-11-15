// Attributes that Roslyn recognizes by full name but that zerolib does not declare.
//
// RequiresLocationAttribute is the important one: without it, declaring a `ref readonly`
// parameter fails with CS0518. Roslyn only auto-embeds this attribute when the
// compilation *is* the corlib (which is why zerolib's own Unsafe.ByteOffset compiles),
// and the embedded copy is internal, so it is invisible to anything referencing zerolib.
//
// These are public so that programs built with --stdlib:arena get `ref readonly` and
// [UnscopedRef] for free, without having to redeclare them.
//
// Note: no [AttributeUsage] here. zerolib's AttributeTargets is an empty enum, so
// AttributeTargets.Parameter does not exist to reference.

namespace System.Runtime.CompilerServices
{
    public sealed class RequiresLocationAttribute : Attribute
    {
    }
}

namespace System.Diagnostics.CodeAnalysis
{
    public sealed class UnscopedRefAttribute : Attribute
    {
    }
}
