namespace D3Simulation.Sim;

public sealed class Create : Element
{
    private readonly IDistribution delay;
    private readonly RandomGenerator rng;
    private double tNextArrival;

    public Create(string name, int source, IDistribution delay,
        RandomGenerator rng, double firstArrival) : base(name)
    {
        Source = source;
        this.delay = delay;
        this.rng = rng;
        tNextArrival = firstArrival;
    }

    public int Source { get; }
    public Router Router { get; set; } = null!;
    public long Arrivals { get; private set; }

    public override double TNext => tNextArrival;

    public Packet OutAct(double t, long packetId)
    {
        Arrivals++;
        // одразу плануємо наступний пакет
        tNextArrival = t + delay.Next(rng);
        return new Packet(packetId, Source, t);
    }

    public override void UpdateStatistics(double dt) { }

    public override void ResetStatistics() => Arrivals = 0;
}
