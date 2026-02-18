namespace SmallEBot.Services.Circuit;

/// <summary>Provides the current Blazor circuit for the active connection. Set by CircuitHandler; used for command confirmation context.</summary>
public interface ICurrentCircuitAccessor
{
    Microsoft.AspNetCore.Components.Server.Circuits.Circuit? CurrentCircuit { get; set; }
}
