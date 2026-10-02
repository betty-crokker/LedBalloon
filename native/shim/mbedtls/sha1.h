#pragma once
#include <cstdint>
typedef struct { int dummy; } mbedtls_sha1_context;
inline void mbedtls_sha1_init(mbedtls_sha1_context*) {}
inline void mbedtls_sha1_free(mbedtls_sha1_context*) {}
inline int mbedtls_sha1_starts(mbedtls_sha1_context*) { return 0; }
inline int mbedtls_sha1_update(mbedtls_sha1_context*, const unsigned char*, size_t) { return 0; }
inline int mbedtls_sha1_finish(mbedtls_sha1_context*, unsigned char*) { return 0; }
