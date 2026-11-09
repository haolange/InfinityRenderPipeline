#ifndef INFINITY_HIZ
#define INFINITY_HIZ
// Virtual level zero is full-resolution surface depth. Level N>0 is mip N-1
// of a ceil(fullSize/2) hierarchy. The last cell owns any trailing odd texels.
uint2 HiZLevelSize(uint2 fullSize, uint level)
{
    return level == 0 ? fullSize : max(uint2(1, 1), ((fullSize + 1) / 2) >> (level - 1));
}
uint2 HiZCell(float2 uv, uint2 fullSize, uint level)
{
    return min((uint2)(uv * fullSize / (1u << level)), HiZLevelSize(fullSize, level) - 1);
}
void HiZCellBounds(uint2 cell, uint2 fullSize, uint level, out float2 lower, out float2 upper)
{
    uint2 size = HiZLevelSize(fullSize, level);
    float scale = (float)(1u << level);
    lower = cell * scale / fullSize;
    upper = min((cell + 1) * scale / fullSize, 1);
    if (cell.x == size.x - 1) upper.x = 1;
    if (cell.y == size.y - 1) upper.y = 1;
}
#endif
