#pragma once
#include "Arduino.h"
#include "FastLED.h"
#include "const.h"

// wled.h's own colour channel macros.
#define R(c) (uint8_t((c) >> 16))
#define G(c) (uint8_t((c) >> 8))
#define B(c) (uint8_t(c))
#define W(c) (uint8_t((c) >> 24))
#define RGBW32(r,g,b,w) (uint32_t((uint8_t(w) << 24) | (uint8_t(r) << 16) | (uint8_t(g) << 8) | (uint8_t(b))))

#include "fcn_declare.h"
#define GPIO_PIN_COUNT 48
#include "pin_manager.h"
#include "bus_manager.h"
#include "FX.h"

// wled.h declares these after FX.h; the effects reach for them through the SEGMENT/SEGLEN macros.
extern WS2812FX strip;
extern byte realtimeMode;
extern bool realtimeOverride;
extern uint32_t lastRedraw;
extern byte errorFlag;
extern char versionString[];
#include "globals.h"
