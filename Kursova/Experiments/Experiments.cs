using System.Globalization;
using System.Text;
using D3Simulation.Sim;

namespace D3Simulation.Experiments;

public sealed class ExperimentSuite(string outputDir)
{
    public const double Hour = 3_600_000;          // година в мс
    public const double Warmup = 300_000;          // розгін моделі
    public const double TMod = Warmup + Hour;
    public const int Replications = 80;            // вистачає для точності

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private readonly ModelParameters basis = new();

    // кожен прогін зі своїм зерном, всі паралельно
    public static SimulationResult[] Replicate(ModelParameters p, int count,
        double tMod, double tWarmup, int seed) =>
        Enumerable.Range(0, count).AsParallel().AsOrdered()
            .Select(i => new D3Network(p, seed + i).Run(tMod, tWarmup))
            .ToArray();

    public void EventProtocol()
    {
        // спершу знаходимо, коли вперше губиться пакет
        const int seed = 7;
        var probe = new D3Network(basis, seed);
        double lossTime = 0;
        probe.Model.AfterEvent = m =>
        {
            if (lossTime == 0 && probe.SatelliteA.Refusals > 0)
                lossTime = m.Time;
        };
        probe.Run(Hour);

        using var w = Writer("protocol.txt");
        w.WriteLine($"Параметри: {basis}");
        w.WriteLine("=== Фрагмент 1: початок імітації ===");
        var net = new D3Network(basis, seed);
        net.Model.Protocol = w;
        net.Model.ProtocolTo = 60;
        net.Model.Simulate(60);
        w.WriteLine($"=== Фрагмент 2: втрата пакета (t = {lossTime:F3}) ===");
        net = new D3Network(basis, seed);
        net.Model.Protocol = w;
        net.Model.ProtocolFrom = lossTime - 45;
        net.Model.ProtocolTo = lossTime + 12;
        net.Model.Simulate(lossTime + 12);
    }

    public void Verification()
    {
        var b = basis;
        var cases = new List<(string Name, ModelParameters P)>
        {
            ("Базові значення", b),
            ("Інтервал надходження 9", b with { ArrivalMean = 9 }),
            ("Інтервал надходження 11", b with { ArrivalMean = 11 }),
            ("Розкид надходження 0", b with { ArrivalSpread = 0 }),
            ("Розкид надходження 6", b with { ArrivalSpread = 6 }),
            ("Час передачі 9", b with { TransmissionTime = 9 }),
            ("Час передачі 11", b with { TransmissionTime = 11 }),
            ("Буфер 1", b with { BufferCapacity = 1 }),
            ("Буфер 4", b with { BufferCapacity = 4 }),
            ("Супутник 5", b with { SatelliteMean = 5, SatelliteSpread = 2.5 }),
            ("Супутник 20", b with { SatelliteMean = 20,
                                     SatelliteSpread = 10 }),
            ("Розкид супутника 0", b with { SatelliteSpread = 0 }),
            ("Розкид супутника 10", b with { SatelliteSpread = 10 }),
            ("Перевантаження", b with { ArrivalMean = 5, ArrivalSpread = 1.5 }),
        };
        const int reps = 20;
        using var w = Writer("verification.csv");
        w.WriteLine("case;arrival;spread;tr;N;sat;satSpread;Ua;buffer;share;" +
                    "f;Usat;Ploss;lostPerHour;delayGround;blocked;balanceOk");
        foreach (var (name, p) in cases)
        {
            var rs = Replicate(p, reps, Hour, 0, 100);
            // жоден пакет не має загубитись в обліку
            bool balance = rs.All(r => r.Arrivals == r.GroundDelivered
                + r.SatelliteServed + r.Lost + r.InSystem);
            w.WriteLine(Row(name, F(p.ArrivalMean), F(p.ArrivalSpread),
                F(p.TransmissionTime), p.BufferCapacity, F(p.SatelliteMean),
                F(p.SatelliteSpread),
                F(rs.Average(r => r.MeanChannelUtilization), 4),
                F(rs.Average(r => r.MeanBuffer), 3),
                F(rs.Average(r => r.SatelliteShare), 5),
                F(rs.Average(r => r.CallFrequency), 3),
                F(rs.Average(r => r.SatelliteUtilization), 4),
                F(rs.Average(r => r.LossProbability), 7),
                F(rs.Average(r => r.LostPerHour), 2),
                F(rs.Average(r => r.MeanDelayGround), 2),
                rs.Sum(r => r.Blocked), balance));
        }
    }

