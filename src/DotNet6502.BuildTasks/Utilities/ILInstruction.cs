using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace DotNet6502;

/// <summary>
/// Holds info about IL
/// </summary>
record ILInstruction(ILOpCode OpCode, int Offset = 0, int? Integer = null, string? String = null, ImmutableArray<byte>? Bytes = null);
