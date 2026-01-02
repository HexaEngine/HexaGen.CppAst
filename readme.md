# HexaGen.CppAst

[![NuGet](https://img.shields.io/nuget/v/HexaGen.CppAst.svg)](https://www.nuget.org/packages/HexaGen.CppAst/)

<img align="right" width="160px" height="160px" src="https://raw.githubusercontent.com/HexaEngine/HexaGen.CppAst/main/icon.png">

HexaGen.CppAst is a complete rewrite and modernization of CppAst.NET, providing a powerful C/C++ parser for header files with full access to the AST, comments, and macros for .NET 9+.

## What Makes HexaGen.CppAst Different

Originally started as a fork of CppAst.NET, **HexaGen.CppAst is now a complete ground-up rewrite** that addresses fundamental architectural limitations of the original library:

### ?? Full Low-Level API Access
- **Direct CXCursor API Access**: Unlike the original CppAst, HexaGen.CppAst exposes the underlying libclang `CXCursor` API through the `ICppElement.Cursor` property
- **Advanced Scenarios**: Enables sophisticated parsing scenarios that require direct interaction with libclang's cursor system
- **No Hidden Abstractions**: Every AST element provides direct access to its underlying Clang representation for maximum flexibility

### ??? Modern, Extensible Architecture
- **Visitor Pattern System**: Clean, extensible architecture using the visitor pattern (`CursorVisitor<TResult>`)
- **Pluggable Parsers**: Register custom visitors for specific cursor types through `CursorVisitorRegistry`
- **Container-Based Processing**: Hierarchical context system (`CppContainerContext`) for proper scope management
- **Type System Redesign**: Comprehensive type resolution with `TypedefResolver` and improved canonical type handling

### ?? Performance & Quality Improvements
- **Despagettification**: Complete restructuring eliminates callback hell and deeply nested code
- **Efficient Memory Management**: Custom allocators and improved string handling (`CString`, `BumpAllocator`)
- **Better Diagnostics**: Enhanced source location tracking and error reporting
- **AOT-Ready**: Full support for Native AOT compilation with trim and AOT analyzers enabled

### ?? Enhanced Features
- **Comprehensive Comment Parsing**: Full support for all libclang comment types (block, inline, param, etc.)
- **Improved Attribute Handling**: Better support for `__attribute__`, `[[]]`, and `__declspec` attributes
- **Template Support**: Enhanced handling of template classes and specializations
- **Flexible Tokenization**: Advanced tokenizer with support for complex macro expansions

## Purpose

> HexaGen.CppAst serves as a robust foundation for P/Invoke/Interop code generation and C++ code analysis tools

## Key Features

- **Targeting**: `net9.0` with full AOT compatibility
- **Clang Version**: Uses `ClangSharp 20.1.2` / `libclang 20.1.2`
- **Parsing Modes**: 
  - Parse in-memory C/C++ text
  - Parse C/C++ files from disk
- **Comprehensive AST**: 
  - Simple, intuitive AST model
  - Full type system representation
  - Access to attributes (`_declspec(...)`, `__attribute__((...))`, `[[...]]`)
  - Attached comments with full structure
  - Expression trees for initializers (e.g., `const int x = (1 + 2) << 1`)
- **Macro Support**: Access to macro definitions and tokens via `CppParserOptions.ParseMacros`
- **Direct CXCursor Access**: Every `ICppElement` exposes its underlying `CXCursor` for advanced scenarios

## Architecture Overview

```csharp
// Core interfaces
ICppElement          // Base interface exposing CXCursor
ICppDeclaration      // Declarations with comments
ICppContainer        // Containers with children

// Visitor system
CursorVisitor<TResult>              // Base visitor class
CursorVisitorRegistry<TVisitor, TResult>  // Visitor registration
DeclContainerVisitorRegistry        // Declaration container visitors

// Context management
CppModelContext      // Parsing context
CppContainerContext  // Container scope context
```

## Quick Start

### Setup

After installing the NuGet package, modify your `.csproj` to select a Platform RID:

```xml
<PropertyGroup>
  <!-- Workaround for libclang native dependencies -->
  <RuntimeIdentifier Condition="'$(RuntimeIdentifier)' == '' AND '$(PackAsTool)' != 'true'">$(NETCoreSdkRuntimeIdentifier)</RuntimeIdentifier>
</PropertyGroup>
```

### Basic Usage

```csharp
using HexaGen.CppAst;

// Parse C++ code
var compilation = CppParser.Parse(@"
enum MyEnum { MyEnum_0, MyEnum_1 };
void function0(int a, int b);
struct MyStruct { int field0; int field1;};
typedef MyStruct* MyStructPtr;
");

// Print diagnostics
foreach (var message in compilation.Diagnostics.Messages)
    Console.WriteLine(message);

// Access AST elements
foreach (var cppEnum in compilation.Enums)
    Console.WriteLine(cppEnum);

foreach (var cppFunction in compilation.Functions)
    Console.WriteLine(cppFunction);

foreach (var cppClass in compilation.Classes)
    Console.WriteLine(cppClass);

foreach (var cppTypedef in compilation.Typedefs)
    Console.WriteLine(cppTypedef);
```

Output:
```
enum MyEnum {...}
void function0(int a, int b)
struct MyStruct { ... }
typedef MyStruct* MyStructPtr
```

### Advanced: Direct CXCursor Access

```csharp
// Every ICppElement exposes the underlying CXCursor
var function = compilation.Functions[0];
var cursor = function.Cursor;

// Access low-level libclang information
var linkage = cursor.Linkage;
var availability = cursor.Availability;
var semanticParent = cursor.SemanticParent;

// Use CXCursor for advanced scenarios
if (cursor.Kind == CXCursorKind.CXCursor_FunctionDecl)
{
    // Perform custom cursor operations
    // Visit children, access tokens, etc.
}
```

### Extending the Parser

```csharp
// Create a custom visitor
public class MyCustomVisitor : CursorVisitor<CppElement>
{
    public override IEnumerable<CXCursorKind> Kinds => 
        new[] { CXCursorKind.CXCursor_ClassDecl };

    protected override unsafe CppElement VisitCore(CXCursor cursor, CXCursor parent)
    {
        // Custom parsing logic
        // Access Context, Builder, Container, etc.
        return new MyCustomElement(cursor);
    }
}

// Register the visitor
var registry = new CursorVisitorRegistry<CursorVisitor<CppElement>, CppElement>();
registry.Register<MyCustomVisitor>();
```

## Documentation

For detailed documentation, see the [user guide](doc/readme.md) in the `doc/` folder.

## Migration from CppAst.NET

While HexaGen.CppAst maintains API compatibility with common use cases, the underlying architecture is completely new. Key differences:

1. **Direct Cursor Access**: All elements expose `ICppElement.Cursor` for low-level operations
2. **Visitor Pattern**: Extension points use the visitor pattern instead of callbacks
3. **Improved Type System**: Better canonical type resolution and typedef handling
4. **Modern .NET**: Targets .NET 9 with full AOT support (use CppAst 0.14.0 for netstandard2.0)

## Binaries

Available as a NuGet package: [![NuGet](https://img.shields.io/nuget/v/HexaGen.CppAst.svg)](https://www.nuget.org/packages/HexaGen.CppAst/)

## Known Limitations

Inherited from libclang:

- Attributes are not fully exposed in all contexts (e.g., function parameters, typedefs)
- Generic instance types have limited exposure (e.g., as parameters or base types)

## License

This software is released under the [MIT License](LICENSE.txt).

## Credits

* Original [CppAst.NET](https://github.com/xoofx/CppAst.NET) by Alexandre Mutel (xoofx)
* [ClangSharp](https://github.com/microsoft/ClangSharp): .NET managed wrapper around Clang/libclang

## Related Projects

* [cppast](https://github.com/foonathan/cppast): C++ library with similar goals
* [HexaGen](https://github.com/HexaEngine/HexaGen): Code generator using HexaGen.CppAst

## Author

Juna Meinhold

---

**Note**: While originally inspired by CppAst.NET, HexaGen.CppAst represents a complete architectural reimagining with modern .NET practices, better extensibility, and direct access to the underlying libclang cursor API for advanced parsing scenarios.
