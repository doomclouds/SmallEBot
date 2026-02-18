namespace SmallEBot.Services.Circuit;

/// <summary>Scoped holder for the current circuit. Set by CircuitHandler.</summary>
public sealed class CurrentCircuitAccessor : ICurrentCircuitAccessor
{
    public Microsoft.AspNetCore.Components.Server.Circuits.Circuit? CurrentCircuit { get; set; }
}
