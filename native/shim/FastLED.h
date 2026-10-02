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

#include "lib8tion.h"
#include "pixeltypes.h"
#include "colorutils.h"
#include "colorpalettes.h"
#include "noise.h"
