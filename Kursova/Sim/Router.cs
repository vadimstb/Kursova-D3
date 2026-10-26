namespace D3Simulation.Sim;

public sealed class Route(QueueingSystem target,
    Func<bool> blockingCondition, string conditionText)
{
    public QueueingSystem Target { get; } = target;
    public Func<bool> IsBlocked { get; } = blockingCondition;
    public string ConditionText { get; } = conditionText;
}

public sealed class Router
{
    private readonly List<Route> routes = [];

    public IReadOnlyList<Route> Routes => routes;

    public Router Add(QueueingSystem target, Func<bool> blockingCondition,
        string conditionText)
    {
        routes.Add(new Route(target, blockingCondition, conditionText));
        return this;
    }

    public Router Add(QueueingSystem target) =>
        Add(target, () => false, "0");

    // перший маршрут, який зараз не заблокований
    public QueueingSystem? Select()
    {
        foreach (var route in routes)
        {
            if (!route.IsBlocked())
            {
                return route.Target;
            }
        }
        return null;
    }
}
