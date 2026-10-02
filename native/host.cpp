// The hardware, replaced by an array.
//
// Everything under vendor/ is WLED's or FastLED's own source, unmodified - verify-vendor.sh checks
// that against the upstream tags. This is the floor it stands on: the
// globals it reads, a clock it can be told the time by, and a BusManager that writes pixels into
// memory instead of out of a GPIO pin. That last part is the whole of the "hardware coupling" the
// effects were supposed to have.
#include "wled.h"
#include <vector>

// ---- the globals WLED declares in wled.h ------------------------------------------------------
WS2812FX strip;
std::vector<BusConfig> busConfigs;
StaticJsonDocument<4096>* pDoc = nullptr;
StubFs stubFs;
EspStub ESP;

bool fadeTransition = true;
bool modeBlending = true;
bool gammaCorrectBri = false;
bool gammaCorrectCol = true;
uint8_t randomPaletteChangeTime = 5;
bool useHarmonicRandomPalette = true;
bool useGlobalLedBuffer = false;
bool stateChanged = false;
bool realtimeRespectLedMaps = false;
byte realtimeMode = 0;
bool realtimeOverride = false;
uint32_t lastRedraw = 0;
byte errorFlag = 0;
uint16_t currentLedmap = 0;
byte interfaceUpdateCallMode = 0;
uint8_t lastRandomIndex = 0;
char settingsScript[1] = {0};
char versionString[1] = {0};

void oappend_shim(const char*) {}
bool readObjectFromFile(const char*, const char*, void*) { return false; }

// ---- the clock --------------------------------------------------------------------------------
// Told rather than read, so a frame can be asked for at an exact millisecond and the answer is
// reproducible. On the controller this is the chip's own counter.
static uint32_t g_millis = 0;
unsigned long millis() { return g_millis; }
unsigned long micros() { return g_millis * 1000UL; }
uint32_t get_millisecond_timer() { return g_millis; }

long random(long howbig) { return howbig ? (long)(rand() % howbig) : 0; }
long random(long howsmall, long howbig) { return howsmall + random(howbig - howsmall); }

float mapf(float x, float in_min, float in_max, float out_min, float out_max) {
  return (x - in_min) * (out_max - out_min) / (in_max - in_min) + out_min;
}

// ---- the parts of WLED that are not effects ----------------------------------------------------
void enumerateLedmaps() {}
int16_t extractModeDefaults(uint8_t, const char*) { return -1; }
// ---- the microphone, replaced by numbers from the host ----------------------------------------
// The nine slots WLED's audio-reactive effects read (FX.cpp:6285-6293). On the controller these come
// from the audioreactive usermod's I2S FFT; here the host sets them, so the effects can be run
// without a microphone - and reproducibly, which a real microphone is not.
static float   g_volumeSmth = 0.0f;
static float   g_volumeRaw  = 0.0f;
static uint8_t g_fftResult[16] = {0};
static uint8_t g_samplePeak = 0;
static float   g_majorPeak  = 1.0f;
static float   g_magnitude  = 0.0f;
static uint8_t g_maxVol     = 10;
static uint8_t g_binNum     = 8;
static float   g_fftBin[256] = {0};

um_data_t* simulateSound(uint8_t) {
  static um_data_t  data;
  static um_types_t types[9] = {UMT_FLOAT, UMT_FLOAT, UMT_BYTE_ARR, UMT_BYTE,
                                UMT_FLOAT, UMT_FLOAT, UMT_BYTE, UMT_BYTE, UMT_FLOAT_ARR};
  static void*      slots[9];
  slots[0] = &g_volumeSmth; slots[1] = &g_volumeRaw;  slots[2] = g_fftResult;
  slots[3] = &g_samplePeak; slots[4] = &g_majorPeak;  slots[5] = &g_magnitude;
  slots[6] = &g_maxVol;     slots[7] = &g_binNum;     slots[8] = g_fftBin;
  data.u_size = 9;
  data.u_type = types;
  data.u_data = slots;
  return &data;
}
uint16_t XY(uint8_t x, uint8_t y) { return x; }
void WS2812FX::setUpMatrix() {}

bool UsermodManager::getUMData(um_data_t**, uint8_t) { return false; }

bool PinManager::isPinAllocated(byte, PinOwner) { return false; }
bool PinManager::isPinOk(byte, bool) { return true; }

uint8_t get_random_wheel_index(uint8_t pos) {
  return (uint8_t)(pos + 42);
}

// ---- the bus: an array, not a GPIO pin ---------------------------------------------------------
#define HOST_LEDS 2048
static uint32_t g_leds[HOST_LEDS];
static uint8_t g_brightness = 255;

uint8_t Bus::_gAWM = 255;
int16_t Bus::_cct = -1;
uint8_t Bus::_cctBlend = 0;

std::vector<Bus*> BusManager::busses;
uint16_t BusManager::_milliAmpsUsed = 0;
uint16_t BusManager::_milliAmpsMax = 0;

