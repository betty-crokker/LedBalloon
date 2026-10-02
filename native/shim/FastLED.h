#pragma once
// The real FastLED headers, not a stand-in. Only the parts the effects read: no controllers, no
// SPI, no platform layer - those are what make FastLED awkward to build off a microcontroller, and
// no effect touches them.
#include "Arduino.h"

#define FASTLED_NAMESPACE_BEGIN
#define FASTLED_NAMESPACE_END
#define FASTLED_USING_NAMESPACE
#define LIB8STATIC static inline
#define LIB8STATIC_ALWAYS_INLINE static inline
#define FASTLED_FORCE_INLINE inline
#define CFASTLED_H
#define __INC_LED_SYSDEFS_H
#define FASTLED_INTERNAL

// FastLED's own defaults, which live in fastled_config.h and are pulled in by FastLED.h:50. This
// file stands in for FastLED.h, so without this include none of them are set - and they are not
// preferences, they are behaviour: FASTLED_NOISE_FIXED picks which easing inoise uses, and
// FASTLED_SCALE8_FIXED picks whether scale8 is (i*sc)>>8 or (i*(1+sc))>>8. Undefined, every
// "#if FASTLED_x_FIXED == 1" silently took the old branch.
#include "fastled_config.h"

#include "lib8tion.h"
#include "pixeltypes.h"
#include "colorutils.h"
#include "colorpalettes.h"
#include "noise.h"
