namespace Cascade.Compiler;

// Builds a typed, value-numbered expression graph, simplifies it, then writes
// the optimized graph back to syntax for the CVM1 lowering pass.
internal sealed class TypedIrOptimizer(ProgramSyntax program, IReadOnlyDictionary<ExpressionSyntax, SourceType> types)
{
    private int _nextValue;
    private readonly Dictionary<(SourceType Type, bool All), IrValue> _maskConstants = [];
    private readonly Dictionary<ValueKey, IrValue> _interned = [];

    public ProgramSyntax Optimize()
    {
        var functions = program.Functions.Select(function =>
            function with { Statements = OptimizeRoutine(function.Parameters, function.Statements) }).ToArray();
        var kernel = program.Kernel with { Statements = OptimizeRoutine(program.Kernel.Parameters, program.Kernel.Statements) };
        return new(functions, kernel);
    }

    private IReadOnlyList<StatementSyntax> OptimizeRoutine(
        IReadOnlyList<ParameterSyntax> parameters,
        IReadOnlyList<StatementSyntax> statements)
    {
        _maskConstants.Clear();
        _interned.Clear();
        var values = new ValueEnvironment();
        foreach (var parameter in parameters)
            values.Add(parameter.Name, NewValue(parameter.Type, new NameExpressionSyntax(parameter.Name, parameter.Token)));
        return EliminateDeadBindings(OptimizeStatements(statements, values));
    }

    private IReadOnlyList<StatementSyntax> OptimizeStatements(
        IReadOnlyList<StatementSyntax> statements,
        ValueEnvironment values)
    {
        var optimized = new List<StatementSyntax>(statements.Count);
        foreach (var statement in statements)
        {
            switch (statement)
            {
                case LetStatementSyntax let:
                    {
                        var expression = OptimizeExpression(let.Expression, values);
                        optimized.Add(let with { Expression = expression.Syntax });
                        values.Add(let.Name, expression.Value with
                        {
                            Representative = new NameExpressionSyntax(let.Name, let.Token),
                        });
                        values.Available.TryAdd(expression.Value.Id, new NameExpressionSyntax(let.Name, let.Token));
                        break;
                    }
                case VarStatementSyntax variable:
                    {
                        var expression = OptimizeExpression(variable.Expression, values);
                        optimized.Add(variable with { Expression = expression.Syntax });
                        values.Add(variable.Name, NewValue(types[variable.Expression],
                            new NameExpressionSyntax(variable.Name, variable.Token)));
                        values.Mutable.Add(variable.Name);
                        break;
                    }
                case AssignmentStatementSyntax assignment:
                    {
                        var expression = OptimizeExpression(assignment.Expression, values);
                        optimized.Add(assignment with { Expression = expression.Syntax });
                        values[assignment.Name] = NewValue(types[assignment.Expression],
                            new NameExpressionSyntax(assignment.Name, assignment.Token));
                        break;
                    }
                case RepeatStatementSyntax repeat:
                    {
                        var childValues = new ValueEnvironment(values);
                        // A loop-carried variable has a different value on later
                        // iterations. Never reuse a pre-loop expression for it.
                        ForgetMutableValues(childValues);
                        optimized.Add(repeat with { Statements = OptimizeStatements(repeat.Statements, childValues) });
                        ForgetMutableValues(values);
                        break;
                    }
                case StoreStatementSyntax store:
                    optimized.Add(store with
                    {
                        Address = OptimizeExpression(store.Address, values).Syntax,
                        Value = OptimizeExpression(store.Value, values).Syntax,
                    });
                    break;
                case ScatterStatementSyntax scatter:
                    optimized.Add(scatter with
                    {
                        Arguments = scatter.Arguments.Select(argument => OptimizeExpression(argument, values).Syntax).ToArray(),
                    });
                    break;
                case ReturnStatementSyntax @return:
                    optimized.Add(@return with { Expression = OptimizeExpression(@return.Expression, values).Syntax });
                    break;
            }
        }
        return optimized;
    }

