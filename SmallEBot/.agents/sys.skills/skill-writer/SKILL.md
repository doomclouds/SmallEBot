---
name: skill-writer
description: Guide users through creating Agent Skills for this app. Use when the user wants to create, write, author, or design a new Skill, or needs help with SKILL.md files, frontmatter, or skill structure. Scripts should be created using C# (.NET 10), which supports single-file execution as scripts.
---

# Skill Writer

This Skill helps you create well-structured Agent Skills for this app that follow best practices and validation requirements.

**Note**: Scripts in Skills should be created using C# (.NET 10), which supports single-file execution. .NET 10 allows running C# files directly as scripts using `dotnet script.cs` or `dotnet run script.cs`, making it ideal for Skill scripts.

## When to use this Skill

Use this Skill when:
- Creating a new Agent Skill
- Writing or updating SKILL.md files
- Designing skill structure and frontmatter
- Troubleshooting skill discovery issues
- Converting existing prompts or workflows into Skills

## Instructions

### Step 1: Determine Skill scope

First, understand what the Skill should do:

1. **Ask clarifying questions**:
   - What specific capability should this Skill provide?
   - When should the agent use this Skill?
   - What tools or resources does it need?
   - Is this for personal use or team sharing?

2. **Keep it focused**: One Skill = one capability
   - Good: "PDF form filling", "Excel data analysis"
   - Too broad: "Document processing", "Data tools"

### Step 2: Choose Skill location

All paths are relative to the **app run directory** (where the application runs).

- **System skills** (read-only, shipped with the app): `.agents/sys.skills/`. Each subdirectory is one skill; directory name = skill id.
- **User skills** (add, edit, delete, import via app UI): `.agents/skills/`. Same structure—one subdirectory per skill with `SKILL.md` and optional other files.

You can add or import user skills through the app's **Skills 配置** (Skills config) in the toolbar; the app creates `.agents/skills/<id>/` for you. To create a skill manually, ensure the directory exists under the run directory (e.g. `.agents/skills/skill-name/`).

### Step 3: Create Skill structure

Create the directory and files (paths relative to app run directory):

```bash
# User skill (manual creation; or use app UI "新增 Skill" / "导入")
mkdir -p .agents/skills/skill-name
```

For multi-file Skills:
```
skill-name/
├── SKILL.md (required)
├── references/ (optional)
│	└── reference1.md
│	└── reference2.md
├── examples/ (optional)
│	└── example1.md
│	└── example2.md
├── scripts/ (optional)
│   └── helper.cs (optional, C# script for .NET 10)
└── templates/ (optional)
    └── template.txt
```

**Script Language**: Use C# (.NET 10) for all scripts. .NET 10 supports single-file execution, allowing C# files to be run directly as scripts without requiring a full project structure.

#### Using C# Scripts in Skills

.NET 10 introduced single-file execution for C# files, making them ideal for Skill scripts:

**Benefits**:
- No project file required - just a single `.cs` file
- Direct execution with `dotnet script.cs` or `dotnet run script.cs`
- Full C# language features and type safety
- Access to all .NET libraries

**Example script structure**:
```csharp
// helper.cs
Console.WriteLine("Hello from C# script!");

if (args.Length > 0)
{
    Console.WriteLine($"Arguments: {string.Join(", ", args)}");
}
```

**Execution**:
```bash
cd scripts
dotnet helper.cs arg1 arg2
# or
dotnet run helper.cs arg1 arg2
```

### Step 4: Write SKILL.md frontmatter

Create YAML frontmatter with required fields:

```yaml
---
name: skill-name
description: Brief description of what this does and when to use it
---
```

**Field requirements**:

- **name**:
  - Lowercase letters, numbers, hyphens only
  - Max 64 characters
  - In this app, skill id = directory name; frontmatter `name` is the display name (can match id).
  - Good: `pdf-processor`, `git-commit-helper`
  - Bad: `PDF_Processor`, `Git Commits!`

