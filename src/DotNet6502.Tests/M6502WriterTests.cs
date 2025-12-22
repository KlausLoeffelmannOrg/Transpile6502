using Xunit.Abstractions;

namespace DotNet6502.Tests;

public class M6502WriterTests
{
    const ushort SizeOfMain = 67;
    readonly MemoryStream _stream = new();
    readonly ILogger _logger;

    public M6502WriterTests(ITestOutputHelper output)
    {
        _logger = new XUnitLogger(output);
    }

    M6502Writer GetWriter(byte[]? PRG_ROM = null, byte[]? CHR_ROM = null)
    {
        _stream.SetLength(0);

        return new M6502Writer(_stream, leaveOpen: true, logger: _logger)
        {
            PRG_ROM = PRG_ROM,
            CHR_ROM = CHR_ROM,
        };
    }

    void AssertInstructions(string assembly)
    {
        var expected = Utilities.ToByteArray(assembly);
        var actual = _stream.ToArray();
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void WriteLDA()
    {
        using var writer = GetWriter();
        writer.Write(M6502Instruction.LDA, 0x08);
        writer.Flush();

        // 8076	A908          	LDA #$08  
        AssertInstructions("A908");
    }

    [Fact]
    public void WriteJSR()
    {
        using var writer = GetWriter();
        writer.Write(M6502Instruction.JSR, 0x84F4);
        writer.Flush();

        // 807A	20F484        	JSR initlib
        AssertInstructions("20F484");
    }
}
