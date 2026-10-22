namespace Cascade.Compiler;

/// <summary>Counts reads once per lexical block, including statically repeated free-variable reads.</summary>
internal sealed class SourceLiveness
{
    private readonly Dictionary<IReadOnlyList<StatementSyntax>, Dictionary<string, int>> _blocks = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<StatementSyntax, int> _bindings = new(ReferenceEqualityComparer.Instance);

    public int UsesOf(StatementSyntax binding) => _bindings[binding];
    public int UsesOf(string name, IReadOnlyList<StatementSyntax> statements) => Analyze(statements).GetValueOrDefault(name);

    public Dictionary<string, int> Analyze(IReadOnlyList<StatementSyntax> statements)
    {
        if (_blocks.TryGetValue(statements, out var cached)) return cached;
        var live = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var index = statements.Count - 1; index >= 0; index--)
        {
            var statement = statements[index];
            switch (statement)
            {
                case LetStatementSyntax let:
                    _bindings[let] = live.GetValueOrDefault(let.Name);
                    live.Remove(let.Name);
                    Read(let.Expression, live);
                    break;
                case VarStatementSyntax variable:
                    _bindings[variable] = live.GetValueOrDefault(variable.Name);
                    live.Remove(variable.Name);
                    Read(variable.Expression, live);
                    break;
                case AssignmentStatementSyntax assignment:
                    Read(assignment.Expression, live);
                    break;
                case RepeatStatementSyntax repeat:
                    foreach (var (name, uses) in Analyze(repeat.Statements))
                        Add(live, name, (long)uses * repeat.Count);
                    break;
                case StoreStatementSyntax store:
                    Read(store.Address, live);
                    Read(store.Value, live);
                    break;
                case ScatterStatementSyntax scatter:
                    foreach (var argument in scatter.Arguments) Read(argument, live);
                    break;
                case ReturnStatementSyntax @return:
                    Read(@return.Expression, live);
                    break;
            }
        }
        _blocks.Add(statements, live);
        return live;
    }

    private static void Read(ExpressionSyntax expression, Dictionary<string, int> live)
    {
        switch (expression)
        {
            case NameExpressionSyntax name:
                Add(live, name.Name, 1);
                break;
            case BinaryExpressionSyntax binary:
                Read(binary.Left, live);
                Read(binary.Right, live);
                break;
            case CallExpressionSyntax call:
                foreach (var argument in call.Arguments) Read(argument, live);
                break;
        }
    }

    private static void Add(Dictionary<string, int> live, string name, long count) =>
        live[name] = (int)Math.Min(int.MaxValue, live.GetValueOrDefault(name) + count);
}