- **description**:
  - Max 1024 characters
  - Include BOTH what it does AND when to use it
  - Use specific trigger words users would say
  - Mention file types, operations, and context

**Note**: This app currently parses only **name** and **description** from frontmatter. Other fields (e.g. allowed-tools) are not used; omit them or add later if the app supports them.

### Step 5: Write effective descriptions

The description is critical for the agent to discover your Skill.

**Formula**: `[What it does] + [When to use it] + [Key triggers]`

**Examples**:

✅ **Good**:
```yaml
description: Extract text and tables from PDF files, fill forms, merge documents. Use when working with PDF files or when the user mentions PDFs, forms, or document extraction.
```

✅ **Good**:
```yaml
description: Analyze Excel spreadsheets, create pivot tables, and generate charts. Use when working with Excel files, spreadsheets, or analyzing tabular data in .xlsx format.
```

❌ **Too vague**:
```yaml
description: Helps with documents
description: For data analysis
```

**Tips**:
- Include specific file extensions (.pdf, .xlsx, .json)
- Mention common user phrases ("analyze", "extract", "generate")
- List concrete operations (not generic verbs)
- Add context clues ("Use when...", "For...")

### Step 6: Structure the Skill content

Use clear Markdown sections:

```markdown
# Skill Name

Brief overview of what this Skill does.

## Quick start

Provide a simple example to get started immediately.

## Instructions

Step-by-step guidance for the agent:
1. First step with clear action
2. Second step with expected outcome
3. Handle edge cases

## Examples

Show concrete usage examples with code or commands.

## Best practices

- Key conventions to follow
- Common pitfalls to avoid
- When to use vs. not use

## Requirements

List any dependencies or prerequisites:
```bash
# For .NET 10 C# scripts
dotnet --version  # Ensure .NET 10 SDK is installed

# If using NuGet packages, reference them in the script:
# #:nuget PackageName
```

## Advanced usage

For complex scenarios, see [reference.md](reference.md).
```

### Step 7: Add supporting files (optional)

Create additional files for progressive disclosure:

