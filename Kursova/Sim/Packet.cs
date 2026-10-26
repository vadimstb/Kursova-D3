namespace D3Simulation.Sim;

public sealed class Packet(long id, int source, double createdAt)
{
    public const int PointA = 0;
    public const int PointB = 1;

    public long Id { get; } = id;
    public int Source { get; } = source;   // з якого пункту пакет
    public double CreatedAt { get; } = createdAt;

    public string SourceName => Source == PointA ? "A" : "B";
}
