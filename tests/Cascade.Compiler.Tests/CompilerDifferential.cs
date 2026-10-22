using Cascade.Compiler;
using Cascade.Runtime;
using System.Buffers.Binary;

internal static class CompilerDifferential
{
    private const int Seed = 0xCA5CADE;

    public static void Run()
    {
        var random = new Random(Seed);
        var cases = 0;
        foreach (var bits in new[] { 32, 64 })
        {
            for (var index = 0; index < 256; index++)
            {
                var vector = Vector(random, bits, 3);
                var predicate = Mask(random, bits, 2);
                var source = $$"""
                    kernel generated(vector<i{{bits}}> input, vector<i{{bits}}> other) -> vector<i{{bits}}> {
                        let x = input + 3;
                        let y = other ^ 17;
                        let alias = x;
                        let selected = {{vector.Text}};
                        let duplicate = {{vector.Text}};
                        let predicate = {{predicate.Text}};
                        let inverse = mask_not(predicate);
                        let restored = mask_not(inverse);
                        return select(restored, selected + duplicate + alias, y);
                    }
                    """;
                Check(source, bits, (a, b) =>
                {
                    var x = Wrap(a + 3, bits);
                    var y = b ^ 17;
                    return predicate.Evaluate(x, y) ? Wrap(unchecked(vector.Evaluate(x, y) * 2 + x), bits) : y;
                }, cases++);
            }
            for (var index = 0; index < 64; index++)
            {
                var amount = random.Next(1, 32);
                var count = random.Next(1, 9);
                var source = $$"""
                    fn bump(vector<i{{bits}}> value) -> vector<i{{bits}}> { return value + {{amount}}; }
                    kernel generated(vector<i{{bits}}> input, vector<i{{bits}}> other) -> vector<i{{bits}}> {
                        var current = input;
                        let saved = current;
                        repeat {{count}} {
                            let before = current;
                            current = bump(current);
                            current = current ^ before;
                        }
                        return current + saved + other;
                    }
                    """;
                Check(source, bits, (a, b) =>
                {
                    var current = a;
                    for (var iteration = 0; iteration < count; iteration++)
                        current = Wrap(current + (ulong)amount, bits) ^ current;
                    return Wrap(current + a + b, bits);
                }, cases++);
            }
        }
        CheckMemory();
        Console.WriteLine($"  {cases} generated kernels; seed {Seed}; optimized/unoptimized and available execution backends");
    }

    private static void Check(string source, int bits, Func<ulong, ulong, ulong> expected, int caseIndex)
    {
        ulong[] boundary = [0, 1, 7, 8, 15, 16, 31, 32, 0x7FFFFFFF, 0x80000000, uint.MaxValue, long.MaxValue, 1UL << 63, ulong.MaxValue];
        var backends = SimdInterpreter.IsSupported
            ? new[] { ExecutionBackend.Reference, ExecutionBackend.HardwareSimd }
            : [ExecutionBackend.Reference];
        foreach (var optimize in new[] { false, true })
        {
            var compilation = CascadeCompiler.Compile(source, new CompilationOptions { Optimize = optimize });
            if (!compilation.Succeeded)
                throw Failure($"compile: {string.Join(", ", compilation.Diagnostics)}");
            var program = CascadeProgram.FromModule(compilation.Module!);
            foreach (var backend in backends)
            {
                var instance = program.Instantiate(new VmOptions { MemorySize = 0 }, backend);
                var lanes = 512 / bits;
                for (var lane = 0; lane < lanes; lane++)
                {
                    var a = Wrap(boundary[(lane + caseIndex) % boundary.Length], bits);
                    var b = Wrap(boundary[(lane * 3 + caseIndex + 1) % boundary.Length], bits);
                    if (bits == 32)
                    {
                        instance.State.Register(0).WriteUInt32(lane, (uint)a);
                        instance.State.Register(1).WriteUInt32(lane, (uint)b);
                    }
                    else
                    {
                        instance.State.Register(0).WriteUInt64(lane, a);
                        instance.State.Register(1).WriteUInt64(lane, b);
                    }
                }
                var result = instance.Execute();
                if (!result.Succeeded) throw Failure($"execution: {result.Trap}");
                for (var lane = 0; lane < lanes; lane++)
                {
                    var a = Wrap(boundary[(lane + caseIndex) % boundary.Length], bits);
                    var b = Wrap(boundary[(lane * 3 + caseIndex + 1) % boundary.Length], bits);
                    var actual = bits == 32 ? instance.State.Register(0).ReadUInt32(lane) : instance.State.Register(0).ReadUInt64(lane);
                    var wanted = expected(a, b);
                    if (actual != wanted)
                        throw Failure($"optimize={optimize}, backend={backend}, lane={lane}: expected {wanted}, actual {actual}");
                }
            }
        }
        return;

        Exception Failure(string detail) => new InvalidOperationException($"Seed {Seed}, case {caseIndex}: {detail}\n{source}");
    }

    private static VectorExpression Vector(Random random, int bits, int depth)
    {
        if (depth == 0)
            return random.Next(2) == 0 ? new("x", (x, _) => x) : new("y", (_, y) => y);
        var left = Vector(random, bits, depth - 1);
        var right = Vector(random, bits, depth - 1);
        var operation = random.Next(bits == 32 ? 6 : 5);
        var symbol = new[] { "+", "-", "^", "&", "|", "*" }[operation];
        return new($"({left.Text} {symbol} {right.Text})", (x, y) =>
        {
            var a = left.Evaluate(x, y);
            var b = right.Evaluate(x, y);
            return Wrap(operation switch
            {
                0 => unchecked(a + b),
                1 => unchecked(a - b),
                2 => a ^ b,
                3 => a & b,
                4 => a | b,
                _ => unchecked(a * b),
            }, bits);
        });
    }

