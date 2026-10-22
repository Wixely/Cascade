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
    public const string LanguageVersion = "0.3";

    public static CompilationResult Compile(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        try
        {
            var program = new Parser(new Lexer(source).Lex()).ParseProgram();
            return new(new Lowerer(program).Compile(), []);
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

internal sealed record ProgramSyntax(IReadOnlyList<FunctionSyntax> Functions, KernelSyntax Kernel);
internal sealed record FunctionSyntax(string Name, IReadOnlyList<ParameterSyntax> Parameters, SourceType ReturnType, IReadOnlyList<StatementSyntax> Statements, Token Token);
internal sealed record KernelSyntax(string Name, IReadOnlyList<ParameterSyntax> Parameters, SourceType ReturnType, IReadOnlyList<StatementSyntax> Statements, Token Token);
internal sealed record ParameterSyntax(SourceType Type, string Name, Token Token);
internal abstract record StatementSyntax(Token Token);
internal sealed record LetStatementSyntax(string Name, ExpressionSyntax Expression, Token Token) : StatementSyntax(Token);
internal sealed record VarStatementSyntax(string Name, ExpressionSyntax Expression, Token Token) : StatementSyntax(Token);
internal sealed record AssignmentStatementSyntax(string Name, ExpressionSyntax Expression, Token Token) : StatementSyntax(Token);
internal sealed record RepeatStatementSyntax(int Count, IReadOnlyList<StatementSyntax> Statements, Token Token) : StatementSyntax(Token);
internal sealed record StoreStatementSyntax(ExpressionSyntax Address, ExpressionSyntax Value, Token Token) : StatementSyntax(Token);
internal sealed record ScatterStatementSyntax(string Name, IReadOnlyList<ExpressionSyntax> Arguments, Token Token) : StatementSyntax(Token);
internal sealed record ReturnStatementSyntax(ExpressionSyntax Expression, Token Token) : StatementSyntax(Token);
internal abstract record ExpressionSyntax(Token Token);
internal sealed record NameExpressionSyntax(string Name, Token Token) : ExpressionSyntax(Token);
internal sealed record LiteralExpressionSyntax(ulong Value, bool Negative, Token Token) : ExpressionSyntax(Token);
internal sealed record BinaryExpressionSyntax(ExpressionSyntax Left, Token Operator, ExpressionSyntax Right) : ExpressionSyntax(Operator);
internal sealed record CallExpressionSyntax(string Name, IReadOnlyList<ExpressionSyntax> Arguments, Token Token) : ExpressionSyntax(Token);

internal sealed class Parser(IReadOnlyList<Token> tokens)
{
    private int _position;

    public ProgramSyntax ParseProgram()
    {
        var functions = new List<FunctionSyntax>();
        while (Current.Kind == TokenKind.Identifier && Current.Text == "fn")
            functions.Add(ParseFunction());

        var kernelToken = ExpectText("kernel");
        var name = Expect(TokenKind.Identifier, "Expected a kernel name.");
        var parameters = ParseParameters();
        Expect(TokenKind.Arrow, "Expected '->' and a kernel return type.");
        var returnType = ParseType(allowMask: false);
        var statements = ParseBlock();
        Expect(TokenKind.End, "Only functions followed by one kernel may appear in a source file.");
        return new(functions, new(name.Text, parameters, returnType, statements, kernelToken));
    }

    private FunctionSyntax ParseFunction()
    {
        var functionToken = ExpectText("fn");
        var name = Expect(TokenKind.Identifier, "Expected a function name.");
        var parameters = ParseParameters();
        Expect(TokenKind.Arrow, "Expected '->' and a function return type.");
        var returnType = ParseType(allowMask: false);
        return new(name.Text, parameters, returnType, ParseBlock(), functionToken);
    }

    private IReadOnlyList<ParameterSyntax> ParseParameters()
    {
        Expect(TokenKind.LeftParenthesis, "Expected '('.");
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
        return parameters;
    }

    private IReadOnlyList<StatementSyntax> ParseBlock()
    {
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
            else if (Current.Text == "var")
            {
                var token = Advance();
                var localName = Expect(TokenKind.Identifier, "Expected a variable name after 'var'.");
                Expect(TokenKind.Equal, "Expected '=' after the variable name.");
                var expression = ParseExpression();
                Expect(TokenKind.Semicolon, "Expected ';' after the variable declaration.");
                statements.Add(new VarStatementSyntax(localName.Text, expression, token));
            }
            else if (Current.Text == "return")
            {
                var token = Advance();
                var expression = ParseExpression();
                Expect(TokenKind.Semicolon, "Expected ';' after the return expression.");
                statements.Add(new ReturnStatementSyntax(expression, token));
            }
            else if (Current.Text == "repeat")
            {
                var token = Advance();
                var count = Expect(TokenKind.Number, "Expected a repeat count.");
                if (!int.TryParse(count.Text, out var value) || value is < 1 or > 1024)
                    throw Error("CAS005", "Repeat count must be between 1 and 1024.", count);
                statements.Add(new RepeatStatementSyntax(value, ParseBlock(), token));
            }
            else if (Current.Text == "store")
            {
                var token = Advance();
                Expect(TokenKind.LeftParenthesis, "Expected '(' after 'store'.");
                var address = ParseExpression();
                Expect(TokenKind.Comma, "Expected ',' after the store address.");
                var value = ParseExpression();
                Expect(TokenKind.RightParenthesis, "Expected ')' after store arguments.");
                Expect(TokenKind.Semicolon, "Expected ';' after store.");
                statements.Add(new StoreStatementSyntax(address, value, token));
            }
            else if (Current.Text is "scatter_i32" or "scatter_i64")
            {
                var token = Advance();
                Expect(TokenKind.LeftParenthesis, $"Expected '(' after '{token.Text}'.");
                var arguments = new List<ExpressionSyntax>();
                if (Current.Kind != TokenKind.RightParenthesis)
                {
                    do arguments.Add(ParseExpression());
                    while (Match(TokenKind.Comma));
                }
                Expect(TokenKind.RightParenthesis, $"Expected ')' after {token.Text} arguments.");
                Expect(TokenKind.Semicolon, $"Expected ';' after {token.Text}.");
                statements.Add(new ScatterStatementSyntax(token.Text, arguments, token));
            }
            else if (Current.Kind == TokenKind.Identifier && Peek.Kind == TokenKind.Equal)
            {
                var name = Advance();
                Advance();
                var expression = ParseExpression();
                Expect(TokenKind.Semicolon, "Expected ';' after assignment.");
                statements.Add(new AssignmentStatementSyntax(name.Text, expression, name));
            }
            else
            {
                throw Error("CAS002", "Expected a declaration, assignment, repeat, store, or return statement.", Current);
            }
        }

        Expect(TokenKind.RightBrace, "Expected '}' after the kernel body.");
        return statements;
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
        if (Match(TokenKind.LeftParenthesis))
        {
            var arguments = new List<ExpressionSyntax>();
            if (Current.Kind != TokenKind.RightParenthesis)
            {
                do arguments.Add(ParseExpression());
                while (Match(TokenKind.Comma));
            }
            Expect(TokenKind.RightParenthesis, "Expected ')' after call arguments.");
            return new CallExpressionSyntax(name.Text, arguments, name);
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
    private Token Peek => tokens[Math.Min(_position + 1, tokens.Count - 1)];
    private static CompilationFailure Error(string code, string message, Token token) => new(new(code, message, token.Line, token.Column));
}

internal sealed class Lowerer
{
    private const int MaximumInstructions = 1_000_000;
    private readonly ProgramSyntax _program;
    private readonly Dictionary<string, FunctionSyntax> _functions = new(StringComparer.Ordinal);
    private readonly Dictionary<ExpressionSyntax, SourceType> _expressionTypes = new();
    private readonly HashSet<string> _activeCalls = new(StringComparer.Ordinal);
    private readonly bool[] _registers = new bool[32];
    private readonly bool[] _masks = new bool[8];
    private readonly List<Instruction> _instructions = [];

    public Lowerer(ProgramSyntax program) => _program = program;

    public CascadeModule Compile()
    {
        CheckSemantics();
        var scope = new ValueScope(null);
        for (var index = 0; index < _program.Kernel.Parameters.Count; index++)
        {
            _registers[index] = true;
            scope.Define(_program.Kernel.Parameters[index].Name,
                new(new(_program.Kernel.Parameters[index].Type, (byte)index, false), Mutable: false, OwnsStorage: false));
        }

        var result = EmitStatements(_program.Kernel.Statements, scope);
        if (result.Index != 0)
            Add(new(Opcode.Move, Destination: 0, SourceA: result.Index), _program.Kernel.Token);
        Release(result);
        Add(new(Opcode.Halt), _program.Kernel.Token);
        return new(_instructions);
    }

    private void CheckSemantics()
    {
        foreach (var function in _program.Functions)
        {
            if (IsBuiltin(function.Name) || !_functions.TryAdd(function.Name, function))
                throw Error("CAS114", $"Function name '{function.Name}' is reserved or duplicated.", function.Token);
        }
        if (_functions.ContainsKey(_program.Kernel.Name))
            throw Error("CAS114", $"Kernel name '{_program.Kernel.Name}' duplicates a function.", _program.Kernel.Token);

        foreach (var function in _program.Functions)
            CheckRoutine(function.Parameters, function.ReturnType, function.Statements, function.Token, "Function");
        CheckRoutine(_program.Kernel.Parameters, _program.Kernel.ReturnType, _program.Kernel.Statements, _program.Kernel.Token, "Kernel");
        RejectRecursiveFunctions();
    }

    private void RejectRecursiveFunctions()
    {
        var states = new Dictionary<string, byte>(StringComparer.Ordinal);
        foreach (var function in _program.Functions) Visit(function);

        void Visit(FunctionSyntax function)
        {
            if (states.TryGetValue(function.Name, out var state) && state == 2) return;
            states[function.Name] = 1;
            foreach (var call in CallsIn(function.Statements))
            {
                if (!_functions.TryGetValue(call.Name, out var target)) continue;
                if (states.TryGetValue(target.Name, out state) && state == 1)
                    throw Error("CAS125", $"Recursive call to '{target.Name}' is not allowed.", call.Token);
                Visit(target);
            }
            states[function.Name] = 2;
        }
    }

    private static IEnumerable<CallExpressionSyntax> CallsIn(IEnumerable<StatementSyntax> statements)
    {
        foreach (var statement in statements)
        {
            var expressions = statement switch
            {
                LetStatementSyntax let => new[] { let.Expression },
                VarStatementSyntax variable => new[] { variable.Expression },
                AssignmentStatementSyntax assignment => new[] { assignment.Expression },
                StoreStatementSyntax store => new[] { store.Address, store.Value },
                ScatterStatementSyntax scatter => scatter.Arguments.ToArray(),
                ReturnStatementSyntax @return => new[] { @return.Expression },
                _ => [],
            };
            foreach (var expression in expressions)
                foreach (var call in CallsIn(expression)) yield return call;
            if (statement is RepeatStatementSyntax repeat)
                foreach (var call in CallsIn(repeat.Statements)) yield return call;
        }
    }

    private static IEnumerable<CallExpressionSyntax> CallsIn(ExpressionSyntax expression)
    {
        if (expression is BinaryExpressionSyntax binary)
        {
            foreach (var call in CallsIn(binary.Left)) yield return call;
            foreach (var call in CallsIn(binary.Right)) yield return call;
        }
        else if (expression is CallExpressionSyntax call)
        {
            yield return call;
            foreach (var argument in call.Arguments)
                foreach (var nested in CallsIn(argument)) yield return nested;
        }
    }

    private void CheckRoutine(IReadOnlyList<ParameterSyntax> parameters, SourceType returnType, IReadOnlyList<StatementSyntax> statements, Token token, string kind)
    {
        if (parameters.Count > 16)
            throw Error("CAS100", $"{kind}s may declare at most 16 parameters.", token);
        var scope = new TypeScope(null);
        foreach (var parameter in parameters)
            DefineType(scope, parameter.Name, new(parameter.Type, Mutable: false), parameter.Token);
        if (!CheckStatements(statements, scope, returnType, allowReturn: true))
            throw Error("CAS104", $"The {kind.ToLowerInvariant()} must end with a return statement.", token);
    }

    private bool CheckStatements(IReadOnlyList<StatementSyntax> statements, TypeScope scope, SourceType returnType, bool allowReturn)
    {
        var sawReturn = false;
        foreach (var statement in statements)
        {
            if (sawReturn) throw Error("CAS102", "No statements may follow return.", statement.Token);
            switch (statement)
            {
                case LetStatementSyntax let:
                    DefineType(scope, let.Name, new(TypeOf(let.Expression, null, scope), Mutable: false), let.Token);
                    break;
                case VarStatementSyntax variable:
                    var variableType = TypeOf(variable.Expression, null, scope);
                    Require(IsVector(variableType), "CAS115", "Mutable variables must contain vectors.", variable.Token);
                    DefineType(scope, variable.Name, new(variableType, Mutable: true), variable.Token);
                    break;
                case AssignmentStatementSyntax assignment:
                    if (!scope.TryGet(assignment.Name, out var assigned))
                        throw Error("CAS105", $"Unknown name '{assignment.Name}'.", assignment.Token);
                    Require(assigned.Mutable, "CAS116", $"'{assignment.Name}' is immutable.", assignment.Token);
                    var assignedType = TypeOf(assignment.Expression, assigned.Type, scope);
                    Require(assignedType == assigned.Type, "CAS117", "Assignment type does not match the variable type.", assignment.Token);
                    break;
                case RepeatStatementSyntax repeat:
                    CheckStatements(repeat.Statements, new(scope), returnType, allowReturn: false);
                    break;
                case StoreStatementSyntax store:
                    Require(TypeOf(store.Address, SourceType.VectorI64, scope) == SourceType.VectorI64,
                        "CAS118", "Store addresses must be vector<i64>.", store.Address.Token);
                    Require(IsVector(TypeOf(store.Value, null, scope)), "CAS119", "Store values must be vectors.", store.Value.Token);
                    break;
                case ScatterStatementSyntax scatter:
                    CheckScatter(scatter, scope);
                    break;
                case ReturnStatementSyntax @return:
                    Require(allowReturn, "CAS120", "Return is not allowed inside repeat.", @return.Token);
                    var actual = TypeOf(@return.Expression, returnType, scope);
                    Require(actual == returnType, "CAS103", $"Return type is {Display(actual)}; expected {Display(returnType)}.", @return.Token);
                    sawReturn = true;
                    break;
            }
        }
        return sawReturn;
    }

    private void CheckScatter(ScatterStatementSyntax scatter, TypeScope scope)
    {
        var isI32 = scatter.Name == "scatter_i32";
        var expectedCount = isI32 ? 4 : 3;
        Require(scatter.Arguments.Count == expectedCount, "CAS126",
            $"{scatter.Name} requires {expectedCount} arguments.", scatter.Token);
        Require(TypeOf(scatter.Arguments[0], SourceType.VectorI64, scope) == SourceType.VectorI64,
            "CAS118", "Scatter addresses must be vector<i64>.", scatter.Arguments[0].Token);
        var valueIndex = isI32 ? 2 : 1;
        var maskIndex = isI32 ? 3 : 2;
        if (isI32)
            Require(TypeOf(scatter.Arguments[1], SourceType.VectorI64, scope) == SourceType.VectorI64,
                "CAS118", "scatter_i32 requires two vector<i64> address vectors.", scatter.Arguments[1].Token);
        var valueType = isI32 ? SourceType.VectorI32 : SourceType.VectorI64;
        var maskType = isI32 ? SourceType.MaskI32 : SourceType.MaskI64;
        Require(TypeOf(scatter.Arguments[valueIndex], valueType, scope) == valueType,
            "CAS127", $"{scatter.Name} has the wrong value type.", scatter.Arguments[valueIndex].Token);
        Require(TypeOf(scatter.Arguments[maskIndex], null, scope) == maskType,
            "CAS128", $"{scatter.Name} has the wrong mask type.", scatter.Arguments[maskIndex].Token);
    }

    private SourceType TypeOf(ExpressionSyntax expression, SourceType? expected, TypeScope scope)
    {
        if (_expressionTypes.TryGetValue(expression, out var cached)) return cached;
        SourceType type;
        switch (expression)
        {
            case NameExpressionSyntax name:
                if (!scope.TryGet(name.Name, out var symbol))
                    throw Error("CAS105", $"Unknown name '{name.Name}'.", name.Token);
                type = symbol.Type;
                break;
            case LiteralExpressionSyntax literal:
                if (expected is not SourceType.VectorI32 and not SourceType.VectorI64)
                    throw Error("CAS106", "An integer literal needs a vector context.", literal.Token);
                CheckLiteral(literal, expected.Value);
                type = expected.Value;
                break;
            case BinaryExpressionSyntax binary:
                type = TypeOfBinary(binary, expected, scope);
                break;
            case CallExpressionSyntax call:
                type = TypeOfCall(call, expected, scope);
                break;
            default:
                throw new InvalidOperationException();
        }
        _expressionTypes.Add(expression, type);
        return type;
    }

    private SourceType TypeOfCall(CallExpressionSyntax call, SourceType? expected, TypeScope scope)
    {
        if (call.Name == "select")
        {
            Require(call.Arguments.Count == 3, "CAS121", "select requires three arguments.", call.Token);
            var conditionType = TypeOf(call.Arguments[0], null, scope);
            Require(IsMask(conditionType), "CAS107", "The first select argument must be a mask.", call.Arguments[0].Token);
            var valueType = expected is SourceType.VectorI32 or SourceType.VectorI64 ? expected.Value : VectorType(conditionType);
            var trueType = TypeOf(call.Arguments[1], valueType, scope);
            var falseType = TypeOf(call.Arguments[2], trueType, scope);
            Require(IsVector(trueType) && falseType == trueType, "CAS109", "Both select values must have the same vector type.", call.Token);
            Require(LaneType(conditionType) == LaneType(trueType), "CAS110", "The select mask and values must have the same lane type.", call.Token);
            return trueType;
        }

        if (call.Name is "load_i32" or "load_i64")
        {
            Require(call.Arguments.Count == 1, "CAS121", $"{call.Name} requires one address argument.", call.Token);
            Require(TypeOf(call.Arguments[0], SourceType.VectorI64, scope) == SourceType.VectorI64,
                "CAS118", "Load addresses must be vector<i64>.", call.Arguments[0].Token);
            return call.Name == "load_i32" ? SourceType.VectorI32 : SourceType.VectorI64;
        }

        if (call.Name is "gather_i32" or "gather_i64")
        {
            var isI32 = call.Name == "gather_i32";
            var expectedCount = isI32 ? 3 : 2;
            Require(call.Arguments.Count == expectedCount, "CAS126",
                $"{call.Name} requires {expectedCount} arguments.", call.Token);
            Require(TypeOf(call.Arguments[0], SourceType.VectorI64, scope) == SourceType.VectorI64,
                "CAS118", "Gather addresses must be vector<i64>.", call.Arguments[0].Token);
            if (isI32)
                Require(TypeOf(call.Arguments[1], SourceType.VectorI64, scope) == SourceType.VectorI64,
                    "CAS118", "gather_i32 requires two vector<i64> address vectors.", call.Arguments[1].Token);
            var maskIndex = isI32 ? 2 : 1;
            var maskType = isI32 ? SourceType.MaskI32 : SourceType.MaskI64;
            Require(TypeOf(call.Arguments[maskIndex], null, scope) == maskType,
                "CAS128", $"{call.Name} has the wrong mask type.", call.Arguments[maskIndex].Token);
            return isI32 ? SourceType.VectorI32 : SourceType.VectorI64;
        }

        if (call.Name is "mask_all_i32" or "mask_none_i32" or "mask_all_i64" or "mask_none_i64")
        {
            Require(call.Arguments.Count == 0, "CAS121", $"{call.Name} takes no arguments.", call.Token);
            return call.Name.EndsWith("i32", StringComparison.Ordinal) ? SourceType.MaskI32 : SourceType.MaskI64;
        }

        if (call.Name == "mask_not")
        {
            Require(call.Arguments.Count == 1, "CAS121", "mask_not requires one argument.", call.Token);
            var maskType = TypeOf(call.Arguments[0], null, scope);
            Require(IsMask(maskType), "CAS129", "mask_not requires a mask.", call.Arguments[0].Token);
            return maskType;
        }

        if (call.Name is "mask_and" or "mask_or" or "mask_xor")
        {
            Require(call.Arguments.Count == 2, "CAS121", $"{call.Name} requires two arguments.", call.Token);
            var left = TypeOf(call.Arguments[0], null, scope);
            var right = TypeOf(call.Arguments[1], null, scope);
            Require(IsMask(left) && right == left, "CAS129", $"{call.Name} requires masks with the same lane type.", call.Token);
            return left;
        }

        if (!_functions.TryGetValue(call.Name, out var function))
            throw Error("CAS122", $"Unknown function '{call.Name}'.", call.Token);
        Require(call.Arguments.Count == function.Parameters.Count, "CAS123",
            $"Function '{call.Name}' expects {function.Parameters.Count} arguments; received {call.Arguments.Count}.", call.Token);
        for (var index = 0; index < call.Arguments.Count; index++)
        {
            var actual = TypeOf(call.Arguments[index], function.Parameters[index].Type, scope);
            Require(actual == function.Parameters[index].Type, "CAS124", $"Argument {index + 1} has the wrong type.", call.Arguments[index].Token);
        }
        return function.ReturnType;
    }

    private SourceType TypeOfBinary(BinaryExpressionSyntax binary, SourceType? expected, TypeScope scope)
    {
        var contextual = expected is SourceType.VectorI32 or SourceType.VectorI64 ? expected : null;
        SourceType leftType;
        SourceType rightType;
        if (binary.Left is LiteralExpressionSyntax && binary.Right is not LiteralExpressionSyntax)
        {
            rightType = TypeOf(binary.Right, contextual, scope);
            leftType = TypeOf(binary.Left, rightType, scope);
        }
        else
        {
            leftType = TypeOf(binary.Left, contextual, scope);
            rightType = TypeOf(binary.Right, leftType, scope);
        }
        Require(IsVector(leftType) && rightType == leftType, "CAS111", "Binary operands must be vectors with the same lane type.", binary.Operator);
        if (binary.Operator.Kind == TokenKind.Star && leftType == SourceType.VectorI64)
            throw Error("CAS112", "i64 multiplication is not supported by CVM1.", binary.Operator);
        return binary.Operator.Kind is TokenKind.EqualEqual or TokenKind.Less ? MaskType(leftType) : leftType;
    }

    private Value EmitStatements(IReadOnlyList<StatementSyntax> statements, ValueScope scope)
    {
        foreach (var statement in statements)
        {
            switch (statement)
            {
                case LetStatementSyntax let:
                    var local = Emit(let.Expression, scope);
                    scope.Define(let.Name, new(local with { Owned = false }, Mutable: false, OwnsStorage: local.Owned));
                    break;
                case VarStatementSyntax variable:
                    var initial = Emit(variable.Expression, scope);
                    var storage = initial.Owned ? initial : Copy(initial, variable.Token);
                    scope.Define(variable.Name, new(storage with { Owned = false }, Mutable: true, OwnsStorage: true));
                    break;
                case AssignmentStatementSyntax assignment:
                    var target = scope.Get(assignment.Name);
                    var assigned = Emit(assignment.Expression, scope);
                    if (assigned.Index != target.Value.Index)
                        Add(new(Opcode.Move, Destination: target.Value.Index, SourceA: assigned.Index), assignment.Token);
                    Release(assigned);
                    break;
                case RepeatStatementSyntax repeat:
                    for (var iteration = 0; iteration < repeat.Count; iteration++)
                    {
                        var child = new ValueScope(scope);
                        _ = EmitStatementsWithoutReturn(repeat.Statements, child);
                        Dispose(child);
                    }
                    break;
                case StoreStatementSyntax store:
                    var address = Emit(store.Address, scope);
                    var value = Emit(store.Value, scope);
                    Add(new(Opcode.Store, SourceA: address.Index, SourceB: value.Index), store.Token);
                    Release(address);
                    Release(value);
                    break;
                case ScatterStatementSyntax scatter:
                    EmitScatter(scatter, scope);
                    break;
                case ReturnStatementSyntax @return:
                    return Emit(@return.Expression, scope);
            }
        }
        throw new InvalidOperationException("Semantics require a return statement.");
    }

    private bool EmitStatementsWithoutReturn(IReadOnlyList<StatementSyntax> statements, ValueScope scope)
    {
        foreach (var statement in statements)
        {
            if (statement is ReturnStatementSyntax) throw new InvalidOperationException();
            switch (statement)
            {
                case LetStatementSyntax let:
                    var local = Emit(let.Expression, scope);
                    scope.Define(let.Name, new(local with { Owned = false }, Mutable: false, OwnsStorage: local.Owned));
                    break;
                case VarStatementSyntax variable:
                    var initial = Emit(variable.Expression, scope);
                    var storage = initial.Owned ? initial : Copy(initial, variable.Token);
                    scope.Define(variable.Name, new(storage with { Owned = false }, Mutable: true, OwnsStorage: true));
                    break;
                case AssignmentStatementSyntax assignment:
                    var target = scope.Get(assignment.Name);
                    var assigned = Emit(assignment.Expression, scope);
                    if (assigned.Index != target.Value.Index)
                        Add(new(Opcode.Move, Destination: target.Value.Index, SourceA: assigned.Index), assignment.Token);
                    Release(assigned);
                    break;
                case RepeatStatementSyntax repeat:
                    for (var iteration = 0; iteration < repeat.Count; iteration++)
                    {
                        var child = new ValueScope(scope);
                        EmitStatementsWithoutReturn(repeat.Statements, child);
                        Dispose(child);
                    }
                    break;
                case StoreStatementSyntax store:
                    var address = Emit(store.Address, scope);
                    var value = Emit(store.Value, scope);
                    Add(new(Opcode.Store, SourceA: address.Index, SourceB: value.Index), store.Token);
                    Release(address);
                    Release(value);
                    break;
                case ScatterStatementSyntax scatter:
                    EmitScatter(scatter, scope);
                    break;
            }
        }
        return false;
    }

    private Value Emit(ExpressionSyntax expression, ValueScope scope) => expression switch
    {
        NameExpressionSyntax name => scope.Get(name.Name).Value with { Owned = false },
        LiteralExpressionSyntax literal => EmitLiteral(literal),
        BinaryExpressionSyntax binary => EmitBinary(binary, scope),
        CallExpressionSyntax call => EmitCall(call, scope),
        _ => throw new InvalidOperationException(),
    };

    private Value EmitLiteral(LiteralExpressionSyntax literal)
    {
        var type = _expressionTypes[literal];
        var register = AllocateRegister(literal.Token);
        var value = literal.Negative ? unchecked(0UL - literal.Value) : literal.Value;
        Add(new(type == SourceType.VectorI32 ? Opcode.SplatI32 : Opcode.SplatI64, Destination: register, Immediate: value), literal.Token);
        return new(type, register, true);
    }

    private Value EmitBinary(BinaryExpressionSyntax binary, ValueScope scope)
    {
        var left = Emit(binary.Left, scope);
        var right = Emit(binary.Right, scope);
        var type = _expressionTypes[binary];
        if (IsMask(type))
        {
            var mask = AllocateMask(binary.Token);
            Add(new(ComparisonOpcode(binary.Operator.Kind, left.Type), SourceA: left.Index, SourceB: right.Index, Mask: mask), binary.Token);
            Release(left);
            Release(right);
            return new(type, mask, true);
        }
        var destination = left.Owned ? left.Index : right.Owned ? right.Index : AllocateRegister(binary.Token);
        Add(new(VectorOpcode(binary.Operator.Kind, type), Destination: destination, SourceA: left.Index, SourceB: right.Index), binary.Token);
        if (left.Owned && left.Index != destination) Release(left);
        if (right.Owned && right.Index != destination) Release(right);
        return new(type, destination, true);
    }

    private Value EmitCall(CallExpressionSyntax call, ValueScope scope)
    {
        if (call.Name == "select") return EmitSelect(call, scope);
        if (call.Name is "load_i32" or "load_i64")
        {
            var address = Emit(call.Arguments[0], scope);
            var destination = AllocateRegister(call.Token);
            Add(new(Opcode.Load, Destination: destination, SourceA: address.Index), call.Token);
            Release(address);
            return new(_expressionTypes[call], destination, true);
        }
        if (call.Name is "gather_i32" or "gather_i64") return EmitGather(call, scope);
        if (call.Name.StartsWith("mask_all_", StringComparison.Ordinal)) return EmitMaskConstant(call, all: true);
        if (call.Name.StartsWith("mask_none_", StringComparison.Ordinal)) return EmitMaskConstant(call, all: false);
        if (call.Name == "mask_not") return EmitMaskNot(call, scope);
        if (call.Name is "mask_and" or "mask_or" or "mask_xor") return EmitMaskBinary(call, scope);

        var function = _functions[call.Name];
        if (!_activeCalls.Add(function.Name))
            throw Error("CAS125", $"Recursive call to '{function.Name}' is not allowed.", call.Token);
        var arguments = call.Arguments.Select(argument => Emit(argument, scope)).ToArray();
        var functionScope = new ValueScope(null);
        for (var index = 0; index < arguments.Length; index++)
            functionScope.Define(function.Parameters[index].Name, new(arguments[index] with { Owned = false }, Mutable: false, OwnsStorage: false));
        var returned = EmitStatements(function.Statements, functionScope);
        var copy = Copy(returned, call.Token);
        Release(returned);
        Dispose(functionScope);
        foreach (var argument in arguments) Release(argument);
        _activeCalls.Remove(function.Name);
        return copy;
    }

    private Value EmitGather(CallExpressionSyntax call, ValueScope scope)
    {
        var isI32 = call.Name == "gather_i32";
        var low = Emit(call.Arguments[0], scope);
        if (!isI32)
        {
            var mask64 = Emit(call.Arguments[1], scope);
            var destination64 = AllocateRegister(call.Token);
            Add(new(Opcode.Zero, Destination: destination64), call.Token);
            Add(new(Opcode.GatherI64, Destination: destination64, SourceA: low.Index, Mask: mask64.Index), call.Token);
            Release(low);
            Release(mask64);
            return new(SourceType.VectorI64, destination64, true);
        }

        var high = Emit(call.Arguments[1], scope);
        var mask = Emit(call.Arguments[2], scope);
        var pair = PrepareAddressPair(low, high, call.Token);
        var destination = AllocateRegister(call.Token);
        Add(new(Opcode.Zero, Destination: destination), call.Token);
        Add(new(Opcode.GatherI32, Destination: destination, SourceA: pair.Index, Mask: mask.Index), call.Token);
        Release(low);
        Release(high);
        Release(mask);
        Release(pair);
        return new(SourceType.VectorI32, destination, true);
    }

    private void EmitScatter(ScatterStatementSyntax scatter, ValueScope scope)
    {
        var isI32 = scatter.Name == "scatter_i32";
        var low = Emit(scatter.Arguments[0], scope);
        if (!isI32)
        {
            var value64 = Emit(scatter.Arguments[1], scope);
            var mask64 = Emit(scatter.Arguments[2], scope);
            Add(new(Opcode.ScatterI64, SourceA: low.Index, SourceB: value64.Index, Mask: mask64.Index), scatter.Token);
            Release(low);
            Release(value64);
            Release(mask64);
            return;
        }

        var high = Emit(scatter.Arguments[1], scope);
        var value = Emit(scatter.Arguments[2], scope);
        var mask = Emit(scatter.Arguments[3], scope);
        var pair = PrepareAddressPair(low, high, scatter.Token);
        Add(new(Opcode.ScatterI32, SourceA: pair.Index, SourceB: value.Index, Mask: mask.Index), scatter.Token);
        Release(low);
        Release(high);
        Release(value);
        Release(mask);
        Release(pair);
    }

    private Value EmitMaskConstant(CallExpressionSyntax call, bool all)
    {
        var type = _expressionTypes[call];
        var vectorType = VectorType(type);
        var zero = AllocateRegister(call.Token);
        Add(new(Opcode.Zero, Destination: zero), call.Token);
        var mask = AllocateMask(call.Token);
        Add(new(all ? ComparisonOpcode(TokenKind.EqualEqual, vectorType) : ComparisonOpcode(TokenKind.Less, vectorType),
            SourceA: zero, SourceB: zero, Mask: mask), call.Token);
        Release(new Value(vectorType, zero, true));
        return new(type, mask, true);
    }

    private Value EmitMaskNot(CallExpressionSyntax call, ValueScope scope)
    {
        var mask = Emit(call.Arguments[0], scope);
        var vector = MaterializeMask(mask, call.Token);
        var zero = AllocateRegister(call.Token);
        Add(new(Opcode.Zero, Destination: zero), call.Token);
        var result = AllocateMask(call.Token);
        Add(new(ComparisonOpcode(TokenKind.EqualEqual, vector.Type), SourceA: vector.Index, SourceB: zero, Mask: result), call.Token);
        Release(vector);
        Release(new Value(vector.Type, zero, true));
        return new(mask.Type, result, true);
    }

    private Value EmitMaskBinary(CallExpressionSyntax call, ValueScope scope)
    {
        var leftMask = Emit(call.Arguments[0], scope);
        var rightMask = Emit(call.Arguments[1], scope);
        var left = MaterializeMask(leftMask, call.Token);
        var right = MaterializeMask(rightMask, call.Token);
        var opcode = call.Name switch
        {
            "mask_and" => Opcode.And,
            "mask_or" => Opcode.Or,
            "mask_xor" => Opcode.Xor,
            _ => throw new InvalidOperationException(),
        };
        Add(new(opcode, Destination: left.Index, SourceA: left.Index, SourceB: right.Index), call.Token);
        Release(right);
        var one = AllocateRegister(call.Token);
        Add(new(left.Type == SourceType.VectorI32 ? Opcode.SplatI32 : Opcode.SplatI64, Destination: one, Immediate: 1), call.Token);
        var result = AllocateMask(call.Token);
        Add(new(ComparisonOpcode(TokenKind.EqualEqual, left.Type), SourceA: left.Index, SourceB: one, Mask: result), call.Token);
        Release(left);
        Release(new Value(left.Type, one, true));
        return new(_expressionTypes[call], result, true);
    }

    private Value MaterializeMask(Value mask, Token token)
    {
        var type = VectorType(mask.Type);
        var one = AllocateRegister(token);
        Add(new(type == SourceType.VectorI32 ? Opcode.SplatI32 : Opcode.SplatI64, Destination: one, Immediate: 1), token);
        var zero = AllocateRegister(token);
        Add(new(Opcode.Zero, Destination: zero), token);
        Add(new(type == SourceType.VectorI32 ? Opcode.SelectI32 : Opcode.SelectI64,
            Destination: one, SourceA: one, SourceB: zero, Mask: mask.Index), token);
        Release(new Value(type, zero, true));
        Release(mask);
        return new(type, one, true);
    }

    private RegisterPair PrepareAddressPair(Value low, Value high, Token token)
    {
        if ((low.Index & 1) == 0 && high.Index == low.Index + 1)
            return new(low.Index, Owned: false);
        var pair = AllocateRegisterPair(token);
        Add(new(Opcode.Move, Destination: pair, SourceA: low.Index), token);
        Add(new(Opcode.Move, Destination: checked((byte)(pair + 1)), SourceA: high.Index), token);
        return new(pair, Owned: true);
    }

    private Value EmitSelect(CallExpressionSyntax call, ValueScope scope)
    {
        var condition = Emit(call.Arguments[0], scope);
        var whenTrue = Emit(call.Arguments[1], scope);
        var whenFalse = Emit(call.Arguments[2], scope);
        var destination = whenTrue.Owned ? whenTrue.Index : whenFalse.Owned ? whenFalse.Index : AllocateRegister(call.Token);
        var type = _expressionTypes[call];
        Add(new(type == SourceType.VectorI32 ? Opcode.SelectI32 : Opcode.SelectI64,
            Destination: destination, SourceA: whenTrue.Index, SourceB: whenFalse.Index, Mask: condition.Index), call.Token);
        Release(condition);
        if (whenTrue.Owned && whenTrue.Index != destination) Release(whenTrue);
        if (whenFalse.Owned && whenFalse.Index != destination) Release(whenFalse);
        return new(type, destination, true);
    }

    private Value Copy(Value source, Token token)
    {
        var destination = AllocateRegister(token);
        Add(new(Opcode.Move, Destination: destination, SourceA: source.Index), token);
        return new(source.Type, destination, true);
    }

    private void Dispose(ValueScope scope)
    {
        foreach (var binding in scope.Bindings)
            if (binding.OwnsStorage) Release(binding.Value with { Owned = true });
    }

    private void Add(Instruction instruction, Token token)
    {
        if (_instructions.Count >= MaximumInstructions)
            throw Error("CAS202", $"Compiled kernels may contain at most {MaximumInstructions} instructions.", token);
        _instructions.Add(instruction);
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

    private byte AllocateRegisterPair(Token token)
    {
        for (var index = 0; index <= 30; index += 2)
        {
            if (_registers[index] || _registers[index + 1]) continue;
            _registers[index] = true;
            _registers[index + 1] = true;
            return (byte)index;
        }
        throw Error("CAS203", "i32 gather/scatter requires a free even-aligned register pair.", token);
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

    private void Release(RegisterPair pair)
    {
        if (!pair.Owned) return;
        _registers[pair.Index] = false;
        _registers[pair.Index + 1] = false;
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
    private static bool IsBuiltin(string name) => name is
        "select" or "load_i32" or "load_i64" or "store" or
        "gather_i32" or "gather_i64" or "scatter_i32" or "scatter_i64" or
        "mask_all_i32" or "mask_none_i32" or "mask_all_i64" or "mask_none_i64" or
        "mask_not" or "mask_and" or "mask_or" or "mask_xor";
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

    private static void DefineType(TypeScope scope, string name, TypeBinding binding, Token token)
    {
        if (scope.TryGet(name, out _)) throw Error("CAS101", $"'{name}' is already declared.", token);
        scope.Define(name, binding);
    }

    private static CompilationFailure Error(string code, string message, Token token) => new(new(code, message, token.Line, token.Column));
    private readonly record struct Value(SourceType Type, byte Index, bool Owned);
    private readonly record struct RegisterPair(byte Index, bool Owned);
    private readonly record struct TypeBinding(SourceType Type, bool Mutable);
    private readonly record struct ValueBinding(Value Value, bool Mutable, bool OwnsStorage);

    private sealed class TypeScope(TypeScope? parent)
    {
        private readonly Dictionary<string, TypeBinding> _bindings = new(StringComparer.Ordinal);

        public void Define(string name, TypeBinding binding) => _bindings.Add(name, binding);

        public bool TryGet(string name, out TypeBinding binding)
        {
            if (_bindings.TryGetValue(name, out binding)) return true;
            return parent is not null && parent.TryGet(name, out binding);
        }
    }

    private sealed class ValueScope(ValueScope? parent)
    {
        private readonly Dictionary<string, ValueBinding> _bindings = new(StringComparer.Ordinal);
        public IEnumerable<ValueBinding> Bindings => _bindings.Values;

        public void Define(string name, ValueBinding binding) => _bindings.Add(name, binding);

        public ValueBinding Get(string name) => _bindings.TryGetValue(name, out var binding)
            ? binding
            : parent?.Get(name) ?? throw new InvalidOperationException($"Missing lowered symbol '{name}'.");
    }
}

internal sealed class CompilationFailure(CascadeDiagnostic diagnostic) : Exception(diagnostic.Message)
{
    public CascadeDiagnostic Diagnostic { get; } = diagnostic;
}
