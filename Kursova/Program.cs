using System.Diagnostics;
using D3Simulation.Experiments;
using D3Simulation.Sim;

Console.OutputEncoding = System.Text.Encoding.UTF8;

string mode = args.Length > 0 ? args[0] : "run";
string outputDir = args.Length > 1 ? args[1] : "results";
var suite = new ExperimentSuite(outputDir);
var sw = Stopwatch.StartNew();

switch (mode)
{
    case "run": RunOnce(); break;
    case "protocol": suite.EventProtocol(); break;
    case "verify": suite.Verification(); break;
    case "transient": suite.Transient(); break;
    case "base": suite.BaseStatistics(); break;
    case "buffer": suite.BufferStudy(); break;
    case "horizon": suite.HorizonStudy(); break;
    case "proposals": suite.Proposals(); break;
    case "all":
        suite.EventProtocol();
        suite.Verification();
        suite.Transient();
        suite.BaseStatistics();
        suite.BufferStudy();
        suite.HorizonStudy();
        suite.Proposals();
        break;
    default:
        Console.WriteLine("Режими: run | protocol | verify | transient | " +
                          "base | buffer | horizon | proposals | all");
        break;
}
Console.WriteLine($"Виконано за {sw.Elapsed.TotalSeconds:F1} с");

// один прогін базової моделі
static void RunOnce()
{
    var net = new D3Network(new ModelParameters(), seed: 1);
    var r = net.Run(ExperimentSuite.TMod, ExperimentSuite.Warmup);
    Console.WriteLine($"Параметри моделі: {net.Parameters}");
    Console.WriteLine($"Час моделювання: {ExperimentSuite.TMod} мс " +
                      $"(перехідний період {ExperimentSuite.Warmup} мс)");
    Console.WriteLine($"Надійшло пакетів:               {r.Arrivals}");
    Console.WriteLine($"Передано дуплексним каналом:    {r.GroundDelivered}");
    Console.WriteLine($"Викликів супутникової лінії:    {r.SatelliteCalls}");
    Console.WriteLine($"Передано супутниковою лінією:   {r.SatelliteServed}");
    Console.WriteLine($"Втрачено пакетів:               {r.Lost}");
    Console.WriteLine($"Частота викликів, 1/с:          {r.CallFrequency:F4}");
    Console.WriteLine($"Завантаження супутника:         " +
                      $"{r.SatelliteUtilization:F5}");
    Console.WriteLine($"Ймовірність втрати пакета:      " +
                      $"{r.LossProbability:E3}");
    Console.WriteLine($"Завантаження каналів A, B:      " +
        $"{r.ChannelUtilizationA:F4}; {r.ChannelUtilizationB:F4}");
    Console.WriteLine($"Середній вміст буферів A, B:    " +
                      $"{r.MeanBufferA:F4}; {r.MeanBufferB:F4}");
    Console.WriteLine($"Середній час передачі каналом:  " +
                      $"{r.MeanDelayGround:F3} мс");
}
