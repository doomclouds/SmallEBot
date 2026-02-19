# Grep Tools Design

## Overview

Add two built-in tools for searching files and content in the workspace, similar to grep functionality.

## Tools

### 1. GrepFiles - Search file names

Search for files by glob pattern or regex in the workspace.

**Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `pattern` | string | Yes | Search pattern |
| `mode` | string | No | Match mode: `"glob"` (default) or `"regex"` |
| `path` | string | No | Starting directory, defaults to workspace root |
| `maxDepth` | int | No | Max recursion depth, 0 for unlimited, default 10 |

**Return format (JSON):**

```json
{
  "pattern": "*.cs",
  "mode": "glob",
  "path": "src",
  "files": ["src/Program.cs", "src/Services/MyService.cs"],
  "count": 2
}
```

**Examples:**

```
GrepFiles("*.cs")              // Find all .cs files
GrepFiles("**/test*.py")       // Find all .py files starting with "test"
GrepFiles("(?i)^readme", mode: "regex")  // Case-insensitive match
```

### 2. GrepContent - Search file content

Search content within allowed file types with full grep functionality.

**Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `pattern` | string | Yes | Regex search pattern |
| `path` | string | No | Starting directory, defaults to workspace root |
| `filePattern` | string | No | Glob file filter (e.g. `"*.cs"`), default all allowed extensions |
| `ignoreCase` | bool | No | Case-insensitive match, default `false` |
| `contextLines` | int | No | Show N lines before and after match (-C), default 0 |
| `beforeLines` | int | No | Show N lines before match, default 0 |
| `afterLines` | int | No | Show N lines after match, default 0 |
| `filesOnly` | bool | No | Return only file names, no content, default `false` |
| `countOnly` | bool | No | Return only match counts per file, default `false` |
| `invertMatch` | bool | No | Invert match (show non-matching lines), default `false` |
| `maxResults` | int | No | Max match results, default 100 |

**Return format (JSON):**

```json
{
  "pattern": "TODO|FIXME",
  "path": "src",
  "filePattern": "*.cs",
  "results": [
    {
      "file": "src/Program.cs",
      "matches": [
        {
          "line": 15,
          "content": "// TODO: implement error handling",
          "context": ["14: var x = 1;", "15: // TODO: implement", "16: return x;"]
        }
      ],
      "matchCount": 1
    }
  ],
  "totalMatches": 5,
  "filesScanned": 12,
  "truncated": false
}
```

**Examples:**

```
GrepContent("TODO")                              // Find all TODO
GrepContent("public\\s+class")                   // Find class definitions
GrepContent("TODO", filePattern: "*.cs")         // Search only .cs files
GrepContent("error", ignoreCase: true)           // Case-insensitive
GrepContent("function", contextLines: 2)         // Show 2 lines of context
GrepContent("import", filesOnly: true)           // Only list matching files
GrepContent("TODO", countOnly: true)             // Return match counts only
GrepContent("^$", invertMatch: true)             // Find non-empty lines
```

## Implementation

### Location

All changes in `SmallEBot\Services\Agent\BuiltInToolFactory.cs`:

- Add `GrepFiles` private method
- Add `GrepContent` private method
- Register tools in `CreateTools()`

### Dependencies

Use .NET built-in libraries:

- `System.Text.RegularExpressions` - Regex matching
- `System.IO` - File traversal
- `Microsoft.Extensions.FileSystemGlobbing` - Glob pattern matching

### Security & Performance

| Aspect | Handling |
|--------|----------|
| Path escape | Validate all paths are within workspace |
| File types | Only search `AllowedFileExtensions` |
| Large results | `maxResults` limit + `truncated` flag |
| Large files | Skip files over 1MB |
| Recursion depth | `maxDepth` limit (GrepFiles) |

### Error Handling

```
- Invalid regex → "Error: Invalid regex pattern: ..."
- Path not found → "Error: Directory not found."
- Path out of bounds → "Error: path must be under the workspace."
- No matches → Return empty list (not an error)
```

## Documentation Updates

After implementation, update:

1. **AGENTS.md** - Add tools to Built-in tools table
2. **README.md** - Add tools to 内置工具 table
