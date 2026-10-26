namespace D3Simulation.Sim;

public sealed class QueueingSystem : Element
{
    public const int Unlimited = int.MaxValue;

    private readonly IDistribution delay;
    private readonly RandomGenerator rng;
    private readonly Packet?[] channelPacket; // null – канал вільний
    private readonly double[] channelTNext;   // коли канал звільниться
    private readonly Queue<Packet> queue = new();

    private double busyTimeIntegral;
    private double queueIntegral;
    private double occupancyIntegral;
    private double observedTime;
    private readonly long[] arrivalsBySource = new long[2];
    private readonly long[] refusalsBySource = new long[2];

    public QueueingSystem(string name, int channels, int maxQueue,
        IDistribution delay, RandomGenerator rng) : base(name)
    {
        ChannelCount = channels;
        MaxQueue = maxQueue;
        this.delay = delay;
        this.rng = rng;
        channelPacket = new Packet?[channels];
        channelTNext = Enumerable.Repeat(double.PositiveInfinity, channels)
            .ToArray();
    }

    public int ChannelCount { get; }
    public int MaxQueue { get; }
    public IDistribution Delay => delay;
    public Router? NextRouter { get; set; }

    public long Accepted { get; private set; }
    public long Served { get; private set; }
    public long Refusals { get; private set; }
    public int MaxOccupancy { get; private set; }
    public List<double>? ArrivalLog { get; set; }

    public int BusyChannels
    {
        get
        {
            int busy = 0;
            foreach (var p in channelPacket) if (p != null) busy++;
            return busy;
        }
    }

    public int QueueLength => queue.Count;
    public int Occupancy => BusyChannels + queue.Count;
    public bool HasFreePlace =>
        BusyChannels < ChannelCount || queue.Count < MaxQueue;

    public override double TNext
    {
        get
        {
            double min = double.PositiveInfinity;
            foreach (var tr in channelTNext) if (tr < min) min = tr;
            return min;
        }
    }

    public double ChannelTNext(int channel) => channelTNext[channel];
    public long ArrivalsFrom(int source) => arrivalsBySource[source];
    public long RefusalsFrom(int source) => refusalsBySource[source];

    public bool InAct(Packet packet, double t)
    {
        arrivalsBySource[packet.Source]++;
        ArrivalLog?.Add(t);
        for (int r = 0; r < ChannelCount; r++)
        {
            if (channelPacket[r] == null)
            {
                StartService(r, packet, t);
                Accepted++;
                UpdateMaxOccupancy();
                return true;
            }
        }
        if (queue.Count < MaxQueue)
        {
            queue.Enqueue(packet);
            Accepted++;
            UpdateMaxOccupancy();
            return true;
        }
        // місця немає – відмова
        Refusals++;
        refusalsBySource[packet.Source]++;
        return false;
    }

    public Packet OutAct(int channel, double t)
    {
        var finished = channelPacket[channel]
            ?? throw new InvalidOperationException($"{Name}: канал вільний");
        channelPacket[channel] = null;
        channelTNext[channel] = double.PositiveInfinity;
        Served++;
        // канал звільнився – беремо наступний пакет з черги
        if (queue.Count > 0)
        {
            StartService(channel, queue.Dequeue(), t);
        }
        return finished;
    }

    // стартовий стан ближчий до сталого, щоб модель швидше "розігналась"
    public void Preload(IEnumerable<Packet> packets, double t)
    {
        foreach (var packet in packets)
        {
            if (!HasFreePlace) break;
            InAct(packet, t);
        }
        ResetStatistics();
    }

    private void StartService(int channel, Packet packet, double t)
    {
        channelPacket[channel] = packet;
        channelTNext[channel] = t + delay.Next(rng);
    }

    private void UpdateMaxOccupancy()
    {
        int occupancy = Occupancy;
        if (occupancy > MaxOccupancy) MaxOccupancy = occupancy;
    }

    // накопичуємо "площу під графіком" для середніх за часом
    public override void UpdateStatistics(double dt)
    {
        int busy = BusyChannels;
        busyTimeIntegral += busy * dt;
        queueIntegral += queue.Count * dt;
        occupancyIntegral += (busy + queue.Count) * dt;
        observedTime += dt;
    }

    public override void ResetStatistics()
    {
        busyTimeIntegral = queueIntegral = occupancyIntegral = 0;
        observedTime = 0;
        Accepted = Served = Refusals = 0;
        Array.Clear(arrivalsBySource);
        Array.Clear(refusalsBySource);
        MaxOccupancy = Occupancy;
        ArrivalLog?.Clear();
    }

    public double Utilization => observedTime > 0
        ? busyTimeIntegral / observedTime / ChannelCount : 0;
    public double MeanQueue =>
        observedTime > 0 ? queueIntegral / observedTime : 0;
    public double MeanOccupancy =>
        observedTime > 0 ? occupancyIntegral / observedTime : 0;
    public double BusyTime => busyTimeIntegral;
}
