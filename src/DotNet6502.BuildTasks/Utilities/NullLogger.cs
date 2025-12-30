namespace DotNet6502;

class NullLogger : ILogger
{
    public void WriteLine(IFormattable message) { }
}
