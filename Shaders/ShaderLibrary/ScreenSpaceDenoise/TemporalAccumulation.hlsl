#ifndef INFINITY_SCREEN_SPACE_TEMPORAL_ACCUMULATION
#define INFINITY_SCREEN_SPACE_TEMPORAL_ACCUMULATION
// Temporal accumulation shared by the screen-space signals (SSR; SSGI moves onto it in 0.5.0 S14).
// History is stored with its age in frames. A pixel blends the current sample with weight
// max(1 - maxHistoryWeight, 1 / (age + 1)): a running mean right after a reset, then the configured exponential
// weight. A rejected history restarts at age 0, so the current sample is taken as is.

struct FNeighborhoodMoments
{
    float4 mean;
    float4 minimum;
    float4 maximum;
};

// Mean and stddev of the samples, widened to include the center so the clip box never excludes the current value.
FNeighborhoodMoments ResolveNeighborhoodMoments(float4 sum, float4 sumSquares, float count, float4 center, float clampScale)
{
    FNeighborhoodMoments moments;
    moments.mean = sum / count;
    float4 stddev = sqrt(max(0, sumSquares / count - moments.mean * moments.mean));
    moments.minimum = min(moments.mean - clampScale * stddev, center);
    moments.maximum = max(moments.mean + clampScale * stddev, center);
    return moments;
}

// Variance clipping (Salvi 2016): moves the history toward the box center until it lies inside, instead of clamping
// each channel independently, which shifts hue.
float4 ClipHistoryToNeighborhood(float4 history, FNeighborhoodMoments moments)
{
    float4 center = 0.5 * (moments.maximum + moments.minimum);
    float4 extents = max(0.5 * (moments.maximum - moments.minimum), 1e-5);
    float4 offset = history - center;
    float4 unit = abs(offset / extents);
    float maxUnit = max(max(unit.x, unit.y), max(unit.z, unit.w));
    return maxUnit > 1 ? center + offset / maxUnit : history;
}

// Normalized distance of a history candidate from the neighborhood, used to choose between reprojection candidates.
float NeighborhoodDistance(float4 value, FNeighborhoodMoments moments)
{
    float4 extents = max(0.5 * (moments.maximum - moments.minimum), 1e-4);
    float4 unit = abs(value - moments.mean) / extents;
    return max(max(unit.x, unit.y), max(unit.z, unit.w));
}

float TemporalCurrentWeight(float historyAge, float maxHistoryWeight)
{
    return max(1.0 - maxHistoryWeight, rcp(historyAge + 1.0));
}

#define SCREEN_SPACE_MAX_HISTORY_AGE 64.0

float NextHistoryAge(float historyAge)
{
    return min(historyAge + 1.0, SCREEN_SPACE_MAX_HISTORY_AGE);
}
#endif