void BusManager::setPixelColor(unsigned pix, uint32_t c) { if (pix < HOST_LEDS) g_leds[pix] = c; }
uint32_t BusManager::getPixelColor(unsigned pix) { return pix < HOST_LEDS ? g_leds[pix] : 0; }
void BusManager::setBrightness(uint8_t b) { g_brightness = b; }
void BusManager::setSegmentCCT(int16_t, bool) {}
void BusManager::show() {}
bool BusManager::canAllShow() { return true; }
// One bus, reporting RGB over the whole run. Segment::refreshLightCapabilities walks the busses to
// decide whether a segment can show colour at all, and with no bus at all it decides it cannot -
// which makes color_from_palette hand back the colour slot instead of the palette.
class HostBus : public Bus {
 public:
  HostBus(uint16_t len) : Bus(TYPE_WS2812_RGB, 0, AW_GLOBAL_DISABLED, len) { _valid = true; }
  void show() override {}
  void setPixelColor(unsigned pix, uint32_t c) override { if (pix < HOST_LEDS) g_leds[pix] = c; }
  uint32_t getPixelColor(unsigned pix) const override { return pix < HOST_LEDS ? g_leds[pix] : 0; }
};

static HostBus* g_bus = nullptr;
Bus* BusManager::getBus(uint8_t busNr) { return busNr == 0 ? (Bus*)g_bus : nullptr; }
int BusManager::add(const BusConfig&) { busses.push_back((Bus*)g_bus); return 0; }
uint32_t BusConfig::memUsage(unsigned) const { return 0; }

// ---- what C# calls ------------------------------------------------------------------------------
extern "C" {
#ifdef _WIN32
#define EXPORT __declspec(dllexport)
#else
#define EXPORT __attribute__((visibility("default")))
#endif

EXPORT void wled_begin(uint16_t count) {
  memset(g_leds, 0, sizeof(g_leds));

  // WLED's own init. _length is private and only finalizeInit() sets it, and WS2812FX::setPixelColor
  // drops every index at or past it - so skipping this is what limited the output to the 30 pixels of
  // DEFAULT_LED_COUNT. busConfigs is seeded first; with it empty, finalizeInit walks its default-bus
  // block looking for usable GPIO pins, which is the part that has no answer here.
  if (!g_bus) {
    g_bus = new HostBus(count);
    uint8_t pins[1] = {16};
    busConfigs.emplace_back(TYPE_WS2812_RGB, pins, 0, count);
  }
  strip.finalizeInit();

  strip.resetSegments();
  strip.getSegment(0).setGeometry(0, count);
  strip.getSegment(0).on = true;
  strip.getSegment(0).opacity = 255;
  strip.getSegment(0).refreshLightCapabilities();
  strip.setBrightness(255, true);
  strip.setTargetFps(42);
}

// The effect list and its fxdata, straight from the engine. LedBalloon currently scrapes these from
// a controller over HTTP (/json/eff and /json/fxdata); here they come from the same table the
// firmware builds, so the app can know what an effect offers without a controller on the network.
EXPORT uint8_t wled_mode_count() { return strip.getModeCount(); }

EXPORT const char* wled_mode_data(uint8_t id) { return strip.getModeData(id); }

EXPORT uint8_t wled_palette_count() { return strip.getPaletteCount(); }

EXPORT void wled_segment(uint8_t fx, uint8_t pal, uint8_t speed, uint8_t intensity,
                         uint32_t c0, uint32_t c1, uint32_t c2) {
  Segment& seg = strip.getSegment(0);
  seg.mode = fx;
  seg.palette = pal;
  seg.speed = speed;
  seg.intensity = intensity;
  seg.colors[0] = c0;
  seg.colors[1] = c1;
  seg.colors[2] = c2;

  // Normally done by the service loop when a segment changes; loadPalette turns the palette id into
  // the sixteen entries ColorFromPalette reads.
  seg.setCurrentPalette();
}

// Mirrors LedBalloon's AudioFrame: one volume, sixteen bins, a peak frequency and a beat flag.
EXPORT void wled_audio(float volume, const uint8_t* bins, float majorPeakHz, float magnitude,
                       uint8_t beat) {
  g_volumeSmth = volume;
  g_volumeRaw  = volume;
  g_samplePeak = beat;
  g_majorPeak  = majorPeakHz;
  g_magnitude  = magnitude;
  if (bins) for (int i = 0; i < 16; i++) {
    g_fftResult[i] = bins[i];
    g_fftBin[i]    = (float)bins[i] * 8.0f;
  }
}

EXPORT void wled_frame(uint32_t now, uint16_t count, uint32_t* out) {
  g_millis = now;
  strip.now = now;
  strip.service();
  for (uint16_t i = 0; i < count && i < HOST_LEDS; i++) out[i] = g_leds[i];
}
}
