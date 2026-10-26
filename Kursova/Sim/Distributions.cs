using System.Globalization;

namespace D3Simulation.Sim;

public sealed class RandomGenerator
{
    private readonly Random random;

    public RandomGenerator(int seed) => random = new Random(seed);

    public double NextUniform01()
    {
        double u;
        do { u = random.NextDouble(); } while (u <= 0.0);
        return u;
    }

    // перемішуємо події, які сталися в один момент
    public void Shuffle<T>(IList<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}

public interface IDistribution
{
    double Next(RandomGenerator rng);
    double Mean { get; }
}

public sealed class Deterministic(double value) : IDistribution
{
    public double Mean => value;
    public double Next(RandomGenerator rng) => value;
    public override string ToString() =>
        value.ToString("0.###", CultureInfo.InvariantCulture);
}

public sealed class Uniform(double mean, double spread) : IDistribution
{
    public double Mean => mean;
    public double Spread => spread;
    // рівномірно від mean - spread до mean + spread
    public double Next(RandomGenerator rng) =>
        mean + spread * (2.0 * rng.NextUniform01() - 1.0);
    public override string ToString() => string.Create(
        CultureInfo.InvariantCulture, $"unif({mean:0.###}, {spread:0.###})");
}

public sealed class Exponential(double mean) : IDistribution
{
    public double Mean => mean;
    public double Next(RandomGenerator rng) =>
        -mean * Math.Log(rng.NextUniform01());
    public override string ToString() => string.Create(
        CultureInfo.InvariantCulture, $"exp({mean:0.###})");
}
