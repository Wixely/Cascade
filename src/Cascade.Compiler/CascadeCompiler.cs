using Cascade.Core;

namespace Cascade.Compiler;

public sealed record CascadeDiagnostic(string Code, string Message, int Line, int Column)
{
    public override string ToString() => $"{Line}:{Column}: {Code}: {Message}";
}

public sealed record CompilationResult(CascadeModule? Module, IReadOnlyList<CascadeDiagnostic> Diagnostics)
{
    public bool Succeeded => Module is not null && Diagnostics.Count == 0;
}

public static class CascadeCompiler
{
    public static CompilationResult Compile(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        try
        {
            var kernel = new Parser(new Lexer(source).Lex()).ParseKernel();
            return new(new Lowerer(kernel).Compile(), []);
        }
        catch (CompilationFailure failure)
        {
            return new(null, [failure.Diagnostic]);
        }
    }
}

internal enum TokenKind
{
    Identifier,
    Number,
    LeftParenthesis,
    RightParenthesis,
    LeftBrace,
    RightBrace,
    Less,
    Greater,
    Comma,
    Semicolon,
    Plus,
    Minus,
    Star,
    Ampersand,
    Pipe,
    Caret,
    Equal,
    EqualEqual,
    Arrow,
    End,
}

internal readonly record struct Token(TokenKind Kind, string Text, int Line, int Column);

internal sealed class Lexer(string source)
{
    private int _offset;
    private int _line = 1;
    private int _column = 1;

    public IReadOnlyList<Token> Lex()
    {
        var tokens = new List<Token>();
        while (_offset < source.Length)
        {
            if (char.IsWhiteSpace(Current))
            {
                Advance();
                continue;
            }

            if (Current == '/' && Peek() == '/')
            {
                while (_offset < source.Length && Current != '\n') Advance();
                continue;
            }

            var line = _line;
            var column = _column;
            if (char.IsAsciiLetter(Current) || Current == '_')
            {
                var start = _offset;
                while (_offset < source.Length && (char.IsAsciiLetterOrDigit(Current) || Current == '_')) Advance();
                tokens.Add(new(TokenKind.Identifier, source[start.._offset], line, column));
                continue;
            }

            if (char.IsAsciiDigit(Current))
            {
                var start = _offset;
                while (_offset < source.Length && char.IsAsciiDigit(Current)) Advance();
                tokens.Add(new(TokenKind.Number, source[start.._offset], line, column));
                continue;
            }

            var first = Current;
            Advance();
            tokens.Add(first switch
            {
                '(' => new(TokenKind.LeftParenthesis, "(", line, column),
                ')' => new(TokenKind.RightParenthesis, ")", line, column),
                '{' => new(TokenKind.LeftBrace, "{", line, column),
                '}' => new(TokenKind.RightBrace, "}", line, column),
                '<' => new(TokenKind.Less, "<", line, column),
                '>' => new(TokenKind.Greater, ">", line, column),
                ',' => new(TokenKind.Comma, ",", line, column),
                ';' => new(TokenKind.Semicolon, ";", line, column),
                '+' => new(TokenKind.Plus, "+", line, column),
                '*' => new(TokenKind.Star, "*", line, column),
                '&' => new(TokenKind.Ampersand, "&", line, column),
                '|' => new(TokenKind.Pipe, "|", line, column),
                '^' => new(TokenKind.Caret, "^", line, column),
                '-' when Current == '>' => Arrow(line, column),
                '-' => new(TokenKind.Minus, "-", line, column),
                '=' when Current == '=' => EqualEqual(line, column),
                '=' => new(TokenKind.Equal, "=", line, column),
                _ => throw Fail("CAS001", $"Unexpected character '{first}'.", line, column),
            });
        }

        tokens.Add(new(TokenKind.End, string.Empty, _line, _column));
        return tokens;
    }

    private Token Arrow(int line, int column)
    {
        Advance();
        return new(TokenKind.Arrow, "->", line, column);
    }

    private Token EqualEqual(int line, int column)
    {
        Advance();
        return new(TokenKind.EqualEqual, "==", line, column);
    }

