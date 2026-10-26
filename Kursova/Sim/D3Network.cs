using System.Globalization;

namespace D3Simulation.Sim;

// усі часи в мілісекундах
public sealed record ModelParameters
{
    public double ArrivalMean { get; init; } = 10;
    public double ArrivalSpread { get; init; } = 3;
    public double TransmissionTime { get; init; } = 10;
    // місця в буфері разом з пакетом, що передається
    public int BufferCapacity { get; init; } = 2;
    public double SatelliteMean { get; init; } = 10;
    public double SatelliteSpread { get; init; } = 5;
    public bool DuplexSatellite { get; init; }      // для пропозиції V2
    public int InitialBuffer { get; init; }

    public override string ToString() => string.Format(
        CultureInfo.InvariantCulture,
        "a=unif({0},{1}) tr={2} N={3} sat=unif({4},{5}){6}",
        ArrivalMean, ArrivalSpread, TransmissionTime, BufferCapacity,
        SatelliteMean, SatelliteSpread, DuplexSatellite ? " duplexSat" : "");
}

public sealed record SimulationResult
{
    public double ObservedTime { get; init; }
    public long Arrivals { get; init; }
    public long GroundDelivered { get; init; }
    public long SatelliteCalls { get; init; }
    public long SatelliteServed { get; init; }
    public long Lost { get; init; }
    public long InSystem { get; init; }
    public long Blocked { get; init; }
    public double SatelliteBusyTime { get; init; }
    public double SatelliteUtilization { get; init; }
    public double ChannelUtilizationA { get; init; }
    public double ChannelUtilizationB { get; init; }
    public double MeanBufferA { get; init; }
    public double MeanBufferB { get; init; }
    public int MaxBufferA { get; init; }
    public int MaxBufferB { get; init; }
    public double MeanDelayGround { get; init; }
    public double MeanDelaySatellite { get; init; }

    // виклики супутника за секунду
    public double CallFrequency => SatelliteCalls / ObservedTime * 1000.0;
    public double SatelliteShare =>
        Arrivals > 0 ? (double)SatelliteCalls / Arrivals : 0;
    public double LossProbability =>
        Arrivals > 0 ? (double)Lost / Arrivals : 0;
    public double LostPerHour => Lost / ObservedTime * 3_600_000.0;
    public double MeanChannelUtilization =>
        (ChannelUtilizationA + ChannelUtilizationB) / 2;
    public double MeanBuffer => (MeanBufferA + MeanBufferB) / 2;
    public int MaxBuffer => Math.Max(MaxBufferA, MaxBufferB);
}

public sealed class D3Network
{
    public D3Network(ModelParameters p, int seed)
    {
        Parameters = p;
        Rng = new RandomGenerator(seed);
        var arrival = new Uniform(p.ArrivalMean, p.ArrivalSpread);
        var transmission = new Deterministic(p.TransmissionTime);
        var satDelay = new Uniform(p.SatelliteMean, p.SatelliteSpread);
        // одне місце в буфері займає пакет, що зараз передається
        int maxQueue = p.BufferCapacity == QueueingSystem.Unlimited
            ? QueueingSystem.Unlimited
            : p.BufferCapacity - 1;

        GenA = new Create("пункт A", Packet.PointA, arrival, Rng,
            arrival.Next(Rng));
        GenB = new Create("пункт B", Packet.PointB, arrival, Rng,
            arrival.Next(Rng));
        ChannelA = new QueueingSystem("СМО1", 1, maxQueue, transmission, Rng);
        ChannelB = new QueueingSystem("СМО2", 1, maxQueue, transmission, Rng);
        SatelliteA = new QueueingSystem("СМО3", 1, 0, satDelay, Rng);
        SatelliteB = p.DuplexSatellite
            ? new QueueingSystem("СМО4", 1, 0, satDelay, Rng)
            : SatelliteA;

        // поки в буфері є місце – туди, інакше на супутник
        GenA.Router = new Router()
            .Add(ChannelA, () => !ChannelA.HasFreePlace, "K1 + L1 = N")
            .Add(SatelliteA, () => ChannelA.HasFreePlace, "K1 + L1 < N");
        GenB.Router = new Router()
            .Add(ChannelB, () => !ChannelB.HasFreePlace, "K2 + L2 = N")
            .Add(SatelliteB, () => ChannelB.HasFreePlace, "K2 + L2 < N");

        Model = new Model(Rng).Add(GenA).Add(GenB)
            .Add(ChannelA).Add(ChannelB).Add(SatelliteA);
        if (p.DuplexSatellite) Model.Add(SatelliteB);
        Model.StateFormatter = FormatState;

        if (p.InitialBuffer > 0)
        {
            ChannelA.Preload(InitialPackets(Packet.PointA), 0);
            ChannelB.Preload(InitialPackets(Packet.PointB), 0);
        }
    }