    public void Transient()
    {
        const int runs = 5;
        // базова модель, середні з початку і до t
        const double step = 10_000, horizon = 1_200_000;
        int points = (int)(horizon / step);
        var f = new double[runs, points];
        var u = new double[runs, points];
        Parallel.For(0, runs, i =>
        {
            var net = new D3Network(basis, 500 + i);
            for (int k = 0; k < points; k++)
            {
                net.Model.Simulate((k + 1) * step);
                var r = net.Collect();
                f[i, k] = r.CallFrequency;
                u[i, k] = r.SatelliteUtilization;
            }
        });
        using (var w = Writer("transient_base.csv"))
        {
            w.WriteLine("t;" + Cols("f", runs) + ";" + Cols("u", runs));
            for (int k = 0; k < points; k++)
                w.WriteLine(F((k + 1) * step, 0) + ";" + Series(f, k, runs, 4)
                            + ";" + Series(u, k, runs, 5));
        }

        // великий буфер: старт з порожнього і з половини
        const int n = 40;
        const double step2 = 20_000, horizon2 = 2_400_000;
        int points2 = (int)(horizon2 / step2);
        var empty = new double[runs, points2];
        var preload = new double[runs, points2];
        Parallel.For(0, 2 * runs, j =>
        {
            int i = j % runs;
            bool pre = j >= runs;
            var p = basis with
            {
                BufferCapacity = n,
                InitialBuffer = pre ? n / 2 : 0,
            };
            var net = new D3Network(p, 700 + i);
            var target = pre ? preload : empty;
            for (int k = 0; k < points2; k++)
            {
                net.Model.Simulate((k + 1) * step2);
                target[i, k] = net.Collect().MeanBuffer;
            }
        });
        using (var w = Writer("transient_buffer40.csv"))
        {
            w.WriteLine("t;" + Cols("empty", runs) + ";" + Cols("pre", runs));
            for (int k = 0; k < points2; k++)
                w.WriteLine(F((k + 1) * step2, 0) + ";"
                            + Series(empty, k, runs, 3) + ";"
                            + Series(preload, k, runs, 3));
        }
    }

