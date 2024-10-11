using System;
using System.Collections.Generic;
using System.Text;

namespace Salvavida.Generator
{
    public class ScriptBuilder
    {
        public struct CurlyBracketScope : IDisposable
        {
            public CurlyBracketScope(ScriptBuilder sb, bool indent)
            {
                _sb = sb;
                _indent = indent;
                sb.BeginCurlyBrackets(indent);
            }

            private ScriptBuilder? _sb;
            private readonly bool _indent;

            public void Dispose()
            {
                _sb?.EndCurlyBrackets(_indent);
                _sb = null;
            }
        }
        private readonly string NEW_LINE = Environment.NewLine;

        public ScriptBuilder()
        {
            _builder = new StringBuilder();
        }

        private int _currentCharIndex;
        private readonly Stack<int> _bracketStack = new Stack<int>();
        private readonly StringBuilder _builder;

        public int Indent { get; set; }


        public void Write(string val, bool noAutoIndent = false)
        {
            if (!noAutoIndent)
                val = GetIndents() + val;
            if (_currentCharIndex == _builder.Length)
                _builder.Append(val);
            else
                _builder.Insert(_currentCharIndex, val);
            _currentCharIndex += val.Length;
        }

        public void WriteLine(string val, bool noAutoIndent = false)
        {
            Write(val + NEW_LINE, noAutoIndent);
        }

        public void WriteLine()
        {
            Write(NEW_LINE, false);
        }

        private int WriteCurlyBrackets(bool increaseIndent = false)
        {
            var openBracket = GetIndents() + "{" + NEW_LINE;
            var closeBracket = GetIndents() + "}" + NEW_LINE;
            Write(openBracket + closeBracket, true);
            _currentCharIndex -= closeBracket.Length;
            if (increaseIndent)
                Indent++;
            return closeBracket.Length;
        }

        public void BeginCurlyBrackets(bool increaseIndent = true)
        {
            _bracketStack.Push(WriteCurlyBrackets(increaseIndent));
        }

        public CurlyBracketScope CurlyBracketsScope(bool increaseIndent = true)
        {
            return new CurlyBracketScope(this, increaseIndent);
        }

        private void GetOutOfCurlyBrackets(int lastCurlyBracketSize, bool decreaseIndent = false)
        {
            _currentCharIndex += lastCurlyBracketSize;
            if (decreaseIndent)
                Indent--;
        }


        public void EndCurlyBrackets(bool decreaseIndent = true)
        {
            if (_bracketStack.Count == 0)
                return;
            GetOutOfCurlyBrackets(_bracketStack.Pop(), decreaseIndent);
        }

        public string GetIndents()
        {
            var str = "";
            for (var i = 0; i < Indent; i++)
                str += "    ";
            return str;
        }

        public override string ToString()
        {
            return _builder.ToString();
        }
    }
}