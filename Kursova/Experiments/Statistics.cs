namespace D3Simulation.Experiments;

public sealed record Summary(int Count, double Mean, double Std,
    double Min, double Max)
{
    public double ConfidenceHalfWidth(double beta = 0.95) => Count < 2 ? 0
        : Statistics.StudentQuantile(0.5 + beta / 2, Count - 1)
          * Std / Math.Sqrt(Count);
}

public sealed record ChiSquareResult(double Statistic, int Df,
    double Critical, double PValue, double[] Edges, int[] Observed,
    double[] Expected)
{
    public bool Accepted => Statistic < Critical;
}

public sealed record RegressionResult(double[] Coefficients,
    double CorrelationIndex, double ResidualStd);

public sealed record AnovaResult(double FactorVariance,
    double ResidualVariance, double F, double FCritical, int Df1, int Df2)
{
    public bool Significant => F > FCritical;
}

public static class Statistics
{
    public static Summary Describe(IReadOnlyList<double> x)
    {
        double mean = x.Average();
        double ss = x.Sum(v => (v - mean) * (v - mean));
        double std = x.Count > 1 ? Math.Sqrt(ss / (x.Count - 1)) : 0;
        return new Summary(x.Count, mean, std, x.Min(), x.Max());
    }

    // скільки прогонів треба (нерівність чебишева)
    public static int RequiredReplications(double sigma, double epsilon,
        double beta = 0.95) =>
        (int)Math.Ceiling(sigma * sigma / (epsilon * epsilon * (1 - beta)));

    public static ChiSquareResult ChiSquareTest(IReadOnlyList<double> x,
        int bins, Func<double, double> cdf, int k, double alpha = 0.05)
    {
        double min = x.Min(), max = x.Max();
        double width = (max - min) / bins;
        var edges = Enumerable.Range(0, bins + 1)
            .Select(i => min + i * width).ToArray();
        var observed = new int[bins];
        foreach (double v in x)
            observed[Math.Min(bins - 1, (int)((v - min) / width))]++;
        var expected = new double[bins];
        for (int j = 0; j < bins; j++)
        {
            double lo = j == 0 ? double.NegativeInfinity : edges[j];
            double hi = j == bins - 1 ? double.PositiveInfinity : edges[j + 1];
            expected[j] = x.Count * (cdf(hi) - cdf(lo));
        }
        // зливаємо інтервали, де очікується менше 5
        var obs = new List<double>();
        var exp = new List<double>();
        double accO = 0, accE = 0;
        for (int j = 0; j < bins; j++)
        {
            accO += observed[j];
            accE += expected[j];
            if (accE >= 5)
            {
                obs.Add(accO);
                exp.Add(accE);
                accO = accE = 0;
            }
        }
        if (accE > 0 && exp.Count > 0)
        {
            obs[^1] += accO;
            exp[^1] += accE;
        }
        double chi2 = obs.Select((o, i) => (o - exp[i]) * (o - exp[i]) / exp[i])
            .Sum();
        int df = Math.Max(1, obs.Count - 1 - k);
        return new ChiSquareResult(chi2, df, ChiSquareQuantile(1 - alpha, df),
            1 - ChiSquareCdf(chi2, df), edges, observed, expected);
    }

    // мнк через нормальні рівняння
    public static RegressionResult LeastSquares(
        IReadOnlyList<double[]> features, IReadOnlyList<double> y)
    {
        int m = features[0].Length;
        var xtx = new double[m, m];
        var xty = new double[m];
        for (int i = 0; i < y.Count; i++)
            for (int a = 0; a < m; a++)
            {
                xty[a] += features[i][a] * y[i];
                for (int b = 0; b < m; b++)
                    xtx[a, b] += features[i][a] * features[i][b];
            }
        double[] coef = SolveLinear(xtx, xty);
        double mean = y.Average(), ssRes = 0, ssTot = 0;
        for (int i = 0; i < y.Count; i++)
        {
            double fit = 0;
            for (int a = 0; a < m; a++) fit += coef[a] * features[i][a];
            ssRes += (y[i] - fit) * (y[i] - fit);
            ssTot += (y[i] - mean) * (y[i] - mean);
        }
        // наскільки добре регресія лягає на точки
        double r = Math.Sqrt(Math.Max(0, 1 - ssRes / ssTot));
        return new RegressionResult(coef, r,
            Math.Sqrt(ssRes / Math.Max(1, y.Count - m)));
    }

    public static AnovaResult OneWayAnova(
        IReadOnlyList<IReadOnlyList<double>> groups, double alpha = 0.05)
    {
        int q = groups.Count, p = groups[0].Count;
        double grand = groups.SelectMany(g => g).Average();
        var means = groups.Select(g => g.Average()).ToArray();
        double ssFactor = p * means.Sum(m => (m - grand) * (m - grand));
        double ssResidual = groups
            .Select((g, j) => g.Sum(v => (v - means[j]) * (v - means[j])))
            .Sum();
        int df1 = q - 1, df2 = q * (p - 1);
        double dFactor = ssFactor / df1, dResidual = ssResidual / df2;
        return new AnovaResult(dFactor, dResidual, dFactor / dResidual,
            FisherQuantile(1 - alpha, df1, df2), df1, df2);
    }