    private void ForgetMutableValues(ValueEnvironment values)
    {
        foreach (var name in values.Mutable)
        {
            var old = values[name];
            values[name] = NewValue(old.Type, old.Representative);
        }
    }

    private OptimizedExpression OptimizeExpression(ExpressionSyntax expression, ValueEnvironment values)
    {
        var result = OptimizeExpressionCore(expression, values);
        return result.Pure && values.Available.TryGetValue(result.Value.Id, out var saved)
            ? result with { Syntax = saved } : result;
    }

    private OptimizedExpression OptimizeExpressionCore(ExpressionSyntax expression, ValueEnvironment values)
    {
        var type = types[expression];
        switch (expression)
        {
            case NameExpressionSyntax name:
                return new(name, values[name.Name], Pure: true);
            case LiteralExpressionSyntax literal:
                var typedLiteral = literal with { ResolvedType = type };
                var bits = literal.Negative ? unchecked(0UL - literal.Value) : literal.Value;
                if (type == SourceType.VectorI32) bits &= uint.MaxValue;
                return new(typedLiteral, Intern(new(type, "literal", Literal: bits), typedLiteral), Pure: true);
            case BinaryExpressionSyntax binary:
                {
                    var left = OptimizeExpression(binary.Left, values);
                    var right = OptimizeExpression(binary.Right, values);
                    var syntax = binary with { Left = left.Syntax, Right = right.Syntax };
                    var pure = left.Pure && right.Pure;
                    var value = pure ? Intern(new(type, binary.Operator.Kind.ToString(), left.Value.Id, right.Value.Id), syntax)
                        : NewValue(type, syntax);
                    return new(syntax, value, pure);
                }
            case CallExpressionSyntax call:
                return OptimizeCall(call, type, values);
            default:
                throw new InvalidOperationException();
        }
    }

    private OptimizedExpression OptimizeCall(
        CallExpressionSyntax call,
        SourceType type,
        ValueEnvironment values)
    {
        var arguments = call.Arguments.Select(argument => OptimizeExpression(argument, values)).ToArray();
        var syntax = call with { Arguments = arguments.Select(argument => argument.Syntax).ToArray() };
        var pure = IsPureCall(call.Name) && arguments.All(argument => argument.Pure);

        if (call.Name is "mask_all_i32" or "mask_all_i64")
            return ConstantMask(type, all: true, call.Token);
        if (call.Name is "mask_none_i32" or "mask_none_i64")
            return ConstantMask(type, all: false, call.Token);

        if (call.Name == "mask_not")
        {
            var operand = arguments[0];
            if (IsConstant(operand.Value, all: true)) return ConstantMask(type, all: false, call.Token);
            if (IsConstant(operand.Value, all: false)) return ConstantMask(type, all: true, call.Token);
            if (TryRestoreComplement(operand, out var restored)) return restored;
            return new(syntax, Complement(operand.Value, syntax), pure);
        }

        if (call.Name is "mask_and" or "mask_or" or "mask_xor")
            return OptimizeMaskBinary(call, type, arguments, syntax, pure);

        if (call.Name == "select")
        {
            if (arguments[0].Pure && IsConstant(arguments[0].Value, all: true) && arguments[2].Pure) return arguments[1];
            if (arguments[0].Pure && IsConstant(arguments[0].Value, all: false) && arguments[1].Pure) return arguments[2];
        }

        return new(syntax, pure && call.Name == "select"
            ? Intern(new(type, call.Name, arguments[0].Value.Id, arguments[1].Value.Id, arguments[2].Value.Id), syntax)
            : NewValue(type, syntax), pure);
    }

