using System.Diagnostics;
using Xunit.Abstractions;

namespace DotNet6502.Tests;

class XUnitLogger : ILogger
{
    readonly ITestOutputHelper _output;

    public XUnitLogger(ITestOutputHelper output) => _output = output;

    public void WriteLine(IFormattable message)
    {
        string text = message?.ToString() ?? "";
        Debug.WriteLine(text);
        _output.WriteLine(text);
    }
}
