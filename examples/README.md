# Cascade demos

`Cascade.Demos` is a dependency-free console tour of the public embedding API.
Every demo builds a module, validates and instantiates it through
`CascadeProgram`, executes it, checks the final VM state, and prints the result.

```powershell
dotnet run --project examples\Cascade.Demos
dotnet run --project examples\Cascade.Demos -- list
dotnet run --project examples\Cascade.Demos -- arithmetic masks memory capability
```

| Demo | What it shows |
| --- | --- |
| `arithmetic` | Sixteen `i32` lanes, multiplication, and defined wrapping overflow |
| `masks` | Signed comparison, a 16-bit predicate mask, and lane-wise selection |
| `memory` | Eight noncontiguous `i64` scatter/gather addresses in fixed linear memory |
| `capability` | An explicitly declared, fuel-charged host import with no ambient authority |

Running without arguments, or with `all`, executes all demos. A failed result
or unexpected VM state returns a nonzero exit code, so the same command is a
useful smoke test.
