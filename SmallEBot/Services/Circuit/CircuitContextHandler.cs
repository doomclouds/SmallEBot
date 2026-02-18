using Microsoft.AspNetCore.Components.Server.Circuits;
using ServerCircuit = Microsoft.AspNetCore.Components.Server.Circuits.Circuit;

namespace SmallEBot.Services.Circuit;

/// <summary>Sets the current circuit in ICurrentCircuitAccessor when a circuit opens.</summary>
internal sealed class CircuitContextHandler(ICurrentCircuitAccessor accessor) : CircuitHandler
{
    public override Task OnCircuitOpenedAsync(ServerCircuit circuit, CancellationToken cancellationToken)
    {
        accessor.CurrentCircuit = circuit;
        return base.OnCircuitOpenedAsync(circuit, cancellationToken);
    }

    public override Task OnCircuitClosedAsync(ServerCircuit circuit, CancellationToken cancellationToken)
    {
        if (accessor.CurrentCircuit == circuit)
            accessor.CurrentCircuit = null;
        return base.OnCircuitClosedAsync(circuit, cancellationToken);
    }
}