    public void BaseStatistics()
    {
        var pilot = Replicate(basis, 20, TMod, Warmup, 1000);
        var sf = Statistics.Describe(Select(pilot, r => r.CallFrequency));
        var su = Statistics.Describe(
            Select(pilot, r => r.SatelliteUtilization));
        using (var w = Writer("replications.txt"))
        {
            w.WriteLine($"pilot f: mean={F(sf.Mean, 4)} std={F(sf.Std, 4)}; " +
                        $"Usat: mean={F(su.Mean, 5)} std={F(su.Std, 5)}");
            foreach (double k in new[] { 1.0, 2.0, 4.0 })
                w.WriteLine($"eps = sigma/{k}: p = " +
                    Statistics.RequiredReplications(sf.Std, sf.Std / k));
        }

        var rs = Replicate(basis, Replications, TMod, Warmup, 2000);
        using (var w = Writer("base_runs.csv"))
        {
            w.WriteLine("run;f;Usat;share;Ploss;lost;Ua;Ub;bufA;bufB;" +
                        "delayGround;delaySat");
            for (int i = 0; i < rs.Length; i++)
            {
                var r = rs[i];
                w.WriteLine(Row(i + 1, F(r.CallFrequency, 4),
                    F(r.SatelliteUtilization, 5), F(r.SatelliteShare, 5),
                    F(r.LossProbability, 7), r.Lost,
                    F(r.ChannelUtilizationA, 4), F(r.ChannelUtilizationB, 4),
                    F(r.MeanBufferA, 4), F(r.MeanBufferB, 4),
                    F(r.MeanDelayGround, 3), F(r.MeanDelaySatellite, 3)));
            }
        }
        using (var w = Writer("base_summary.txt"))
        {
            void Report(string name, Func<SimulationResult, double> y)
            {
                var x = Select(rs, y);
                var s = Statistics.Describe(x);
                var chi = Statistics.ChiSquareTest(x, 8,
                    v => Statistics.NormalCdf(v, s.Mean, s.Std), 2);
                w.WriteLine($"{name}: n={s.Count} mean={F(s.Mean, 5)} " +
                    $"std={F(s.Std, 5)} min={F(s.Min, 5)} max={F(s.Max, 5)} " +
                    $"ci95={F(s.ConfidenceHalfWidth(), 5)} | " +
                    $"chi2={F(chi.Statistic)} df={chi.Df} " +
                    $"crit={F(chi.Critical)} p={F(chi.PValue)} " +
                    $"normal={chi.Accepted}");
                w.WriteLine("  edges: " + Join(chi.Edges, 5));
                w.WriteLine("  observed: " + string.Join(' ', chi.Observed));
                w.WriteLine("  expected: " + Join(chi.Expected, 2));
            }
            Report("f, 1/s", r => r.CallFrequency);
            Report("Usat", r => r.SatelliteUtilization);
            Report("share", r => r.SatelliteShare);
            Report("lost per hour", r => r.LostPerHour);
            Report("Ua", r => r.MeanChannelUtilization);
            Report("buffer", r => r.MeanBuffer);
            Report("delay ground", r => r.MeanDelayGround);
        }
        CallIntervals();
    }

    private void CallIntervals()
    {
        var net = new D3Network(basis, 3000);
        net.SatelliteA.ArrivalLog = [];
        net.Run(Warmup + 10 * Hour, Warmup);
        var log = net.SatelliteA.ArrivalLog;
        var intervals = log.Zip(log.Skip(1), (a, b) => b - a).ToArray();
        var si = Statistics.Describe(intervals);
        // закон розподілу перевіряємо на 1000 інтервалів
        var sample = intervals.Take(1000).ToArray();
        var ss = Statistics.Describe(sample);
        double shape = ss.Mean * ss.Mean / (ss.Std * ss.Std);
        double scale = ss.Std * ss.Std / ss.Mean;
        var chiExp = Statistics.ChiSquareTest(sample, 20,
            v => v <= 0 ? 0 : 1 - Math.Exp(-v / ss.Mean), 1);
        var chiGamma = Statistics.ChiSquareTest(sample, 20,
            v => v <= 0 ? 0
                : Statistics.RegularizedGammaP(shape, v / scale), 2);
        using (var w = Writer("call_intervals.txt"))
        {
            w.WriteLine($"all: n={si.Count} mean={F(si.Mean)} " +
                $"std={F(si.Std)} cv={F(si.Std / si.Mean)} " +
                $"min={F(si.Min)} max={F(si.Max)}");
            w.WriteLine($"sample: n={ss.Count} mean={F(ss.Mean)} " +
                $"std={F(ss.Std)} shape={F(shape, 4)} scale={F(scale)}");
            foreach (var (name, chi) in new[] { ("exp", chiExp),
                                                ("gamma", chiGamma) })
                w.WriteLine($"{name}: chi2={F(chi.Statistic, 2)} df={chi.Df} " +
                            $"crit={F(chi.Critical, 2)} p={F(chi.PValue, 4)} " +
                            $"accepted={chi.Accepted}");
        }
        using (var w = Writer("call_intervals.csv"))
        {
            w.WriteLine("interval");
            foreach (double v in intervals) w.WriteLine(F(v));
        }
    }

    public static readonly int[] BufferValues =
        [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 12, 14, 16, 18, 20,
         25, 30, 35, 40, 50, 60];

