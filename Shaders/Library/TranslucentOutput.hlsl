#ifndef INFINITY_TRANSLUCENT_OUTPUT_INCLUDED
#define INFINITY_TRANSLUCENT_OUTPUT_INCLUDED
// Translucent color stages write color, the optional reactive mask and motion.
#if defined(INFINITY_WRITE_REACTIVE_MASK)
#define INFINITY_MOTION_TARGET SV_Target2
#else
#define INFINITY_MOTION_TARGET SV_Target1
#endif
#endif
