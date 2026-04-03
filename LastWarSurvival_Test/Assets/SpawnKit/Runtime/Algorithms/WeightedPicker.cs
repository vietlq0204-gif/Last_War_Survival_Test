namespace Vit.SpawnKit.Algorithms
{
public static class WeightedPicker
{
    /// <summary>
    /// Picks an index using deterministic weighted randomness.
    /// </summary>
    public static int PickIndex(float[] weights, uint seed, uint salt)
    {
        float sum = 0f;
        for (int i = 0; i < weights.Length; i++) sum += weights[i];
        if (sum <= 0f) return 0;

        uint hash = Hash(seed ^ salt);
        float r01 = (hash >> 8) * (1f / 16777216f);
        float value = r01 * sum;

        float acc = 0f;
        for (int i = 0; i < weights.Length; i++)
        {
            acc += weights[i];
            if (value <= acc) return i;
        }

        return weights.Length - 1;
    }

    private static uint Hash(uint x)
    {
        x ^= x >> 16;
        x *= 0x7feb352du;
        x ^= x >> 15;
        x *= 0x846ca68bu;
        x ^= x >> 16;
        return x == 0 ? 1u : x;
    }
}
}