    private char Current => source[_offset];
    private char Peek() => _offset + 1 < source.Length ? source[_offset + 1] : '\0';

    private void Advance()
    {
        if (source[_offset++] == '\n')
        {
            _line++;
            _column = 1;
        }
        else
        {
            _column++;
        }
    }

    private static CompilationFailure Fail(string code, string message, int line, int column) =>
        new(new(code, message, line, column));
}

internal enum SourceType
{
    VectorI32,
    VectorI64,
    MaskI32,
    MaskI64,
}

internal sealed record KernelSyntax(string Name, IReadOnlyList<ParameterSyntax> Parameters, SourceType ReturnType, IReadOnlyList<StatementSyntax> Statements, Token Token);
internal sealed record ParameterSyntax(SourceType Type, string Name, Token Token);
internal abstract record StatementSyntax(Token Token);
internal sealed record LetStatementSyntax(string Name, ExpressionSyntax Expression, Token Token) : StatementSyntax(Token);
internal sealed record ReturnStatementSyntax(ExpressionSyntax Expression, Token Token) : StatementSyntax(Token);
internal abstract record ExpressionSyntax(Token Token);
internal sealed record NameExpressionSyntax(string Name, Token Token) : ExpressionSyntax(Token);
internal sealed record LiteralExpressionSyntax(ulong Value, bool Negative, Token Token) : ExpressionSyntax(Token);
internal sealed record BinaryExpressionSyntax(ExpressionSyntax Left, Token Operator, ExpressionSyntax Right) : ExpressionSyntax(Operator);
internal sealed record SelectExpressionSyntax(ExpressionSyntax Condition, ExpressionSyntax WhenTrue, ExpressionSyntax WhenFalse, Token Token) : ExpressionSyntax(Token);

internal sealed class Parser(IReadOnlyList<Token> tokens)
{
    private int _position;

    public KernelSyntax ParseKernel()
    {
        var kernelToken = ExpectText("kernel");
        var name = Expect(TokenKind.Identifier, "Expected a kernel name.");
        Expect(TokenKind.LeftParenthesis, "Expected '(' after the kernel name.");
        var parameters = new List<ParameterSyntax>();
        if (Current.Kind != TokenKind.RightParenthesis)
        {
            do
            {
                var typeToken = Current;
                var type = ParseType(allowMask: false);
                var parameterName = Expect(TokenKind.Identifier, "Expected a parameter name.");
                parameters.Add(new(type, parameterName.Text, typeToken));
            }
            while (Match(TokenKind.Comma));
        }

        Expect(TokenKind.RightParenthesis, "Expected ')' after the parameters.");
        Expect(TokenKind.Arrow, "Expected '->' and a kernel return type.");
        var returnType = ParseType(allowMask: false);
        Expect(TokenKind.LeftBrace, "Expected '{' before the kernel body.");

        var statements = new List<StatementSyntax>();
        while (Current.Kind != TokenKind.RightBrace && Current.Kind != TokenKind.End)
        {
            if (Current.Text == "let")
            {
                var token = Advance();
                var localName = Expect(TokenKind.Identifier, "Expected a local name after 'let'.");
                Expect(TokenKind.Equal, "Expected '=' after the local name.");
                var expression = ParseExpression();
                Expect(TokenKind.Semicolon, "Expected ';' after the local binding.");
                statements.Add(new LetStatementSyntax(localName.Text, expression, token));
            }
            else if (Current.Text == "return")
            {
                var token = Advance();
                var expression = ParseExpression();
                Expect(TokenKind.Semicolon, "Expected ';' after the return expression.");
                statements.Add(new ReturnStatementSyntax(expression, token));
            }
            else
            {
                throw Error("CAS002", "Expected a 'let' or 'return' statement.", Current);
            }
        }

        Expect(TokenKind.RightBrace, "Expected '}' after the kernel body.");
        Expect(TokenKind.End, "Only one kernel may appear in a source file.");
        return new(name.Text, parameters, returnType, statements, kernelToken);
    }

