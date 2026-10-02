#!/bin/bash
# Checks that every vendored file is byte-identical to the upstream tag it claims to come from.
#
# The point of this directory is that none of the effect code was retyped, transliterated or
# understood - it is WLED's and FastLED's own source, compiled. That claim is only worth anything if
# it can be checked, so this checks it. Needs `gh` authenticated; nothing else.
#
#   bash native/verify-vendor.sh
cd "$(dirname "$0")"

fail=0
check() {  # check <repo> <tag> <upstream-path> <local-path>
  want=$(gh api "repos/$1/contents/$3?ref=$2" --jq .content 2>/dev/null | base64 -d | sha256sum | cut -c1-64)
  got=$(sha256sum "$4" | cut -c1-64)
  if [ -z "$want" ]; then
    printf '  ?? %-44s could not reach upstream\n' "$4"; fail=1
  elif [ "$want" = "$got" ]; then
    printf '  ok %-44s %s\n' "$4" "${got:0:12}"
  else
    printf '  XX %-44s local %s, upstream %s\n' "$4" "${got:0:12}" "${want:0:12}"; fail=1
  fi
}

echo "WLED 0.15.3 (tag v0.15.3), wled00/ -> native/vendor/wled/"
for f in FX.cpp FX_fcn.cpp colors.cpp wled_math.cpp util.cpp \
         FX.h fcn_declare.h const.h palettes.h bus_manager.h bus_wrapper.h pin_manager.h; do
  check wled/WLED v0.15.3 "wled00/$f" "vendor/wled/$f"
done

echo
echo "FastLED 3.7.0 (tag 3.7.0), src/ -> native/vendor/fastled/"
for f in colorpalettes.cpp colorutils.cpp hsv2rgb.cpp lib8tion.cpp noise.cpp \
         color.h colorpalettes.h colorutils.h fastled_progmem.h hsv2rgb.h \
         lib8tion.h noise.h pixeltypes.h \
         lib8tion/math8.h lib8tion/random8.h lib8tion/scale8.h lib8tion/trig8.h; do
  check FastLED/FastLED 3.7.0 "src/$f" "vendor/fastled/$f"
done

echo
echo "beats.cpp, the one hand-cut extraction, against vendor/wled/util.cpp:394-424"
if diff -q <(sed -n '394,424p' vendor/wled/util.cpp) <(tail -n +10 beats.cpp) >/dev/null; then
  printf '  ok %-44s byte-exact\n' "beats.cpp"
else
  printf '  XX %-44s differs from those lines\n' "beats.cpp"; fail=1
fi

echo
if [ $fail -eq 0 ]; then echo "every vendored file matches upstream."; else echo "MISMATCH - see above."; fi
exit $fail
