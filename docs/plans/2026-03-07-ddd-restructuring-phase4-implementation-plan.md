# SmallEBot DDD Restructuring - Phase 4: Host Layer Refactoring

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Refactor Host layer to only contain Blazor UI components and DI registration, removing business logic to proper layers.

**Architecture:** Clean separation where Host only depends on Application.Contracts, with all implementations injected via DI.

**Tech Stack:** .NET 10, Blazor Server, MudBlazor, Microsoft.Extensions.DependencyInjection

---

## Current State Analysis

After Phase 3, the Host layer still contains:
- Service implementations that should be moved or already implement Contracts interfaces
- Direct dependencies on concrete types instead of Contracts interfaces
- Some Blazor components still inject concrete types

## Target Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                    Host (SmallEBot)                            │
│  - Blazor Components (.razor) - inject Contracts interfaces  │
│  - Program.cs / DI Configuration                               │
│  - Blazor-specific services (Circuit, JS interop)            │
│  - NO business logic implementations                            │
└─────────────────────────────────────────────────────────────┘
```

---

## Task 4.1: Audit Host Layer Services

**Goal:** Identify all services in Host layer and categorize them.

**Files to check:**
- `SmallEBot/Services/` - all subdirectories
- `SmallEBot/Extensions/ServiceCollectionExtensions.cs`

**Step 1: List all services in Host layer**

Run: `find SmallEBot/Services -name "*.cs" -type f`

**Step 2: Categorize each service:**

For each service file, determine:
1. Does it implement a Contracts interface? → Keep, ensure proper interface registration
2. Is it Blazor-specific (Circuit, JS interop)? → Keep in Host
3. Is it business logic? → Should have been moved in Phase 3, verify
4. Is it infrastructure (file, network)? → Should be in Infrastructure

**Step 3: Document findings**

Create a list of:
- Services to keep in Host (Blazor-specific)
- Services that should implement Contracts interfaces
- Services that need to be moved

---

## Task 4.2: Remove Business Logic from Host Services

**Goal:** Ensure no business logic remains in Host layer services.

**Files to check:**
- All services in `SmallEBot/Services/`

**Step 1: Review each service for business logic**

Check for:
- Domain logic (validation, business rules)
- Data access patterns
- External service calls

**Step 2: For services with business logic:**

1. If interface exists in Contracts → ensure implementation delegates to proper layer
2. If no interface → create interface in Contracts

**Step 3: Verify services only orchestrate/delegate**

Host services should:
- Receive UI requests
- Call Application/Infrastructure services
- Return results to UI

---

## Task 4.3: Update Blazor Component Injections

**Goal:** All Blazor components inject Contracts interfaces, not concrete types.

**Files to modify:**
- `SmallEBot/Components/**/*.razor`
- `SmallEBot/Pages/**/*.razor`

**Step 1: Find all @inject statements**

Run: `grep -r "@inject" SmallEBot/Components SmallEBot/Pages`

**Step 2: For each @inject:**

1. Check if injecting concrete type
2. If concrete type → change to Contracts interface
3. Add `@using` for Contracts namespace if needed

**Example transformation:**

Before:
```razor
@inject AgentConfigService AgentConfig
```

After:
```razor
@using SmallEBot.Application.Agents
@inject IAgentConfigService AgentConfig
```

**Step 3: Verify components compile**

Run: `dotnet build SmallEBot`

---

## Task 4.4: Consolidate DI Registration

**Goal:** Clean up DI registration to use Contracts interfaces consistently.

**Files:**
- `SmallEBot/Extensions/ServiceCollectionExtensions.cs`
- `SmallEBot/Program.cs`

**Step 1: Review current DI registrations**

Check that:
- All services are registered with their Contracts interface
- Lifetimes are appropriate (Singleton/Scoped/Transient)
- No concrete types are registered without interface

**Step 2: Update registrations**

Pattern:
```csharp
// Correct:
services.AddScoped<IAgentConfigService, AgentConfigService>();

// Incorrect:
services.AddScoped<AgentConfigService>();
```

**Step 3: Remove redundant registrations**

Remove any duplicate or unnecessary service registrations.

---

## Task 4.5: Remove Unused Host Services

**Goal:** Remove services that are no longer needed in Host layer.

**Step 1: Identify unused services**

Check for services that:
- Were moved to Application/Infrastructure in Phase 3
- Are no longer referenced
- Are duplicates

**Step 2: Remove files and registrations**

1. Delete service files
2. Remove DI registrations
3. Update any remaining references

**Step 3: Verify build**

Run: `dotnet build`

---

## Task 4.6: Final Verification

**Goal:** Verify all functionality works after refactoring.

**Step 1: Build entire solution**

Run: `dotnet build`

Expected: 0 errors

**Step 2: Run application**

Run: `dotnet run --project SmallEBot`

Expected: Application starts without errors

**Step 3: Verify core functionality**

Test:
1. Create new conversation
2. Send message and receive response
3. File operations in workspace
4. Settings changes

**Step 4: Commit**

```bash
git add -A
git commit -m "refactor(host): complete Phase 4 DDD restructuring

- Remove business logic from Host layer
- All Blazor components use Contracts interfaces
- Clean up DI registration
- Remove unused services"
```

---

## Phase 4 Summary

After Phase 4 completion:

```
SmallEBot (Host)/
├── Components/
│   └── **/                   # Only UI, injects Contracts interfaces
├── Pages/
│   └── **/                   # Only UI, injects Contracts interfaces
├── Extensions/
│   └── ServiceCollectionExtensions.cs  # DI registration only
├── Services/
│   ├── Circuit/               # Blazor-specific (keep)
│   └── Presentation/           # UI-specific services (keep)
├── Program.cs                  # Entry point
└── _Imports.razor              # Global usings
```

**Dependency Flow:**
```
Blazor Component
    ↓ @inject
Application.Contracts (interface)
    ↓ DI resolves to
Application/Infrastructure (implementation)
    ↓
Domain (entities)
```

---

**Phase 4 Complete!**