    private OptimizedExpression OptimizeMaskBinary(
        CallExpressionSyntax call,
        SourceType type,
        IReadOnlyList<OptimizedExpression> arguments,
        CallExpressionSyntax syntax,
        bool pure)
    {
        var left = arguments[0];
        var right = arguments[1];
        if (left.Pure && right.Pure && left.Value.Id == right.Value.Id)
            return call.Name == "mask_xor" ? ConstantMask(type, all: false, call.Token) : left;

        var complements = left.Value.ComplementOf?.Id == right.Value.Id || right.Value.ComplementOf?.Id == left.Value.Id;
        if (left.Pure && right.Pure && complements)
            return ConstantMask(type, all: call.Name != "mask_and", call.Token);

        if (call.Name == "mask_and")
        {
            if (IsConstant(left.Value, all: false) && right.Pure) return left;
            if (IsConstant(right.Value, all: false) && left.Pure) return right;
            if (IsConstant(left.Value, all: true)) return right;
            if (IsConstant(right.Value, all: true)) return left;
        }
        else if (call.Name == "mask_or")
        {
            if (IsConstant(left.Value, all: true) && right.Pure) return left;
            if (IsConstant(right.Value, all: true) && left.Pure) return right;
            if (IsConstant(left.Value, all: false)) return right;
            if (IsConstant(right.Value, all: false)) return left;
        }
        else
        {
            if (IsConstant(left.Value, all: false)) return right;
            if (IsConstant(right.Value, all: false)) return left;
            if (IsConstant(left.Value, all: true)) return ComplementExpression(right, syntax, pure);
            if (IsConstant(right.Value, all: true)) return ComplementExpression(left, syntax, pure);
        }

        return new(syntax, pure ? Intern(new(type, call.Name, left.Value.Id, right.Value.Id), syntax) : NewValue(type, syntax), pure);
    }

    private OptimizedExpression ComplementExpression(OptimizedExpression operand, CallExpressionSyntax source, bool pure)
    {
        var syntax = new CallExpressionSyntax("mask_not", [operand.Syntax], source.Token);
        return TryRestoreComplement(operand, out var restored)
            ? restored : new(syntax, Complement(operand.Value, syntax), pure);
    }

    private static bool TryRestoreComplement(OptimizedExpression operand, out OptimizedExpression restored)
    {
        restored = default;
        if (operand.Value.ComplementOf is not { } original) return false;
        // A previously evaluated expression is not a recipe to run again. Only
        // reuse a saved immutable mask, or remove syntactically adjacent NOTs.
        if (original.Representative is NameExpressionSyntax name)
        {
            restored = new(name, original, Pure: true);
            return true;
        }
        if (operand.Syntax is CallExpressionSyntax { Name: "mask_not" } call)
        {
            restored = new(call.Arguments[0], original, operand.Pure);
            return true;
        }
        return false;
    }

    private IrValue Complement(IrValue operand, ExpressionSyntax representative)
        => NewValue(operand.Type, representative, operand);

    private OptimizedExpression ConstantMask(SourceType type, bool all, Token token)
    {
        if (!_maskConstants.TryGetValue((type, all), out var value))
        {
            var constructorName = $"mask_{(all ? "all" : "none")}_{(type == SourceType.MaskI32 ? "i32" : "i64")}";
            var syntax = new CallExpressionSyntax(constructorName, [], token);
            value = NewValue(type, syntax);
            _maskConstants.Add((type, all), value);
        }
        var name = $"mask_{(all ? "all" : "none")}_{(type == SourceType.MaskI32 ? "i32" : "i64")}";
        return new(new CallExpressionSyntax(name, [], token), value, Pure: true);
    }

    private bool IsConstant(IrValue value, bool all) =>
        _maskConstants.TryGetValue((value.Type, all), out var constant) && constant.Id == value.Id;

    private IrValue NewValue(SourceType type, ExpressionSyntax? representative = null, IrValue? complementOf = null) =>
        new(++_nextValue, type, representative, complementOf);

