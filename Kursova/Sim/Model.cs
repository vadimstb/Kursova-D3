using System.Globalization;

namespace D3Simulation.Sim;

public sealed class Model(RandomGenerator rng)
{
    private readonly List<Create> creates = [];
    private readonly List<QueueingSystem> systems = [];
    private readonly List<(QueueingSystem Smo, int Channel)> dueOut = [];
    private readonly List<Create> dueArrivals = [];
    private long packetCounter;

    private readonly Dictionary<QueueingSystem, long> delivered = [];
    private readonly Dictionary<QueueingSystem, double> deliveryTime = [];

    public double Time { get; private set; }
    public double ObservedTime { get; private set; }
    public long Blocked { get; private set; }
    public long EventCount { get; private set; }

    public IReadOnlyList<Create> Creates => creates;
    public IReadOnlyList<QueueingSystem> Systems => systems;

    public TextWriter? Protocol { get; set; }
    public double ProtocolFrom { get; set; }
    public double ProtocolTo { get; set; } = double.PositiveInfinity;
    public Func<string>? StateFormatter { get; set; }
    public Action<Model>? AfterEvent { get; set; }

    public Model Add(Create create)
    {
        creates.Add(create);
        return this;
    }

    public Model Add(QueueingSystem system)
    {
        systems.Add(system);
        delivered[system] = 0;
        deliveryTime[system] = 0;
        return this;
    }

    public long DeliveredBy(QueueingSystem s) => delivered[s];
    public double MeanDeliveryTime(QueueingSystem s) =>
        delivered[s] > 0 ? deliveryTime[s] / delivered[s] : 0;

    public void Simulate(double tMod, double tWarmup = 0)
    {
        bool warmedUp = tWarmup <= 0;
        while (true)
        {
            // шукаємо найближчу подію
            double tNext = double.PositiveInfinity;
            foreach (var c in creates) tNext = Math.Min(tNext, c.TNext);
            foreach (var s in systems) tNext = Math.Min(tNext, s.TNext);
            if (!warmedUp) tNext = Math.Min(tNext, tWarmup);
            if (tNext > tMod) tNext = tMod;

            // статистика за проміжок до неї
            double dt = tNext - Time;
            foreach (var s in systems) s.UpdateStatistics(dt);
            ObservedTime += dt;

            Time = tNext;
            // розгін закінчився – починаємо рахувати з нуля
            if (!warmedUp && Time >= tWarmup)
            {
                ResetStatistics();
                warmedUp = true;
            }
            if (Time >= tMod) break;

            ChangeState();
            AfterEvent?.Invoke(this);
        }
    }

    private void ChangeState()
    {
        // спершу звільняємо канали, потім приймаємо нові пакети
        dueOut.Clear();
        foreach (var s in systems)
            for (int r = 0; r < s.ChannelCount; r++)
                if (s.ChannelTNext(r) == Time) dueOut.Add((s, r));
        rng.Shuffle(dueOut);
        foreach (var (smo, channel) in dueOut)
        {
            EventCount++;
            var packet = smo.OutAct(channel, Time);
            if (smo.NextRouter == null)
            {
                delivered[smo]++;
                deliveryTime[smo] += Time - packet.CreatedAt;
                Log($"Кінець обсл. {smo.Name}, #{packet.Id}");
            }
            else
            {
                string to = Route(smo.NextRouter, packet);
                Log($"Кінець обсл. {smo.Name}, #{packet.Id} {to}");
            }
        }

        dueArrivals.Clear();
        foreach (var c in creates)
            if (c.TNext == Time) dueArrivals.Add(c);
        rng.Shuffle(dueArrivals);
        foreach (var create in dueArrivals)
        {
            EventCount++;
            var packet = create.OutAct(Time, ++packetCounter);
            string to = Route(create.Router, packet);
            Log($"Надходж. у {create.Name}, #{packet.Id} {to}");
        }
    }

    private string Route(Router router, Packet packet)
    {
        var target = router.Select();
        if (target == null)
        {
            Blocked++;
            return "-> маршрут заблоковано";
        }
        return target.InAct(packet, Time)
            ? $"-> {target.Name}"
            : $"-> {target.Name} відмова";
    }

    private void ResetStatistics()
    {
        foreach (var c in creates) c.ResetStatistics();
        foreach (var s in systems)
        {
            s.ResetStatistics();
            delivered[s] = 0;
            deliveryTime[s] = 0;
        }
        Blocked = 0;
        ObservedTime = 0;
    }

    private void Log(string message)
    {
        if (Protocol == null || Time < ProtocolFrom || Time > ProtocolTo)
            return;
        string state = StateFormatter?.Invoke() ?? "";
        Protocol.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"{Time,9:F3} | {message,-40} | {state}"));
    }
}
