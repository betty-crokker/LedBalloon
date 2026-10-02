#pragma once
// WLED's own globals and debug macros, with the types taken from wled.h rather than guessed.
// These are declarations only: the effects read them, nothing here decides what they contain.
#include <vector>
#include <ctime>

#define DEBUG_PRINT(x)
#define DEBUG_PRINTLN(x)
#define DEBUG_PRINTF(...)
#define DEBUG_PRINTF_P(...)
#define yield()
#define sprintf_P sprintf
#define strcpy_P strcpy
#define strcat_P strcat
#define strlcpy(d, s, n) (strncpy((d), (s), (n)), strlen(s))
#define GPIO_PIN_COUNT 48
#define WLED_FS stubFs

struct StubFs { bool exists(const char*) { return false; } };
extern StubFs stubFs;

struct EspStub {
  uint32_t getFreeHeap() { return 100000; }
  uint32_t getChipId() { return 0; }
  void restart() {}
};
extern EspStub ESP;

template <size_t N> class StaticJsonDocument {
 public:
  JsonObject to() { return JsonObject(); }
  template <typename T> T as() { return T(); }
  JsonVariant operator[](const char*) { return JsonVariant(); }
  void clear() {}
};
bool readObjectFromFile(const char*, const char*, void*);
template <size_t N> bool readObjectFromFile(const char* f, std::nullptr_t, StaticJsonDocument<N>*) { return false; }

extern bool fadeTransition;
extern bool modeBlending;
extern bool gammaCorrectBri;
extern uint8_t randomPaletteChangeTime;
extern bool useHarmonicRandomPalette;
extern bool useGlobalLedBuffer;
extern bool stateChanged;
extern bool realtimeRespectLedMaps;
extern uint8_t lastRandomIndex;
extern bool gammaCorrectCol;
extern uint16_t currentLedmap;
extern byte interfaceUpdateCallMode;
struct CHSV;
CHSV rgb2hsv_approximate(const CRGB&);
extern std::vector<BusConfig> busConfigs;
extern StaticJsonDocument<4096>* pDoc;

// ---- the wall clock ---------------------------------------------------------------------------
// WLED's clock effects (Analog Clock among them) read the local time through the Time library.
// Told rather than read, like the millisecond clock: an effect asked for the same moment twice
// draws the same thing, which a real clock would not give.
extern time_t localTime;
extern bool useAMPM;

int hour(time_t t);
int minute(time_t t);
int second(time_t t);
int day(time_t t);
int month(time_t t);
int year(time_t t);
const char* monthShortStr(uint8_t month);