    private static void CheckMemory()
    {
        var cases = 0;
        foreach (var bits in new[] { 32, 64 })
            foreach (var useHelper in new[] { false, true })
                foreach (var invalidAddress in new[] { false, true })
                    foreach (var optimize in new[] { false, true })
                        foreach (var backend in SimdInterpreter.IsSupported
                            ? new[] { ExecutionBackend.Reference, ExecutionBackend.HardwareSimd } : [ExecutionBackend.Reference])
                        {
                            var helper = $$"""
                fn replace(vector<i64> address, vector<i{{bits}}> value) -> vector<i{{bits}}> {
                    store(address, value);
                    return value;
                }
                """;
                            var body = useHelper
                                ? $"let selected = select(mask_all_i{bits}(), old, replace(address, fresh)); return selected;"
                                : $"let inverse = mask_not(load_i{bits}(address) < 8); store(address, fresh); return select(mask_not(inverse), fresh, old);";
                            var source = $$"""
                {{helper}}
                kernel memory(vector<i64> address, vector<i{{bits}}> old, vector<i{{bits}}> fresh) -> vector<i{{bits}}> {
                    store(address, old);
                    {{body}}
                }
                """;
                            var compilation = CascadeCompiler.Compile(source, new CompilationOptions { Optimize = optimize });
                            if (!compilation.Succeeded) throw new InvalidOperationException(string.Join(", ", compilation.Diagnostics));
                            var instance = CascadeProgram.FromModule(compilation.Module!).Instantiate(new VmOptions { MemorySize = 64 }, backend);
                            instance.State.Register(0).WriteUInt64(0, invalidAddress ? 1UL : 0UL);
                            var expectedMemory = new byte[64];
                            for (var lane = 0; lane < 512 / bits; lane++)
                            {
                                if (bits == 32)
                                {
                                    instance.State.Register(1).WriteUInt32(lane, (uint)lane);
                                    instance.State.Register(2).WriteUInt32(lane, (uint)(lane + 100));
                                    if (!invalidAddress) BinaryPrimitives.WriteUInt32LittleEndian(expectedMemory.AsSpan(lane * 4), (uint)(lane + 100));
                                }
                                else
                                {
                                    instance.State.Register(1).WriteUInt64(lane, (ulong)(lane * 2));
                                    instance.State.Register(2).WriteUInt64(lane, (ulong)(lane + 100));
                                    if (!invalidAddress) BinaryPrimitives.WriteUInt64LittleEndian(expectedMemory.AsSpan(lane * 8), (ulong)(lane + 100));
                                }
                            }
                            var result = instance.Execute();
                            if (invalidAddress ? result.Trap?.Code != TrapCode.InvalidMemoryAccess : !result.Succeeded)
                                throw new InvalidOperationException($"Memory case {cases}: unexpected result {result}");
                            if (!instance.State.LinearMemory.Span.SequenceEqual(expectedMemory))
                                throw new InvalidOperationException($"Memory case {cases}: effects differ from scalar oracle");
                            if (!invalidAddress)
                            {
                                for (var lane = 0; lane < 512 / bits; lane++)
                                {
                                    var old = (ulong)(bits == 32 ? lane : lane * 2);
                                    var expected = useHelper || old >= 8 ? old : (ulong)(lane + 100);
                                    var actual = bits == 32 ? instance.State.Register(0).ReadUInt32(lane) : instance.State.Register(0).ReadUInt64(lane);
                                    if (actual != expected) throw new InvalidOperationException($"Memory case {cases}: lane {lane}, expected {expected}, actual {actual}");
                                }
                            }
                            cases++;
                        }
        Console.WriteLine($"  {cases} memory/effect/trap configurations matched independent expected state");
    }

    private static MaskExpression Mask(Random random, int bits, int depth)
    {
        if (depth == 0)
        {
            var left = Vector(random, bits, 1);
            var right = Vector(random, bits, 1);
            var equal = random.Next(2) == 0;
            return new($"({left.Text} {(equal ? "==" : "<")} {right.Text})", (x, y) =>
            {
                var a = left.Evaluate(x, y);
                var b = right.Evaluate(x, y);
                return equal ? a == b : bits == 32 ? unchecked((int)a) < unchecked((int)b) : unchecked((long)a) < unchecked((long)b);
            });
        }
        var first = Mask(random, bits, depth - 1);
        var second = Mask(random, bits, depth - 1);
        return random.Next(6) switch
        {
            0 => new($"mask_not({first.Text})", (x, y) => !first.Evaluate(x, y)),
            1 => new($"mask_and({first.Text}, {second.Text})", (x, y) => first.Evaluate(x, y) & second.Evaluate(x, y)),
            2 => new($"mask_or({first.Text}, {second.Text})", (x, y) => first.Evaluate(x, y) | second.Evaluate(x, y)),
            3 => new($"mask_xor({first.Text}, {second.Text})", (x, y) => first.Evaluate(x, y) ^ second.Evaluate(x, y)),
            4 => new($"mask_and(mask_all_i{bits}(), {first.Text})", first.Evaluate),
            _ => new($"mask_or({first.Text}, mask_none_i{bits}())", first.Evaluate),
        };
    }

    private static ulong Wrap(ulong value, int bits) => bits == 32 ? value & uint.MaxValue : value;
    private sealed record VectorExpression(string Text, Func<ulong, ulong, ulong> Evaluate);
    private sealed record MaskExpression(string Text, Func<ulong, ulong, bool> Evaluate);
}
