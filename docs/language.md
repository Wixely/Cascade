# Cascade kernel language

- Language version: 0.2
- File extension: `.cascade`
- Compiler command: `cascade compile <input.cascade> [-o <output.cvm>]`

Cascade kernel source is a small, statically typed language that compiles
deterministically to portable `CVM1` bytecode. It expresses the data-parallel
part of an application; the host loads inputs, invokes the kernel, and consumes
its output.

## Example

```c
kernel choose(
    vector<i32> value,
    vector<i32> increment
) -> vector<i32> {
    let adjusted = value + increment * 2;
    return select(adjusted < 10, adjusted, 10);
}
```

The complete Game of Life kernel is
[`life-rule.cascade`](../examples/Cascade.GameOfLife/life-rule.cascade).

## Types and execution model

`vector<i32>` contains 16 32-bit lanes and `vector<i64>` contains eight 64-bit
lanes. Arithmetic is lane-wise and wraps modulo the lane width. The types
describe lane width; comparison operators define when signed interpretation
matters.

Comparisons produce internal `mask<i32>` or `mask<i64>` values. A mask can be
passed to `select(mask, whenTrue, whenFalse)`, which chooses independently for
each lane. Mask types are inferred and cannot currently appear in signatures.

Integer literals are splatted across all lanes and receive their vector type
from context. Negative i32 literals may be as low as `-2147483648`; positive
i32 literals may contain any 32-bit bit pattern through `4294967295`.

## Operators

From lowest to highest precedence:

| Operators | Result | Semantics |
| --- | --- | --- |
| `\|` | vector | lane-wise bitwise OR |
| `^` | vector | lane-wise bitwise XOR |
| `&` | vector | lane-wise bitwise AND |
| `==`, `<` | mask | equality or signed less-than comparison |
| `+`, `-` | vector | wrapping addition or subtraction |
| `*` | vector | wrapping i32 multiplication |

Parentheses override precedence. `select` requires a mask and two vectors with
the same lane type. CVM1 has no i64 multiplication instruction, so the compiler
rejects it.

## Kernel ABI

A source file contains one kernel and may declare helper functions before it.
Kernel parameters are assigned to vector
registers in declaration order, beginning with `v0`. Version 0.2 permits up to
16 parameters so registers remain available for expressions. The returned
vector is placed in `v0`, and compilation ends the module with `halt`.

`let` creates an immutable, inferred binding. Bindings are evaluated in source
order; names must be unique and declared before use. Every kernel must end with
one `return`, and statements after it are rejected.

## Functions

Typed helper functions use `fn`, the same parameter and return types as kernels,
and lexical local bindings:

```c
fn add_amount(vector<i32> value, vector<i32> amount) -> vector<i32> {
    return value + amount;
}
```

The 0.2 compiler deterministically inlines helper calls. Recursion, including
indirect recursion, is rejected. Inlining keeps CVM1 output independent of a
source-level calling ABI and lets the compiler type-check every argument.

## Bounded control flow and mutation

`var` declares a mutable vector. Assignment is permitted only to `var`; kernel
parameters and `let` bindings remain immutable. `repeat N` executes its block a
compile-time constant number of times:

```c
var result = input;
repeat 4 {
    result = add_amount(result, 3);
}
```

`N` must be between 1 and 1024. The compiler unrolls the block, so source code
cannot introduce an unbounded loop and the VM's ordinary fuel accounting remains
authoritative. Compiled modules are limited to 1,000,000 instructions.

## Typed memory

`load_i32(address)` and `load_i64(address)` load one 64-byte architectural
vector and assign its source-level lane type. `store(address, value)` stores one
64-byte vector. Addresses must be `vector<i64>`; as with the CVM1 `load` and
`store` instructions, lane 0 supplies the unsigned byte address. Runtime bounds
checking and trap-before-effect behavior are unchanged.

## Grammar

```text
program     := function* kernel
function    := "fn" signature block
kernel      := "kernel" signature block
signature   := identifier "(" parameters? ")" "->" vector-type
block       := "{" statement* "}"
parameters  := parameter ("," parameter)*
parameter   := vector-type identifier
vector-type := "vector" "<" ("i32" | "i64") ">"
statement   := "let" identifier "=" expression ";"
             | "var" identifier "=" expression ";"
             | identifier "=" expression ";"
             | "repeat" integer block
             | "store" "(" expression "," expression ")" ";"
             | "return" expression ";"
expression  := identifier | integer | "-" integer
             | "(" expression ")"
             | expression binary-operator expression
             | identifier "(" arguments? ")"
arguments   := expression ("," expression)*
```

Whitespace is insignificant. `//` begins a comment through the end of the
line. Diagnostics include a stable code plus one-based line and column.

## Current boundaries

Language version 0.2 intentionally has no data-dependent branches, dynamic
loops, gather/scatter syntax, imports, casts, or mask Boolean operators. These
remain available at the bytecode level and can be added with corresponding type
and safety rules. The compiler performs deterministic temporary-register
allocation but does not yet optimize or spill.