    public ModelParameters Parameters { get; }
    public RandomGenerator Rng { get; }
    public Model Model { get; }
    public Create GenA { get; }
    public Create GenB { get; }
    public QueueingSystem ChannelA { get; }
    public QueueingSystem ChannelB { get; }
    public QueueingSystem SatelliteA { get; }
    public QueueingSystem SatelliteB { get; }

    private IEnumerable<QueueingSystem> Satellites => Parameters.DuplexSatellite
        ? [SatelliteA, SatelliteB]
        : [SatelliteA];

    private IEnumerable<Packet> InitialPackets(int source) =>
        Enumerable.Range(0, Parameters.InitialBuffer)
            .Select(_ => new Packet(-1, source, 0));

    public SimulationResult Run(double tMod, double tWarmup = 0)
    {
        Model.Simulate(tMod, tWarmup);
        return Collect();
    }

    public SimulationResult Collect()
    {
        var m = Model;
        double busy = Satellites.Sum(s => s.BusyTime);
        long satServed = Satellites.Sum(s => m.DeliveredBy(s));
        long ground = m.DeliveredBy(ChannelA) + m.DeliveredBy(ChannelB);
        double groundTime =
            m.MeanDeliveryTime(ChannelA) * m.DeliveredBy(ChannelA) +
            m.MeanDeliveryTime(ChannelB) * m.DeliveredBy(ChannelB);
        double satTime = Satellites.Sum(
            s => m.MeanDeliveryTime(s) * m.DeliveredBy(s));
        return new SimulationResult
        {
            ObservedTime = m.ObservedTime,
            Arrivals = GenA.Arrivals + GenB.Arrivals,
            GroundDelivered = ground,
            SatelliteCalls = Satellites.Sum(s =>
                s.ArrivalsFrom(Packet.PointA) + s.ArrivalsFrom(Packet.PointB)),
            SatelliteServed = satServed,
            Lost = Satellites.Sum(s => s.Refusals),
            InSystem = ChannelA.Occupancy + ChannelB.Occupancy +
                       Satellites.Sum(s => s.Occupancy),
            Blocked = m.Blocked,
            SatelliteBusyTime = busy,
            SatelliteUtilization =
                busy / m.ObservedTime / Satellites.Count(),
            ChannelUtilizationA = ChannelA.Utilization,
            ChannelUtilizationB = ChannelB.Utilization,
            MeanBufferA = ChannelA.MeanOccupancy,
            MeanBufferB = ChannelB.MeanOccupancy,
            MaxBufferA = ChannelA.MaxOccupancy,
            MaxBufferB = ChannelB.MaxOccupancy,
            MeanDelayGround = groundTime / Math.Max(1, ground),
            MeanDelaySatellite = satTime / Math.Max(1, satServed),
        };
    }

    private string FormatState()
    {
        string sat = Parameters.DuplexSatellite
            ? $"K3={SatelliteA.BusyChannels} K4={SatelliteB.BusyChannels}"
            : $"K3={SatelliteA.BusyChannels}";
        long calls = Satellites.Sum(s => s.ArrivalsFrom(0) + s.ArrivalsFrom(1));
        long lost = Satellites.Sum(s => s.Refusals);
        return $"K1={ChannelA.BusyChannels} L1={ChannelA.QueueLength} " +
               $"K2={ChannelB.BusyChannels} L2={ChannelB.QueueLength} " +
               $"{sat} | Nв={calls} Nвт={lost}";
    }
}
