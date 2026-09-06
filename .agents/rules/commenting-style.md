# OmniHID Commenting & Code Documentation Rule

When generating, editing, or refactoring C# source code in this repository:

1. **Always apply the standards defined in the `csharp-commenting-style` skill**:
   - Write all comments and documentation strictly in clear English.
   - Provide complete XML documentation (`/// <summary>`, `<param>`, `<returns>`, `<remarks>`) on all public and internal classes, interfaces, structs, enums, methods, and properties.
   - Use standard 75-character section banners (`// ═══════════════════════════════════════════════════════════════════════════`) to partition distinct logical areas.
   - Clearly annotate raw HID packet structures with byte offsets, command codes, bitwise masks, and vendor protocol specifications.
2. **Preserve existing meaningful documentation** and ensure any newly added code maintains 100% stylistic consistency with the rest of the codebase.
3. **Avoid `#region` tags** and empty or redundant comments.