    private SourceType ParseType(bool allowMask)
    {
        var constructor = Expect(TokenKind.Identifier, "Expected a type.");
        var isVector = constructor.Text == "vector";
        var isMask = constructor.Text == "mask";
        if (!isVector && !(allowMask && isMask))
            throw Error("CAS003", "Expected 'vector<i32>' or 'vector<i64>'.", constructor);
        Expect(TokenKind.Less, "Expected '<' in the type.");
        var lane = Expect(TokenKind.Identifier, "Expected an i32 or i64 lane type.");
        Expect(TokenKind.Greater, "Expected '>' after the lane type.");
        return (isVector, lane.Text) switch
        {
            (true, "i32") => SourceType.VectorI32,
            (true, "i64") => SourceType.VectorI64,
            (false, "i32") => SourceType.MaskI32,
            (false, "i64") => SourceType.MaskI64,
            _ => throw Error("CAS003", "Only i32 and i64 lane types are supported.", lane),
        };
    }

    private ExpressionSyntax ParseExpression(int parentPrecedence = 0)
    {
        ExpressionSyntax left;
        if (Match(TokenKind.Minus, out var minus))
        {
            var number = Expect(TokenKind.Number, "Unary '-' is currently supported only for integer literals.");
            left = new LiteralExpressionSyntax(ParseNumber(number), true, minus);
        }
        else
        {
            left = ParsePrimary();
        }

        while (true)
        {
            var precedence = Precedence(Current.Kind);
            if (precedence == 0 || precedence <= parentPrecedence) break;
            var op = Advance();
            var right = ParseExpression(precedence);
            left = new BinaryExpressionSyntax(left, op, right);
        }

        return left;
    }

    private ExpressionSyntax ParsePrimary()
    {
        if (Match(TokenKind.LeftParenthesis))
        {
            var expression = ParseExpression();
            Expect(TokenKind.RightParenthesis, "Expected ')' after the expression.");
            return expression;
        }

        if (Current.Kind == TokenKind.Number)
        {
            var number = Advance();
            return new LiteralExpressionSyntax(ParseNumber(number), false, number);
        }

        var name = Expect(TokenKind.Identifier, "Expected an expression.");
        if (name.Text == "select" && Match(TokenKind.LeftParenthesis))
        {
            var condition = ParseExpression();
            Expect(TokenKind.Comma, "Expected ',' after the select condition.");
            var whenTrue = ParseExpression();
            Expect(TokenKind.Comma, "Expected ',' after the true select value.");
            var whenFalse = ParseExpression();
            Expect(TokenKind.RightParenthesis, "Expected ')' after select arguments.");
            return new SelectExpressionSyntax(condition, whenTrue, whenFalse, name);
        }

        return new NameExpressionSyntax(name.Text, name);
    }

    private static int Precedence(TokenKind kind) => kind switch
    {
        TokenKind.Pipe => 1,
        TokenKind.Caret => 2,
        TokenKind.Ampersand => 3,
        TokenKind.EqualEqual or TokenKind.Less => 4,
        TokenKind.Plus or TokenKind.Minus => 5,
        TokenKind.Star => 6,
        _ => 0,
    };

    private static ulong ParseNumber(Token token) => ulong.TryParse(token.Text, out var value)
        ? value
        : throw Error("CAS004", "Integer literal is too large.", token);

    private Token ExpectText(string text)
    {
        if (Current.Kind == TokenKind.Identifier && Current.Text == text) return Advance();
        throw Error("CAS002", $"Expected '{text}'.", Current);
    }

    private Token Expect(TokenKind kind, string message)
    {
        if (Current.Kind == kind) return Advance();
        throw Error("CAS002", message, Current);
    }

    private bool Match(TokenKind kind)
    {
        if (Current.Kind != kind) return false;
        Advance();
        return true;
    }

    private bool Match(TokenKind kind, out Token token)
    {
        token = Current;
        return Match(kind);
    }

    private Token Advance() => tokens[_position++];
    private Token Current => tokens[_position];
    private static CompilationFailure Error(string code, string message, Token token) => new(new(code, message, token.Line, token.Column));
}

