namespace D3Simulation.Sim;

public abstract class Element(string name)
{
    public string Name { get; } = name;

    // коли в елементі станеться наступна подія
    public abstract double TNext { get; }

    public abstract void UpdateStatistics(double dt);

    public abstract void ResetStatistics();
}
