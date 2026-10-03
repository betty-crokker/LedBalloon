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
bool useParallelI2S = false;
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

// ---- the wall clock, told rather than read ------------------------------------------------------
// Fixed, and settable, for the same reason the millisecond clock is: an effect asked for the same
// moment twice should draw the same thing. 2026-01-01 12:00:00 is an arbitrary but legible default.
time_t localTime = 1767268800;
bool useAMPM = false;

static std::tm broken(time_t t) {
  std::tm out{};
  time_t v = t;
#ifdef _WIN32
  gmtime_s(&out, &v);
#else
  gmtime_r(&v, &out);
#endif
  return out;
}

int hour(time_t t)   { return broken(t).tm_hour; }
int minute(time_t t) { return broken(t).tm_min; }
int second(time_t t) { return broken(t).tm_sec; }
int day(time_t t)    { return broken(t).tm_mday; }
int month(time_t t)  { return broken(t).tm_mon + 1; }
int year(time_t t)   { return broken(t).tm_year + 1900; }

const char* monthShortStr(uint8_t month) {
  static const char* names[] = {"Err", "Jan", "Feb", "Mar", "Apr", "May", "Jun",
                                "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"};
  return names[month <= 12 ? month : 0];
}

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
// Several strips from one I2S peripheral. finalizeInit's ESP32 branch only reaches this with more
// than one digital bus, and there is one, so it is never called - but it has to link.
void BusManager::useParallelOutput() {}
// One bus, reporting RGB over the whole run. Segment::refreshLightCapabilities walks the busses to
// decide whether a segment can show colour at all, and with no bus at all it decides it cannot -
// which makes color_from_palette hand back the colour slot instead of the palette.
class HostBus : public Bus {
 public:
  HostBus(uint16_t len) : Bus(TYPE_WS2812_RGB, 0, AW_GLOBAL_DISABLED, len) {
    _valid = true;

    // What the bus can show, which the base constructor does not set. It initialises _type, _bri,
    // _start, _len, _reversed, _valid, _needsRefresh and _data, and leaves these three alone,
    // because every real bus is a derived class that sets them itself from its own BusConfig. A bus
    // that skips that step does not report "no colour" - it reports whatever was in that memory.
    //
    // Which is worse than a wrong answer, because it is a different wrong answer in each process.
    // refreshLightCapabilities asks the bus whether it has RGB and records the segment as able to
    // show nothing when it says no; color_from_palette then gives up at its second line and returns
    // the colour slot for every pixel. So the whole run comes out one flat colour that no palette,
    // no effect and no amount of elapsed time can move - on the machines where the garbage happens
    // to be zero, and nowhere else. It read 105 here and 0 on the house's machine.
    _hasRgb   = Bus::hasRGB(TYPE_WS2812_RGB);
    _hasWhite = Bus::hasWhite(TYPE_WS2812_RGB);
    _hasCCT   = Bus::hasCCT(TYPE_WS2812_RGB);
  }
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

static uint32_t g_begins = 0;
static uint32_t g_refreshes = 0;

EXPORT void wled_begin(uint16_t count, uint8_t fps) {
  g_begins++;
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

  // Frame time is behaviour, not tuning. setTargetFps(0) is WLED's unlimited mode, which makes
  // FRAMETIME equal MIN_FRAME_DELAY - 2 ms on an ESP32 - and both controllers here run unlimited
  // (hw.led.fps is 0). Any other value changes the rate of every effect that counts frames or adds
  // FRAMETIME to a counter, which is most of them.
  strip.setTargetFps(fps);
}

// The effect list and its fxdata, straight from the engine. LedBalloon currently scrapes these from
// a controller over HTTP (/json/eff and /json/fxdata); here they come from the same table the
// firmware builds, so the app can know what an effect offers without a controller on the network.
EXPORT uint8_t wled_mode_count() { return strip.getModeCount(); }

EXPORT const char* wled_mode_data(uint8_t id) { return strip.getModeData(id); }

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

// The six controls behind the two sliders: custom1-3 and the three checkmarks. Several effects read
// nothing else - Palette's own fxdata asks for c1=0, o1=1, o2=0, o3=1 - so leaving them at whatever
// the Segment constructor chose makes those effects disagree with a controller for no reason that
// shows up anywhere. custom3 is five bits wide, which is behaviour: it saturates at 31.
EXPORT void wled_controls(uint8_t custom1, uint8_t custom2, uint8_t custom3,
                          uint8_t check1, uint8_t check2, uint8_t check3) {
  Segment& seg = strip.getSegment(0);
  seg.custom1 = custom1;
  seg.custom2 = custom2;
  seg.custom3 = custom3 > 31 ? 31 : custom3;
  seg.check1 = check1 != 0;
  seg.check2 = check2 != 0;
  seg.check3 = check3 != 0;
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

// How the segment is wired. reverse is not only a presentation detail: Segment::setPixelColor maps
// through it on the way to the bus, and mode_flow reads SEGMENT.reverse itself and applies it per
// zone - which no amount of reversing the finished output can imitate. South's roofline runs from
// the far end, so it renders with this set, and a comparison that leaves it clear is comparing two
// different effects.
EXPORT void wled_orientation(uint8_t reverse, uint8_t mirror) {
  Segment& seg = strip.getSegment(0);
  seg.reverse = reverse != 0;
  seg.mirror = mirror != 0;
}

// ---- rendering on behalf of a caller that keeps the state ---------------------------------------
// The app has many previews alive at once - a thumbnail per effect - and one shared WLED segment.
// Rather than give the engine a segment per preview, the caller hands in that preview's runtime
// before each frame and takes it back afterwards, so the engine holds nothing between calls. That
// is the same split LedBalloon's own EffectSegment already uses: it carries step, call, aux0 and
// aux1 for exactly this reason. The only piece it does not already have is WLED's per-segment data
// buffer, which effects allocate for themselves and which this passes through as bytes.
// Resizes the segment without resetting it.
//
// setGeometry would be the obvious call and is the wrong one: it marks the segment for reset, so a
// caller with a different length would wipe the runtime and the pixel buffer of whichever caller
// went before it. With many previews alive at once and each a different length, that is every
// frame. start and stop are public, and SEGLEN comes from them through virtualLength().
EXPORT void wled_length(uint16_t length) {
  Segment& seg = strip.getSegment(0);
  uint16_t stop = length > HOST_LEDS ? HOST_LEDS : length;

  if (seg.start == 0 && seg.stop == stop) return;

  seg.start = 0;
  seg.stop = stop;

  // All four bounds, not two. This stands in for setGeometry, and setGeometry sets startY and stopY
  // as well - it writes stopY = 1 unconditionally, even with 2D compiled out. That is not a detail
  // about matrices: refreshLightCapabilities walks the segment as `for (y = startY; y < stopY; y++)
  // for (x = start; x < stop; x++)`, so a stopY of 0 runs the body no times at all, leaves
  // segStopIdx at 0, and then every bus fails its `getStart() >= segStopIdx` test and the segment is
  // recorded as being able to show nothing.
  seg.startY = 0;
  seg.stopY = 1;

  // The bounds decide what the segment can show, and nothing else recomputes it. Segment capabilities
  // are worked out from the busses the segment covers, and the only place that happens is
  // refreshLightCapabilities - which setGeometry calls and this does not, because setGeometry marks
  // the segment for reset and would wipe whichever caller went before this one.
  //
  // So without this the capability byte is whatever wled_begin computed for bounds the segment then
  // never draws with again, and it is never corrected. That is not a cosmetic staleness: with the
  // RGB bit clear, color_from_palette gives up at its second line and returns the colour slot for
  // every pixel, so the whole run comes out one flat colour that no palette, no effect and no amount
  // of elapsed time can move.
  seg.refreshLightCapabilities();
  g_refreshes++;
}

// The caller's previous frame, which is part of its state: anything that fades or trails reads the
// buffer back through getPixelColor, and the engine's buffer belongs to whoever rendered last.
EXPORT void wled_pixels_set(const uint32_t* in, uint16_t count) {
  if (!in) return;
  for (uint16_t i = 0; i < count && i < HOST_LEDS; i++) g_leds[i] = in[i];
}

// A custom palette, as the app's palette editor holds it.
//
// The engine loads none of its own: loadCustomPalettes reads them off the controller's filesystem,
// which is not here. Without them a segment set to a custom palette falls back to palette 0, and
// palette 0 means "use the colour slot" - so a palette-driven effect on a custom palette came out
// as a flat wash of the primary colour, or black if the primary is black, while the house drew the
// palette properly. WLED addresses custom palettes as 255 minus the slot; this takes the slot.
EXPORT void wled_custom_palette(uint8_t slot, const uint32_t* entries) {
  if (!entries) return;
  if (strip.customPalettes.size() <= slot) strip.customPalettes.resize((size_t)slot + 1);

  CRGBPalette16& into = strip.customPalettes[slot];
  for (int i = 0; i < 16; i++) {
    into[i] = CRGB(R(entries[i]), G(entries[i]), B(entries[i]));
  }
}

// How many palettes the engine can actually draw, so a caller can avoid asking for one it cannot.
// Ids from here up to 245 index past the end of the gradient table: 71 reads one past it and
// segfaults, and the rest read whatever follows.
EXPORT uint8_t wled_palette_count() { return strip.getPaletteCount(); }

// What the segment thinks it can show: bit 0 RGB, bit 1 white, bit 2 CCT.
//
// Worth exporting because a segment that reports no RGB is not a drawing bug with a colour in it -
// color_from_palette gives up at its second line and hands back the colour slot for every pixel, so
// the whole run comes out one flat colour that never moves whatever the palette or the effect says.
// It is computed from the busses, once, and is exactly the kind of thing that is invisible from the
// pixels alone: a flat blue run looks the same whether the palette is solid or the capability is
// missing.
EXPORT uint8_t wled_capabilities() {
  if (g_begins == 0 || strip.getSegmentsNum() == 0) return 0;
  return strip.getSegment(0).getLightCapabilities();
}

// What the segment would actually give an effect for a palette index, through the same call the
// effects use.
//
// This exists because reading _capabilities from here does not answer the question. The byte came
// back as 0x73, 0xbb and 0x00 in three processes, and refreshLightCapabilities can only ever assign
// 0 to 7 - so whatever that read is reaching, it is not the field FX_fcn.cpp writes, and a reading
// taken from it means nothing. Asking color_from_palette is not a guess about memory: it is the
// answer the effect gets. Four samples that come back equal is a run that will be one flat colour
// whatever the palette says, and comparing them against the colour slot says which branch took it.
// Walks refreshLightCapabilities' own reasoning, step by step, and reports what each test saw.
//
// It computes zero for a segment whose bounds and bus both look right from here, so the useful thing
// is no longer the answer but which line reaches it: the bus that breaks the loop, the overlap test
// that rejects it, or an index range that never gets built.
EXPORT void wled_capability_trace(uint32_t* out) {
  if (!out) return;
  for (int i = 0; i < 12; i++) out[i] = 0;
  if (g_begins == 0 || strip.getSegmentsNum() == 0) return;

  Segment& seg = strip.getSegment(0);

  unsigned segStartIdx = 0xFFFFU;
  unsigned segStopIdx  = 0;

  out[0] = seg.isActive() ? 1 : 0;
  out[1] = (uint32_t)(Segment::maxWidth * Segment::maxHeight);

  if (seg.start < Segment::maxWidth * Segment::maxHeight) {
    for (int y = seg.startY; y < seg.stopY; y++) for (int x = seg.start; x < seg.stop; x++) {
      unsigned index = strip.getMappedPixelIndex(x + Segment::maxWidth * y);
      if (index < 0xFFFFU) {
        if (segStartIdx > index) segStartIdx = index;
        if (segStopIdx  < index) segStopIdx  = index;
      }
      if (segStartIdx == segStopIdx) segStopIdx++;
    }
    out[2] = 1;             // took the mapped branch
  } else {
    segStartIdx = seg.start;
    segStopIdx  = seg.stop;
  }

  out[3] = segStartIdx;
  out[4] = segStopIdx;
  out[5] = BusManager::getNumBusses();

  Bus* bus = BusManager::getBus(0);
  if (bus == nullptr) { out[6] = 0xFFFFFFFFu; return; }

  out[6] = bus->getLength();
  out[7] = bus->isOk() ? 1 : 0;
  out[8] = bus->getStart();
  out[9] = bus->hasRGB() ? 1 : 0;
  out[10] = (bus->getStart() >= segStopIdx) ? 1 : 0;                        // rejected as past the end
  out[11] = (bus->getStart() + bus->getLength() <= segStartIdx) ? 1 : 0;    // rejected as before the start
}

EXPORT uint32_t wled_palette_sample(uint8_t index) {
  if (g_begins == 0 || strip.getSegmentsNum() == 0) return 0;
  return strip.getSegment(0).color_from_palette(index, false, true, 255);
}

// Everything refreshLightCapabilities reads, so a caps of zero can be explained rather than guessed
// at. It is computed once, in wled_begin, and never again - so whatever it decided there is what
// every effect is drawn with for the rest of the process.
EXPORT void wled_probe(uint32_t* out) {
  if (!out) return;

  // Before wled_begin there are no segments at all, and getSegment(0) on an empty vector takes the
  // process down - which a diagnostic has no business doing.
  if (g_begins == 0 || strip.getSegmentsNum() == 0) {
    for (int i = 0; i < 11; i++) out[i] = 0;
    return;
  }

  Segment& seg = strip.getSegment(0);
  out[0] = g_begins;
  out[1] = BusManager::getNumBusses();
  out[2] = seg.start;
  out[3] = seg.stop;
  out[4] = seg.getLightCapabilities();
  out[5] = Segment::maxWidth;
  out[6] = strip.getLengthTotal();
  out[7] = g_bus ? (uint32_t)(g_bus->isOk() ? g_bus->getLength() : 0) : 0xFFFFFFFFu;
  out[8] = g_refreshes;
  out[9] = seg.startY;
  out[10] = seg.stopY;
}

EXPORT void wled_runtime_set(uint16_t aux0, uint16_t aux1, uint32_t step, uint32_t call,
                             const uint8_t* data, uint16_t len) {
  Segment& seg = strip.getSegment(0);
  seg.aux0 = aux0;
  seg.aux1 = aux1;
  seg.step = step;
  // call is set before allocateData below, deliberately: allocateData wipes the buffer when call is
  // zero, which is how an effect initialises itself on its first frame. Restoring a non-zero call
  // first is what makes a resumed frame resume rather than start again.
  seg.call = call;
  // The segment asks not to be redrawn until next_time; the caller decides when a frame happens.
  seg.next_time = 0;

  if (data && len > 0 && seg.allocateData(len)) {
    memcpy(seg.data, data, len);
  }
}

EXPORT void wled_runtime_get(uint16_t* aux0, uint16_t* aux1, uint32_t* step, uint32_t* call,
                             uint8_t* data, uint16_t capacity, uint16_t* len) {
  Segment& seg = strip.getSegment(0);
  if (aux0) *aux0 = seg.aux0;
  if (aux1) *aux1 = seg.aux1;
  if (step) *step = seg.step;
  if (call) *call = seg.call;

  uint16_t have = seg.data ? seg.dataSize() : 0;
  if (len) *len = have;
  if (data && have > 0 && have <= capacity) {
    memcpy(data, seg.data, have);
  }
}

// Draws one frame at `now`.
//
// Two constraints pull against each other. service() refuses to draw twice within MIN_FRAME_DELAY
// of its last show, measured on the clock it reads for itself - so fifty previews all asking for the
// same millisecond would get one frame and forty-nine blanks. But the clock it reads is also the one
// the effects read: FastLED's beat family goes through GET_MILLIS, which is millis() and not
// strip.now, so moving that clock out from under them to satisfy the gate changes what they draw.
// Lake and Plasma both use beatsin8_t and both came out wrong when this shifted timebase instead.
//
// So the clock is only ever nudged as far as the gate needs. A caller whose own clock advances
// normally - any preview stepping at a frame time - gets exactly the millisecond it asked for. Only
// callers that collide on the same millisecond are pushed apart, by three of them, which is
// invisible in a preview and does not accumulate for the caller that keeps moving.
EXPORT void wled_render(uint32_t now, uint16_t count, uint32_t* out) {
  Segment& seg = strip.getSegment(0);
  uint32_t before = seg.call;

  g_millis = now;
  strip.timebase = 0;
  strip.service();

  // service() declines to draw within MIN_FRAME_DELAY of its own last draw, and the counter it
  // measures against is private. Mostly that does not bite: the comparison is unsigned, so a caller
  // whose clock is behind the last one passes by underflow. The case it does bite is two callers on
  // the identical millisecond, which is exactly what a screenful of previews stepping in lockstep
  // does - and the second would be handed back its own previous frame for ever.
  //
  // So: if it declined, ask again three milliseconds later. Both callers then draw every frame, and
  // the cost is that one of them is up to three milliseconds late. Which one depends on the order
  // they arrive in, so a caller's clock is not quite its own - but three milliseconds is a fifth of
  // a frame, and the alternative is a preview that never moves.
  if (seg.call == before) {
    g_millis = now + MIN_FRAME_DELAY + 1;
    strip.service();
  }

  for (uint16_t i = 0; i < count && i < HOST_LEDS; i++) out[i] = g_leds[i];
}

// Diagnostic: the clock the effects actually see. strip.now is nowUp + timebase, and WLED re-pins
// timebase when a segment resets, so an effect can be handed a clock that never advances.
EXPORT uint32_t wled_now() { return (uint32_t)strip.now; }

EXPORT void wled_frame(uint32_t now, uint16_t count, uint32_t* out) {
  g_millis = now;
  strip.now = now;
  strip.service();
  for (uint16_t i = 0; i < count && i < HOST_LEDS; i++) out[i] = g_leds[i];
}
}