internal sealed class Lowerer
{
    private readonly KernelSyntax _kernel;
    private readonly Dictionary<string, SourceType> _types = new(StringComparer.Ordinal);
    private readonly Dictionary<ExpressionSyntax, SourceType> _expressionTypes = new();
    private readonly Dictionary<string, Value> _values = new(StringComparer.Ordinal);
    private readonly bool[] _registers = new bool[32];
    private readonly bool[] _masks = new bool[8];
    private readonly List<Instruction> _instructions = [];

    public Lowerer(KernelSyntax kernel) => _kernel = kernel;

    public CascadeModule Compile()
    {
        CheckSemantics();
        for (var index = 0; index < _kernel.Parameters.Count; index++)
        {
            _registers[index] = true;
            _values.Add(_kernel.Parameters[index].Name, new(_kernel.Parameters[index].Type, (byte)index, false));
        }

        foreach (var statement in _kernel.Statements)
        {
            switch (statement)
            {
                case LetStatementSyntax let:
                    var local = Emit(let.Expression);
                    _values.Add(let.Name, local with { Owned = false });
                    break;
                case ReturnStatementSyntax @return:
                    var result = Emit(@return.Expression);
                    if (result.Index != 0)
                        _instructions.Add(new(Opcode.Move, Destination: 0, SourceA: result.Index));
                    Release(result);
                    _instructions.Add(new(Opcode.Halt));
                    break;
            }
        }

        return new(_instructions);
    }

    private void CheckSemantics()
    {
        if (_kernel.Parameters.Count > 16)
            throw Error("CAS100", "Kernels may currently declare at most 16 parameters (v0-v15).", _kernel.Token);

        foreach (var parameter in _kernel.Parameters)
        {
            if (!_types.TryAdd(parameter.Name, parameter.Type))
                throw Error("CAS101", $"'{parameter.Name}' is already declared.", parameter.Token);
        }

        var sawReturn = false;
        foreach (var statement in _kernel.Statements)
        {
            if (sawReturn)
                throw Error("CAS102", "No statements may follow return.", statement.Token);

            switch (statement)
            {
                case LetStatementSyntax let:
                    var type = TypeOf(let.Expression, null);
                    if (!_types.TryAdd(let.Name, type))
                        throw Error("CAS101", $"'{let.Name}' is already declared.", let.Token);
                    break;
                case ReturnStatementSyntax @return:
                    var returnType = TypeOf(@return.Expression, _kernel.ReturnType);
                    Require(returnType == _kernel.ReturnType, "CAS103", $"Return type is {Display(returnType)}; expected {Display(_kernel.ReturnType)}.", @return.Token);
                    sawReturn = true;
                    break;
            }
        }

        if (!sawReturn)
            throw Error("CAS104", "The kernel must end with a return statement.", _kernel.Token);
    }

    private SourceType TypeOf(ExpressionSyntax expression, SourceType? expected)
    {
        if (_expressionTypes.TryGetValue(expression, out var cached)) return cached;
        SourceType type;
        switch (expression)
        {
            case NameExpressionSyntax name:
                if (!_types.TryGetValue(name.Name, out type))
                    throw Error("CAS105", $"Unknown name '{name.Name}'.", name.Token);
                break;
            case LiteralExpressionSyntax literal:
                if (expected is not SourceType.VectorI32 and not SourceType.VectorI64)
                    throw Error("CAS106", "An integer literal needs a vector context.", literal.Token);
                CheckLiteral(literal, expected.Value);
                type = expected.Value;
                break;
            case BinaryExpressionSyntax binary:
                type = TypeOfBinary(binary, expected);
                break;
            case SelectExpressionSyntax select:
                var conditionType = TypeOf(select.Condition, null);
                Require(IsMask(conditionType), "CAS107", "The first select argument must be a mask.", select.Condition.Token);
                var selectValueType = expected is SourceType.VectorI32 or SourceType.VectorI64 ? expected : VectorType(conditionType);
                var trueType = TypeOf(select.WhenTrue, selectValueType);
                Require(IsVector(trueType), "CAS108", "Select values must be vectors.", select.WhenTrue.Token);
                var falseType = TypeOf(select.WhenFalse, trueType);
                Require(falseType == trueType, "CAS109", "Both select values must have the same type.", select.WhenFalse.Token);
                Require(LaneType(conditionType) == LaneType(trueType), "CAS110", "The select mask and values must have the same lane type.", select.Token);
                type = trueType;
                break;
            default:
                throw new InvalidOperationException();
        }

        _expressionTypes.Add(expression, type);
        return type;
    }