    private static double[] SolveLinear(double[,] a, double[] b)
    {
        int n = b.Length;
        var m = (double[,])a.Clone();
        var x = (double[])b.Clone();
        for (int col = 0; col < n; col++)
        {
            int piv = col;
            for (int r = col + 1; r < n; r++)
                if (Math.Abs(m[r, col]) > Math.Abs(m[piv, col])) piv = r;
            for (int c = 0; c < n; c++)
                (m[col, c], m[piv, c]) = (m[piv, c], m[col, c]);
            (x[col], x[piv]) = (x[piv], x[col]);
            for (int r = 0; r < n; r++)
            {
                if (r == col) continue;
                double k = m[r, col] / m[col, col];
                for (int c = col; c < n; c++) m[r, c] -= k * m[col, c];
                x[r] -= k * x[col];
            }
        }
        for (int i = 0; i < n; i++) x[i] /= m[i, i];
        return x;
    }


    public static double NormalCdf(double x, double mean, double std) =>
        0.5 * (1 + Erf((x - mean) / (std * Math.Sqrt(2))));

    private static double Erf(double x)
    {
        // наближення з довідника абрамовіца – стіган
        double t = 1 / (1 + 0.3275911 * Math.Abs(x));
        double y = 1 - ((((1.061405429 * t - 1.453152027) * t
            + 1.421413741) * t - 0.284496736) * t + 0.254829592)
            * t * Math.Exp(-x * x);
        return x >= 0 ? y : -y;
    }

    private static double LogGamma(double x)
    {
        double[] c = [76.18009172947146, -86.50532032941677,
            24.01409824083091, -1.231739572450155,
            0.1208650973866179e-2, -0.5395239384953e-5];
        double y = x, tmp = x + 5.5;
        tmp -= (x + 0.5) * Math.Log(tmp);
        double ser = 1.000000000190015;
        foreach (double ci in c) ser += ci / ++y;
        return -tmp + Math.Log(2.5066282746310005 * ser / x);
    }

    // неповна гамма-функція, потрібна для хі-квадрат
    public static double RegularizedGammaP(double a, double x)
    {
        if (x <= 0) return 0;
        double front = Math.Exp(-x + a * Math.Log(x) - LogGamma(a));
        if (x < a + 1)
        {
            double sum = 1 / a, del = sum, ap = a;
            for (int n = 0; n < 500; n++)
            {
                del *= x / ++ap;
                sum += del;
                if (Math.Abs(del) < Math.Abs(sum) * 1e-14) break;
            }
            return sum * front;
        }
        double b = x + 1 - a, c = 1e300, d = 1 / b, h = d;
        for (int i = 1; i < 500; i++)
        {
            double an = -i * (i - a);
            b += 2;
            d = Guard(an * d + b);
            c = Guard(b + an / c);
            d = 1 / d;
            h *= d * c;
            if (Math.Abs(d * c - 1) < 1e-14) break;
        }
        return 1 - front * h;
    }

    // неповна бета-функція для стьюдента і фішера
    public static double RegularizedBeta(double x, double a, double b)
    {
        if (x <= 0) return 0;
        if (x >= 1) return 1;
        double bt = Math.Exp(LogGamma(a + b) - LogGamma(a) - LogGamma(b)
            + a * Math.Log(x) + b * Math.Log(1 - x));
        return x < (a + 1) / (a + b + 2)
            ? bt * BetaFraction(x, a, b) / a
            : 1 - bt * BetaFraction(1 - x, b, a) / b;
    }

    private static double BetaFraction(double x, double a, double b)
    {
        double c = 1, d = 1 / Guard(1 - (a + b) * x / (a + 1)), h = d;
        for (int m = 1; m <= 500; m++)
        {
            int m2 = 2 * m;
            double aa = m * (b - m) * x / ((a - 1 + m2) * (a + m2));
            d = 1 / Guard(1 + aa * d);
            c = Guard(1 + aa / c);
            h *= d * c;
            aa = -(a + m) * (a + b + m) * x / ((a + m2) * (a + 1 + m2));
            d = 1 / Guard(1 + aa * d);
            c = Guard(1 + aa / c);
            h *= d * c;
            if (Math.Abs(d * c - 1) < 1e-14) break;
        }
        return h;
    }

    private static double Guard(double v) =>
        Math.Abs(v) < 1e-300 ? 1e-300 : v;

    public static double ChiSquareCdf(double x, int df) =>
        RegularizedGammaP(df / 2.0, x / 2.0);

    public static double StudentCdf(double t, int df)
    {
        double ib = RegularizedBeta(df / (df + t * t), df / 2.0, 0.5);
        return t >= 0 ? 1 - 0.5 * ib : 0.5 * ib;
    }

    public static double FisherCdf(double f, int d1, int d2) => f <= 0 ? 0
        : RegularizedBeta(d1 * f / (d1 * f + d2), d1 / 2.0, d2 / 2.0);

    public static double ChiSquareQuantile(double p, int df) =>
        Bisect(x => ChiSquareCdf(x, df), p, 0, 1e4);

    public static double StudentQuantile(double p, int df) =>
        Bisect(x => StudentCdf(x, df), p, -1e3, 1e3);

    public static double FisherQuantile(double p, int d1, int d2) =>
        Bisect(x => FisherCdf(x, d1, d2), p, 0, 1e4);

    // квантиль шукаємо поділом навпіл
    private static double Bisect(Func<double, double> cdf, double p,
        double lo, double hi)
    {
        for (int i = 0; i < 200; i++)
        {
            double mid = (lo + hi) / 2;
            if (cdf(mid) < p) lo = mid; else hi = mid;
        }
        return (lo + hi) / 2;
    }
}
