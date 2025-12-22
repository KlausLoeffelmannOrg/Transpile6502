using Xunit.Abstractions;

namespace DotNet6502.Tests;

public class IL2M6502WriterTests
{
    readonly MemoryStream _stream = new();
    readonly ILogger _logger;

    public IL2M6502WriterTests(ITestOutputHelper output)
    {
        _logger = new XUnitLogger(output);
    }

    IL2M6502Writer GetWriter(byte[]? PRG_ROM = null, byte[]? CHR_ROM = null)
    {
        _stream.SetLength(0);

        return new IL2M6502Writer(_stream, leaveOpen: true, logger: _logger)
        {
            PRG_ROM = PRG_ROM,
            CHR_ROM = CHR_ROM,
        };
    }
}
