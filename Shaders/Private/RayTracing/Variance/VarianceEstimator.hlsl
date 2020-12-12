#ifndef _VarianceEstimator_
#define _VarianceEstimator_

struct VarianceEstimator
{
    int num;
    float oldM;
    float oldS;
    float newM;
    float newS;
};

void InitializeVarianceEstimator(out VarianceEstimator variance)
{
    variance.num = 0;
    variance.oldM = 0;
    variance.oldS = 0;
    variance.newM = 0;
    variance.newS = 0;
}

void PushValue(inout VarianceEstimator variance, float value)
{
    variance.num++;

    if (variance.num == 1) {
        variance.oldM = variance.newM = value;
        variance.oldS = 0;
    } else {
        variance.newM = variance.oldM + (value - variance.oldM) / variance.num;
        variance.newS = variance.oldS + (value - variance.oldM) * (value - variance.newM);
        variance.oldM = variance.newM;
        variance.oldS = variance.newS;
    }
}

float GetVariance(in VarianceEstimator variance)
{
    return ( variance.num > 1 ? max(0.0001, variance.newS) / max(0.0001, (variance.num - 1) ) : 0 );
}

#endif