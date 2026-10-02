#pragma once
// MSVC has no __attribute__, and FastLED's headers are full of it. /D cannot express a
// function-like macro on MSVC's command line, so it goes here instead.
#ifdef _MSC_VER
#define __attribute__(x)
#endif
#include <cstdint>
#include <cstring>
#include <cstdio>

// ESP-IDF's version macros, so util.cpp's #if guards evaluate instead of erroring.
#define ESP_IDF_VERSION_VAL(a, b, c) (((a) << 16) | ((b) << 8) | (c))
#define ESP_IDF_VERSION ESP_IDF_VERSION_VAL(4, 4, 0)
#include <cmath>
#include <cstdlib>
#include <algorithm>
typedef uint8_t byte;
typedef bool boolean;
#define PROGMEM
#define PGM_P const char*
#define pgm_read_byte(a) (*(const uint8_t*)(a))
#define pgm_read_word(a) (*(const uint16_t*)(a))
// On AVR/ESP a flash pointer is 32 bits, and WLED reads pointers out of PROGMEM tables with
// pgm_read_dword - see FX_fcn.cpp:258, which casts the result straight back to byte*. Here a pointer
// is 64 bits, so reading one as a dword truncates the address. Read a pointer-sized word instead.
#define pgm_read_dword(a) (*(const uintptr_t*)(a))
#define memcpy_P memcpy
#define strncmp_P strncmp
#define PSTR(s) (s)
#define F(s) (s)
#define IRAM_ATTR

unsigned long millis();
unsigned long micros();
long random(long);
long random(long, long);
// Templates rather than Arduino's macros: the macro form rewrites std::min into std::std::min
// the moment any header reaches for the real one.
template <typename A, typename B> inline A min(A a, B b) { return a < (A)b ? a : (A)b; }
template <typename A, typename B> inline A max(A a, B b) { return a > (A)b ? a : (A)b; }
#define constrain(x,l,h) ((x)<(l)?(l):((x)>(h)?(h):(x)))
#define map(x,a,b,c,d) (((x)-(a))*((d)-(c))/((b)-(a))+(c))
class String {
  const char* _s;
 public:
  String() : _s("") {}
  String(const char* s) : _s(s ? s : "") {}
  const char* c_str() const { return _s; }
  unsigned length() const { return strlen(_s); }
  bool operator==(const String& o) const { return strcmp(_s, o._s) == 0; }
};

// Opaque placeholders for the things fcn_declare.h names in declarations the effects never call:
// the web server, websockets, JSON, E1.31, Alexa, Art-Net. Nothing here needs a body - FX.cpp only
// has to be able to parse the declarations it is handed.
#ifndef M_PI
#define M_PI 3.14159265358979323846
#endif
#define M_TWOPI (2.0 * M_PI)
#ifndef M_PI_2
#define M_PI_2 (M_PI / 2)
#endif
#ifndef M_PI_4
#define M_PI_4 (M_PI / 4)
#endif
#define bitRead(v, b) (((v) >> (b)) & 1)
#define bitWrite(v, b, x) ((x) ? ((v) |= (1UL << (b))) : ((v) &= ~(1UL << (b))))

class Print { public: void print(...) {} void println(...) {} };
class IPAddress { public: IPAddress() {} IPAddress(uint32_t) {} };
class JsonArray;
class JsonVariant {
 public:
  bool isNull() const { return true; }
  template <typename T> T as() const { return T(); }
  template <typename T> bool is() const { return false; }
  operator int() const { return 0; }
  template <typename T> bool operator<(T) const { return false; }
};
class JsonObject {
 public:
  JsonVariant operator[](const char*) const { return JsonVariant(); }
  bool isNull() const { return true; }
  template <typename T> void createNestedArray(T) {}
};
class JsonArray {
 public:
  JsonArray() {}
  JsonArray(const JsonVariant&) {}
  bool isNull() const { return true; }
  size_t size() const { return 0; }
  template <typename I> JsonVariant operator[](I) const { return JsonVariant(); }
  template <typename T> void add(T) {}
};
class JsonDocument {};
class AsyncWebServerRequest {};
class AsyncWebSocket {};
class AsyncWebSocketClient {};
class AsyncClient {};
class EspalexaDevice {};
struct e131_packet_t {};
struct ArtPollReply {};
typedef int AwsEventType;
typedef int WiFiEvent_t;
void oappend_shim(const char*);
extern char settingsScript[];
