#pragma once
// ESP-IDF's LEDC (PWM) driver, stood in for.
//
// pin_manager.h includes it for the analog channel counts and nothing else; const.h computes
// WLED_MAX_ANALOG_CHANNELS from these two. No analog or PWM bus exists here - the bus is an array -
// so nothing is driven through it. The values are the real ESP32's: 8 channels across 2 speed modes.
#define LEDC_CHANNEL_MAX    8
#define LEDC_SPEED_MODE_MAX 2
