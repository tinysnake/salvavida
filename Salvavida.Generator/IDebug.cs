using System.Diagnostics;
using System.Text;

namespace Salvavida.Generator;

public class TraceDebug : IDebug
{
    public void Log(string message)
    {
        Trace.WriteLine(message);
    }
}

public class SourceDebug : IDebug
{
    public SourceDebug(string sourceFile)
    {
        SourceFile = sourceFile.Replace("/", "_").Replace("\\", "_").Replace("<", "_").Replace(">", "_");
    }

    public string SourceFile { get; }

    private readonly StringBuilder _sb = new();

    public void Log(string message)
    {
        _sb.AppendLine("----------");
        _sb.AppendLine(message);
        _sb.AppendLine();
    }

    public override string ToString()
    {
        return _sb.ToString();
    }
}

public interface IDebug
{
    void Log(string message);
}