**reference/**: Detailed API docs, advanced options
**examples/**: Extended examples and use cases
**scripts/**: Helper scripts and utilities
**templates/**: File templates or boilerplate

Reference them from SKILL.md:
```markdown
For advanced usage, see [reference1.md](reference2.md).

Run the helper script:
\`\`\`bash
cd scripts
dotnet helper.cs input.txt
# or
dotnet run helper.cs input.txt
\`\`\`
```

**Script Execution**: C# scripts are executed using `dotnet script.cs [arguments]` or `dotnet run script.cs [arguments]`. Arguments are passed directly to the script.

### Step 8: Validate the Skill

Check these requirements:

✅ **File structure**:
- [ ] SKILL.md exists in correct location
- [ ] Directory name matches frontmatter `name`

✅ **YAML frontmatter**:
- [ ] Opening `---` on line 1
- [ ] Closing `---` before content
- [ ] Valid YAML (no tabs, correct indentation)
- [ ] `name` follows naming rules
- [ ] `description` is specific and < 1024 chars

✅ **Content quality**:
- [ ] Clear instructions for the agent
- [ ] Concrete examples provided
- [ ] Edge cases handled
- [ ] Dependencies listed (if any)

✅ **Testing**:
- [ ] Description matches user questions
- [ ] Skill activates on relevant queries
- [ ] Instructions are clear and actionable

### Step 9: Test the Skill

1. **No restart needed**: After adding or importing a skill, the app rebuilds the agent automatically. Confirm the skill appears in **Skills 配置** and send a message to use it.

2. **Ask relevant questions** that match the description:
   ```
   Can you help me extract text from this PDF?
   ```

3. **Verify activation**: The agent should use the Skill when the query matches the description.

4. **Check behavior**: Confirm the agent follows the instructions correctly.

### Step 10: Debug if needed

If the agent doesn't use the Skill:

1. **Make description more specific**:
   - Add trigger words
   - Include file types
   - Mention common user phrases

2. **Check file location** (paths relative to app run directory):
   ```bash
   # System skill
   .agents/sys.skills/skill-name/SKILL.md
   # User skill
   .agents/skills/skill-name/SKILL.md
   ```
   Use the app's Skills 配置 to confirm the skill is listed, or use the ReadFile tool to read `.agents/sys.skills/<id>/SKILL.md` or `.agents/skills/<id>/SKILL.md`.

3. **Validate YAML**:
   ```bash
   cat SKILL.md | head -n 10
   ```

## Common patterns

### Read-only Skill

```yaml
---
name: code-reader
description: Read and analyze code without making changes. Use for code review, understanding codebases, or documentation.
---
```

### Script-based Skill

```yaml
---
name: data-processor
description: Process CSV and JSON data files with C# scripts. Use when analyzing data files or transforming datasets.
---

# Data Processor

## Instructions

1. Use the processing script:
\`\`\`bash
cd scripts
dotnet process.cs input.csv --output results.json
# or
dotnet run process.cs input.csv --output results.json
\`\`\`

2. Validate output with:
\`\`\`bash
cd scripts
dotnet validate.cs results.json
# or
dotnet run validate.cs results.json
\`\`\`

**Note**: Scripts use C# (.NET 10) which supports single-file execution. Ensure .NET 10 SDK is installed.
```

### Multi-file Skill with progressive disclosure

```yaml
---
name: api-designer
description: Design REST APIs following best practices. Use when creating API endpoints, designing routes, or planning API architecture.
---

# API Designer

Quick start: See [examples.md](examples.md)

Detailed reference: See [reference.md](reference.md)

## Instructions

1. Gather requirements
2. Design endpoints (see examples.md)
3. Document with OpenAPI spec
4. Review against best practices (see reference.md)
```

## Best practices for Skill authors

1. **One Skill, one purpose**: Don't create mega-Skills
2. **Specific descriptions**: Include trigger words users will say
3. **Clear instructions**: Write for the agent, not humans
4. **Concrete examples**: Show real code, not pseudocode
5. **Use C# for scripts**: All scripts should be written in C# (.NET 10) which supports single-file execution
6. **List dependencies**: Mention required .NET SDK version or NuGet packages in description
7. **Test with teammates**: Verify activation and clarity
8. **Version your Skills**: Document changes in content
9. **Use progressive disclosure**: Put advanced details in separate files

## Validation checklist

Before finalizing a Skill, verify:

- [ ] Name is lowercase, hyphens only, max 64 chars
- [ ] Description is specific and < 1024 chars
- [ ] Description includes "what" and "when"
- [ ] YAML frontmatter is valid
- [ ] Instructions are step-by-step
- [ ] Examples are concrete and realistic
- [ ] Dependencies are documented
- [ ] File paths use forward slashes
- [ ] Skill activates on relevant queries
- [ ] The agent follows instructions correctly

## Troubleshooting

**Skill doesn't activate**:
- Make description more specific with trigger words
- Include file types and operations in description
- Add "Use when..." clause with user phrases

**Multiple Skills conflict**:
- Make descriptions more distinct
- Use different trigger words
- Narrow the scope of each Skill

**Skill has errors**:
- Check YAML syntax (no tabs, proper indentation)
- Verify file paths (use forward slashes)
- Ensure .NET 10 SDK is installed for C# scripts
- Verify C# script syntax and compilation
- List all dependencies (NuGet packages or SDK requirements)

## Examples

See the documentation for complete examples:
- Simple single-file Skill (commit-helper)
- Read-only Skill (code-reader)
- Multi-file Skill (pdf-processing)

## Output format

When creating a Skill, I will:

1. Ask clarifying questions about scope and requirements
2. Suggest a Skill name and location
3. Create the SKILL.md file with proper frontmatter
4. Include clear instructions and examples
5. Add supporting files if needed
6. Provide testing instructions
7. Validate against all requirements

The result will be a complete, working Skill that follows all best practices and validation rules.