    public void BufferStudy()
    {
        using var w = Writer("buffer_study.csv");
        w.WriteLine("N;f;fStd;Usat;share;lostPerHour;lostStd;lostMax;" +
                    "zeroLossRuns;Ploss;Ua;buffer;delayGround;maxBuffer");
        var rows = new List<(int N, double F, double Lost)>();
        foreach (int n in BufferValues)
        {
            var p = basis with { BufferCapacity = n, InitialBuffer = n / 2 };
            var rs = Replicate(p, Replications, TMod, Warmup, 10_000 + 100 * n);
            var sf = Statistics.Describe(Select(rs, r => r.CallFrequency));
            var sl = Statistics.Describe(Select(rs, r => r.LostPerHour));
            rows.Add((n, sf.Mean, sl.Mean));
            w.WriteLine(Row(n, F(sf.Mean, 4), F(sf.Std, 4),
                F(rs.Average(r => r.SatelliteUtilization), 5),
                F(rs.Average(r => r.SatelliteShare), 6),
                F(sl.Mean, 3), F(sl.Std, 3), F(sl.Max, 0),
                rs.Count(r => r.Lost == 0),
                F(rs.Average(r => r.LossProbability), 8),
                F(rs.Average(r => r.MeanChannelUtilization), 5),
                F(rs.Average(r => r.MeanBuffer), 3),
                F(rs.Average(r => r.MeanDelayGround), 2),
                rs.Max(r => r.MaxBuffer)));
            w.Flush();
            Console.WriteLine($"  N={n} done");
        }
        BufferRegression(rows.Where(r => r.N >= 2).ToList());
    }

    private void BufferRegression(List<(int N, double F, double Lost)> data)
    {
        using var rep = Writer("buffer_regression.txt");
        var candidates = new (string Name, Func<int, double[]> X)[]
        {
            ("f = b0 + b1/N", n => [1, 1.0 / n]),
            ("f = b0 + b1/(N-1)", n => [1, 1.0 / (n - 1)]),
            ("f = b0 + b1/N + b2/N^2", n => [1, 1.0 / n, 1.0 / (n * n)]),
        };
        foreach (var (name, x) in candidates)
        {
            var reg = Statistics.LeastSquares(
                data.Select(r => x(r.N)).ToList(),
                data.Select(r => r.F).ToList());
            rep.WriteLine($"{name}: b = [{Join(reg.Coefficients, 5)}], " +
                $"R = {F(reg.CorrelationIndex, 5)}, " +
                $"s = {F(reg.ResidualStd, 5)}");
        }
        // залежність степенева, тому беремо логарифми
        foreach (int nMin in new[] { 2, 5 })
        {
            var d = data.Where(r => r.Lost > 0 && r.N >= nMin).ToList();
            var reg = Statistics.LeastSquares(
                d.Select(r => new[] { 1, Math.Log(r.N) }).ToList(),
                d.Select(r => Math.Log(r.Lost)).ToList());
            double b0 = reg.Coefficients[0], b1 = reg.Coefficients[1];
            rep.WriteLine($"N>={nMin}: ln(lost/h) = b0 + b1 ln N: " +
                $"b = [{F(b0, 5)}, {F(b1, 5)}], " +
                $"R = {F(reg.CorrelationIndex, 5)}");
            foreach (double target in new[] { 1.0, 0.1, 0.05, 0.01 })
                rep.WriteLine($"  lost/h = {F(target, 2)}: N >= " +
                              F(Math.Exp((Math.Log(target) - b0) / b1), 1));
        }
    }