    private IrValue Intern(ValueKey key, ExpressionSyntax syntax)
    {
        if (_interned.TryGetValue(key, out var existing)) return existing;
        var value = NewValue(key.Type, syntax);
        _interned.Add(key, value);
        return value;
    }

    private static bool IsPureCall(string name) => name is
        "select" or "mask_not" or "mask_and" or "mask_or" or "mask_xor" ||
        name is "mask_all_i32" or "mask_all_i64" or "mask_none_i32" or "mask_none_i64";


    private static IReadOnlyList<StatementSyntax> EliminateDeadBindings(IReadOnlyList<StatementSyntax> statements)
    {
        var live = new HashSet<string>(StringComparer.Ordinal);
        var kept = new List<StatementSyntax>(statements.Count);
        for (var index = statements.Count - 1; index >= 0; index--)
        {
            var statement = statements[index];
            if (statement is LetStatementSyntax let && !live.Remove(let.Name) && IsPure(let.Expression))
                continue;

            if (statement is RepeatStatementSyntax repeat)
                statement = repeat with { Statements = EliminateDeadBindings(repeat.Statements) };

            foreach (var name in ReadNames(statement)) live.Add(name);
            kept.Add(statement);
        }
        kept.Reverse();
        return kept;
    }

    private static IEnumerable<string> ReadNames(StatementSyntax statement)
    {
        var expressions = statement switch
        {
            LetStatementSyntax let => new[] { let.Expression },
            VarStatementSyntax variable => new[] { variable.Expression },
            AssignmentStatementSyntax assignment => new[] { assignment.Expression },
            StoreStatementSyntax store => new[] { store.Address, store.Value },
            ScatterStatementSyntax scatter => scatter.Arguments,
            ReturnStatementSyntax @return => new[] { @return.Expression },
            _ => [],
        };
        foreach (var expression in expressions)
            foreach (var name in ReadNames(expression)) yield return name;
        if (statement is RepeatStatementSyntax repeat)
            foreach (var nested in repeat.Statements)
                foreach (var name in ReadNames(nested)) yield return name;
    }

    private static IEnumerable<string> ReadNames(ExpressionSyntax expression)
    {
        if (expression is NameExpressionSyntax name) yield return name.Name;
        else if (expression is BinaryExpressionSyntax binary)
        {
            foreach (var nested in ReadNames(binary.Left)) yield return nested;
            foreach (var nested in ReadNames(binary.Right)) yield return nested;
        }
        else if (expression is CallExpressionSyntax call)
            foreach (var argument in call.Arguments)
                foreach (var nested in ReadNames(argument)) yield return nested;
    }

    private static bool IsPure(ExpressionSyntax expression) => expression switch
    {
        NameExpressionSyntax or LiteralExpressionSyntax => true,
        BinaryExpressionSyntax binary => IsPure(binary.Left) && IsPure(binary.Right),
        CallExpressionSyntax call when IsPureCall(call.Name) => call.Arguments.All(IsPure),
        _ => false,
    };

    private sealed record IrValue(int Id, SourceType Type, ExpressionSyntax? Representative, IrValue? ComplementOf);
    private readonly record struct ValueKey(SourceType Type, string Operation, int A = 0, int B = 0, int C = 0, ulong Literal = 0);
    private readonly record struct OptimizedExpression(ExpressionSyntax Syntax, IrValue Value, bool Pure);

    private sealed class ValueEnvironment : Dictionary<string, IrValue>
    {
        public Dictionary<int, NameExpressionSyntax> Available { get; }
        public HashSet<string> Mutable { get; }

        public ValueEnvironment() : base(StringComparer.Ordinal)
        {
            Available = [];
            Mutable = new(StringComparer.Ordinal);
        }

        public ValueEnvironment(ValueEnvironment parent) : base(parent, StringComparer.Ordinal)
        {
            Available = new(parent.Available);
            Mutable = new(parent.Mutable, StringComparer.Ordinal);
        }
    }
}
