using System.Collections.Immutable;
using System.Reflection.PortableExecutable;

namespace DotNet6502;

class ArrayValue
{
	readonly Lazy<ImmutableArray<byte>> _value;

	public ArrayValue(string name, PEMemoryBlock block, int size)
	{
		Name = name;
		_value = new Lazy<ImmutableArray<byte>>(() => block.GetContent(0, size));
	}

	public string Name { get; private set; }

	public ImmutableArray<byte> Value => _value.Value;
}
