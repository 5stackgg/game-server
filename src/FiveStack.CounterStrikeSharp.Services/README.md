# FiveStack Counter-Strike Sharp Services

This directory contains all the Counter-Strike Sharp service abstractions that were previously in `FiveStack.Services`.

## Purpose

The services in this namespace provide a clean abstraction layer over Counter-Strike Sharp APIs, making it easier to:
1. Swap out different implementations if needed
2. Test code without requiring Counter-Strike Sharp runtime
3. Maintain cleaner separation of concerns

## How to Use

To use these services in your code, simply add the following using statement:

```csharp
using FiveStack.CounterStrikeSharp.Services;
```

Then you can reference the interfaces directly (e.g., `IPlayerService`, `ICommandService`) or use dependency injection.

## Directory Structure

- **Interfaces**: `I<Name>Service.cs` files define the service contracts
- **Implementations**: `<Name>Service.cs` files implement the interfaces