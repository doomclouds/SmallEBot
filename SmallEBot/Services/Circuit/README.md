# Circuit Context Management

This folder contains utilities for tracking the current Blazor Circuit in Blazor Server applications.

## What is a Blazor Circuit?

In Blazor Server, a **Circuit** represents a unique user connection/session. Each browser tab connecting to a Blazor Server app gets its own Circuit with a unique `CircuitId`. The Circuit manages:

- SignalR connection state
- Component hierarchy and rendering
- Scoped services lifetime
- User-specific state

When a user closes their browser tab or refreshes, the Circuit is disconnected. When they reconnect, a new Circuit is created.

## Components

| File | Purpose |
|------|---------|
| `ICurrentCircuitAccessor.cs` | Interface defining the circuit accessor contract |
| `CurrentCircuitAccessor.cs` | Simple implementation that holds the current circuit |
| `CircuitContextHandler.cs` | `CircuitHandler` that captures the circuit on open/closed events |

## How It Works

```
┌─────────────────────────────────────────────────────────────┐
│                     User Browser Tab                         │
│                          │                                   │
│                    SignalR Connection                        │
│                          │                                   │
│                          ▼                                   │
│  ┌─────────────────────────────────────────────────────────┐│
│  │                    Blazor Circuit                        ││
│  │  (unique per tab, has CircuitId)                         ││
│  └─────────────────────────────────────────────────────────┘│
│                          │                                   │
│            CircuitContextHandler.OnCircuitOpenedAsync        │
│                          │                                   │
│                          ▼                                   │
│  ┌─────────────────────────────────────────────────────────┐│
│  │           CurrentCircuitAccessor (Scoped)                ││
│  │  CurrentCircuit = circuit                                ││
│  └─────────────────────────────────────────────────────────┘│
│                          │                                   │
│          Any component/service can now access CircuitId      │
└─────────────────────────────────────────────────────────────┘
```

**Registration (in `ServiceCollectionExtensions.cs`):**

```csharp
services.AddScoped<ICurrentCircuitAccessor, CurrentCircuitAccessor>();
services.AddScoped<CircuitHandler, CircuitContextHandler>();
```

**Flow:**

1. When a user connects, Blazor creates a new Circuit
2. `CircuitContextHandler.OnCircuitOpenedAsync` is called
3. The handler stores the Circuit in `CurrentCircuitAccessor`
4. Any component/service can now access `CircuitAccessor.CurrentCircuit?.Id`
5. When the user disconnects, `OnCircuitClosedAsync` clears the reference

## Why This Project Uses It

### Use Case: Command Confirmation Context Association

When the AI agent requests to execute a shell command, the system may require user confirmation (configurable). The confirmation UI needs to know **which user session** is making the request.

**Problem:**
- Multiple users could be using the app simultaneously
- Command confirmation requests are broadcast via events
- Each user should only see confirmations for **their own** commands

**Solution:**
1. Chat area passes `CircuitAccessor.CurrentCircuit?.Id` as `contextId` to the conversation pipeline
2. When a command needs confirmation, the `contextId` is attached to the pending request
3. `CommandConfirmationStrip` component compares incoming request's `contextId` with its own Circuit ID
4. Only requests matching the current user's Circuit are displayed

**Example from `CommandConfirmationStrip.razor`:**

```csharp
private void OnPendingRequestAdded(object? sender, PendingRequestEventArgs e)
{
    var myId = CircuitAccessor.CurrentCircuit?.Id;
    if (string.IsNullOrEmpty(myId) || !string.Equals(e.ContextId, myId, StringComparison.Ordinal))
        return;  // Not my request, ignore

    _pendingRequest = e.Request;  // This is my request, show it
    _ = InvokeAsync(StateHasChanged);
}
```

## How to Use

### In a Component

```razor
@inject ICurrentCircuitAccessor CircuitAccessor

@code {
    private string? GetMyCircuitId()
    {
        return CircuitAccessor.CurrentCircuit?.Id;
    }
}
```

### In a Service

```csharp
public class MyService
{
    private readonly ICurrentCircuitAccessor _circuitAccessor;

    public MyService(ICurrentCircuitAccessor circuitAccessor)
    {
        _circuitAccessor = circuitAccessor;
    }

    public string? GetCurrentContextId()
    {
        return _circuitAccessor.CurrentCircuit?.Id;
    }
}
```

## Important Notes

1. **Scoped Lifetime**: Both `ICurrentCircuitAccessor` and `CircuitHandler` are registered as **Scoped**, meaning each Circuit gets its own instance.

2. **Null Safety**: `CurrentCircuit` can be null if accessed outside a valid Blazor context (e.g., background services). Always use null-conditional operators.

3. **Circuit ID Uniqueness**: The `Circuit.Id` is a GUID generated by Blazor for each connection. It's unique per browser tab/connection.

4. **Reconnection**: If a user refreshes the page, a new Circuit is created with a new ID. The old Circuit's resources are cleaned up via `OnCircuitClosedAsync`.