    public void HorizonStudy()
    {
        double[] horizons = [10_000, 30_000, 100_000, 300_000, 1_000_000,
                             3_000_000, 10_000_000];
        var p = basis with { BufferCapacity = QueueingSystem.Unlimited };
        using var w = Writer("horizon_study.csv");
        w.WriteLine("T;maxMean;maxStd;maxMin;maxMax;meanBuffer;delayGround");
        var rows = new List<(double T, double Max)>();
        foreach (double h in horizons)
        {
            var rs = Replicate(p, 40, h, 0, 50_000);
            var s = Statistics.Describe(Select(rs, r => r.MaxBuffer));
            rows.Add((h, s.Mean));
            w.WriteLine(Row(F(h, 0), F(s.Mean, 2), F(s.Std, 2), F(s.Min, 0),
                F(s.Max, 0), F(rs.Average(r => r.MeanBuffer), 2),
                F(rs.Average(r => r.MeanDelayGround), 1)));
        }
        var reg = Statistics.LeastSquares(
            rows.Select(r => new[] { 1, Math.Sqrt(r.T) }).ToList(),
            rows.Select(r => r.Max).ToList());
        using var rep = Writer("horizon_regression.txt");
        rep.WriteLine("Nmax = b0 + b1 sqrt(T): " +
            $"b = [{Join(reg.Coefficients, 5)}], " +
            $"R = {F(reg.CorrelationIndex, 5)}");
    }

    public void Proposals()
    {
        var b = basis;
        var variants = new (string Name, ModelParameters P)[]
        {
            ("V0", b),
            ("V1", b with { BufferCapacity = 16, InitialBuffer = 8 }),
            ("V2", b with { DuplexSatellite = true }),
            ("V3", b with { TransmissionTime = 9.5 }),
            ("V4", b with { TransmissionTime = 9 }),
        };
        var lostGroups = new List<IReadOnlyList<double>>();
        using var w = Writer("proposals.csv");
        w.WriteLine("variant;f;Usat;share;lostPerHour;lostStd;zeroLossRuns;" +
                    "Ploss;Ua;buffer;delayGround;delaySat");
        foreach (var (name, p) in variants)
        {
            var rs = Replicate(p, Replications, TMod, Warmup, 90_000);
            var lost = Select(rs, r => r.LostPerHour);
            lostGroups.Add(lost);
            var sl = Statistics.Describe(lost);
            w.WriteLine(Row(name, F(rs.Average(r => r.CallFrequency), 4),
                F(rs.Average(r => r.SatelliteUtilization), 5),
                F(rs.Average(r => r.SatelliteShare), 6),
                F(sl.Mean, 3), F(sl.Std, 3), rs.Count(r => r.Lost == 0),
                F(rs.Average(r => r.LossProbability), 8),
                F(rs.Average(r => r.MeanChannelUtilization), 5),
                F(rs.Average(r => r.MeanBuffer), 3),
                F(rs.Average(r => r.MeanDelayGround), 2),
                F(rs.Average(r => r.MeanDelaySatellite), 2)));
        }
        // чи справді модифікація впливає на втрати
        using var rep = Writer("proposals_anova.txt");
        var sets = new[]
        {
            ("V0..V4", lostGroups),
            ("V1..V4", lostGroups.Skip(1).ToList()),
        };
        foreach (var (title, groups) in sets)
        {
            var a = Statistics.OneWayAnova(groups);
            rep.WriteLine($"{title}: DF={F(a.FactorVariance, 4)} " +
                $"DR={F(a.ResidualVariance, 4)} F={F(a.F, 2)} " +
                $"Fcrit={F(a.FCritical)} df=({a.Df1},{a.Df2}) " +
                $"significant={a.Significant}");
        }
    }

    private StreamWriter Writer(string file)
    {
        Directory.CreateDirectory(outputDir);
        return new StreamWriter(Path.Combine(outputDir, file), false,
            new UTF8Encoding(false));
    }

    private static double[] Select(SimulationResult[] rs,
        Func<SimulationResult, double> y) => rs.Select(y).ToArray();

    private static string F(double v, int digits = 3) =>
        v.ToString("F" + digits, Inv);

    private static string Row(params object[] values) =>
        string.Join(';', values.Select(v => Convert.ToString(v, Inv)));

    private static string Join(IEnumerable<double> v, int digits) =>
        string.Join(' ', v.Select(x => F(x, digits)));

    private static string Cols(string prefix, int n) =>
        string.Join(';', Enumerable.Range(1, n).Select(i => prefix + i));

    private static string Series(double[,] d, int k, int n, int digits) =>
        string.Join(';',
            Enumerable.Range(0, n).Select(i => F(d[i, k], digits)));
}