    private SourceType TypeOfBinary(BinaryExpressionSyntax binary, SourceType? expected)
    {
        var contextual = expected is SourceType.VectorI32 or SourceType.VectorI64 ? expected : null;
        SourceType leftType;
        SourceType rightType;
        if (binary.Left is LiteralExpressionSyntax && binary.Right is not LiteralExpressionSyntax)
        {
            rightType = TypeOf(binary.Right, contextual);
            leftType = TypeOf(binary.Left, rightType);
        }
        else
        {
            leftType = TypeOf(binary.Left, contextual);
            rightType = TypeOf(binary.Right, leftType);
        }

        Require(IsVector(leftType) && rightType == leftType, "CAS111", "Binary operands must be vectors with the same lane type.", binary.Operator);
        if (binary.Operator.Kind == TokenKind.Star && leftType == SourceType.VectorI64)
            throw Error("CAS112", "i64 multiplication is not supported by CVM1.", binary.Operator);
        return binary.Operator.Kind switch
        {
            TokenKind.EqualEqual or TokenKind.Less => MaskType(leftType),
            TokenKind.Plus or TokenKind.Minus or TokenKind.Star or TokenKind.Ampersand or TokenKind.Pipe or TokenKind.Caret => leftType,
            _ => throw new InvalidOperationException(),
        };
    }

    private Value Emit(ExpressionSyntax expression) => expression switch
    {
        NameExpressionSyntax name => _values[name.Name],
        LiteralExpressionSyntax literal => EmitLiteral(literal),
        BinaryExpressionSyntax binary => EmitBinary(binary),
        SelectExpressionSyntax select => EmitSelect(select),
        _ => throw new InvalidOperationException(),
    };

    private Value EmitLiteral(LiteralExpressionSyntax literal)
    {
        var type = _expressionTypes[literal];
        var register = AllocateRegister(literal.Token);
        var value = literal.Negative ? unchecked(0UL - literal.Value) : literal.Value;
        _instructions.Add(new(type == SourceType.VectorI32 ? Opcode.SplatI32 : Opcode.SplatI64, Destination: register, Immediate: value));
        return new(type, register, true);
    }

    private Value EmitBinary(BinaryExpressionSyntax binary)
    {
        var left = Emit(binary.Left);
        var right = Emit(binary.Right);
        var type = _expressionTypes[binary];
        if (IsMask(type))
        {
            var mask = AllocateMask(binary.Token);
            _instructions.Add(new(ComparisonOpcode(binary.Operator.Kind, left.Type), SourceA: left.Index, SourceB: right.Index, Mask: mask));
            Release(left);
            Release(right);
            return new(type, mask, true);
        }

        var destination = left.Owned ? left.Index : right.Owned ? right.Index : AllocateRegister(binary.Token);
        _instructions.Add(new(VectorOpcode(binary.Operator.Kind, type), Destination: destination, SourceA: left.Index, SourceB: right.Index));
        if (left.Owned && left.Index != destination) Release(left);
        if (right.Owned && right.Index != destination) Release(right);
        return new(type, destination, true);
    }

    private Value EmitSelect(SelectExpressionSyntax select)
    {
        var condition = Emit(select.Condition);
        var whenTrue = Emit(select.WhenTrue);
        var whenFalse = Emit(select.WhenFalse);
        var destination = whenTrue.Owned ? whenTrue.Index : whenFalse.Owned ? whenFalse.Index : AllocateRegister(select.Token);
        var type = _expressionTypes[select];
        _instructions.Add(new(type == SourceType.VectorI32 ? Opcode.SelectI32 : Opcode.SelectI64,
            Destination: destination, SourceA: whenTrue.Index, SourceB: whenFalse.Index, Mask: condition.Index));
        Release(condition);
        if (whenTrue.Owned && whenTrue.Index != destination) Release(whenTrue);
        if (whenFalse.Owned && whenFalse.Index != destination) Release(whenFalse);
        return new(type, destination, true);
    }

