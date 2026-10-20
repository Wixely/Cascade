# Wixely.Cascade.Core

`Wixely.Cascade.Core` contains the hardware-independent architectural model for
Cascade v0.1: opcodes, decoded instructions, immutable modules, vector
constants, function ranges, import declarations, and 512-bit register storage.

Most applications should install `Wixely.Cascade.Runtime`, which depends on this
package and provides validation, `CVM1` serialization, the reference
interpreter, and the supported embedding lifecycle.

The package targets .NET 10 and is licensed under MIT.