    private byte AllocateRegister(Token token)
    {
        for (byte index = 0; index < _registers.Length; index++)
        {
            if (_registers[index]) continue;
            _registers[index] = true;
            return index;
        }

        throw Error("CAS200", "The kernel requires more than 32 vector registers.", token);
    }

    private byte AllocateMask(Token token)
    {
        for (byte index = 0; index < _masks.Length; index++)
        {
            if (_masks[index]) continue;
            _masks[index] = true;
            return index;
        }

        throw Error("CAS201", "The kernel requires more than eight mask registers.", token);
    }

    private void Release(Value value)
    {
        if (!value.Owned) return;
        if (IsMask(value.Type)) _masks[value.Index] = false;
        else _registers[value.Index] = false;
    }

    private static Opcode VectorOpcode(TokenKind op, SourceType type) => (op, type) switch
    {
        (TokenKind.Plus, SourceType.VectorI32) => Opcode.AddI32,
        (TokenKind.Minus, SourceType.VectorI32) => Opcode.SubI32,
        (TokenKind.Star, SourceType.VectorI32) => Opcode.MulI32,
        (TokenKind.Plus, SourceType.VectorI64) => Opcode.AddI64,
        (TokenKind.Minus, SourceType.VectorI64) => Opcode.SubI64,
        (TokenKind.Ampersand, _) => Opcode.And,
        (TokenKind.Pipe, _) => Opcode.Or,
        (TokenKind.Caret, _) => Opcode.Xor,
        _ => throw new InvalidOperationException(),
    };

    private static Opcode ComparisonOpcode(TokenKind op, SourceType type) => (op, type) switch
    {
        (TokenKind.EqualEqual, SourceType.VectorI32) => Opcode.CompareEqualI32,
        (TokenKind.Less, SourceType.VectorI32) => Opcode.CompareLessThanI32,
        (TokenKind.EqualEqual, SourceType.VectorI64) => Opcode.CompareEqualI64,
        (TokenKind.Less, SourceType.VectorI64) => Opcode.CompareLessThanI64,
        _ => throw new InvalidOperationException(),
    };

    private static void CheckLiteral(LiteralExpressionSyntax literal, SourceType type)
    {
        var maximum = type == SourceType.VectorI32
            ? literal.Negative ? 2_147_483_648UL : uint.MaxValue
            : literal.Negative ? 9_223_372_036_854_775_808UL : ulong.MaxValue;
        if (literal.Value > maximum)
            throw Error("CAS113", $"Literal does not fit {Display(type)}.", literal.Token);
    }

    private static bool IsVector(SourceType type) => type is SourceType.VectorI32 or SourceType.VectorI64;
    private static bool IsMask(SourceType type) => type is SourceType.MaskI32 or SourceType.MaskI64;
    private static int LaneType(SourceType type) => type is SourceType.VectorI32 or SourceType.MaskI32 ? 32 : 64;
    private static SourceType MaskType(SourceType type) => type == SourceType.VectorI32 ? SourceType.MaskI32 : SourceType.MaskI64;
    private static SourceType VectorType(SourceType type) => type == SourceType.MaskI32 ? SourceType.VectorI32 : SourceType.VectorI64;
    private static string Display(SourceType type) => type switch
    {
        SourceType.VectorI32 => "vector<i32>",
        SourceType.VectorI64 => "vector<i64>",
        SourceType.MaskI32 => "mask<i32>",
        SourceType.MaskI64 => "mask<i64>",
        _ => throw new InvalidOperationException(),
    };

    private static void Require(bool condition, string code, string message, Token token)
    {
        if (!condition) throw Error(code, message, token);
    }

    private static CompilationFailure Error(string code, string message, Token token) => new(new(code, message, token.Line, token.Column));
    private readonly record struct Value(SourceType Type, byte Index, bool Owned);
}

internal sealed class CompilationFailure(CascadeDiagnostic diagnostic) : Exception(diagnostic.Message)
{
    public CascadeDiagnostic Diagnostic { get; } = diagnostic;
}
