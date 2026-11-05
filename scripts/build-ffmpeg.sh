#!/usr/bin/env bash
set -euo pipefail

# An LGPL-only, dependency-free FFmpeg build. HTTP is handled by the C# app.
commit=3a0867c2bfda4a4d4309ca1a8cbdc6175e67f587 # FFmpeg n7.1.5
work="$(pwd)/.build/ffmpeg-build"
package="$(pwd)/.build/ffmpeg-package"
mkdir -p "$work" "$package/licenses"
curl --fail --location --retry 3 "https://api.github.com/repos/FFmpeg/FFmpeg/tarball/$commit" -o "$package/ffmpeg-source.tar.gz"
tar -xzf "$package/ffmpeg-source.tar.gz" -C "$work" --strip-components=1
cd "$work"
flags=(
  --target-os=mingw32 --arch=x86_64 --cross-prefix=x86_64-w64-mingw32-
  --disable-autodetect --disable-debug --disable-doc --disable-network
  --disable-ffplay --disable-ffprobe --disable-everything
  --enable-ffmpeg --enable-protocol=file
  --enable-demuxer=hls,mpegts,mov --enable-muxer=mp4,hls,mpegts,null
  --enable-parser=h264,hevc,mpegvideo,mpeg4video,aac,aac_latm,mpegaudio,ac3
  --enable-decoder=h264,hevc,mpeg2video,mpeg4,aac,aac_fixed,aac_latm,mp2,mp3,ac3,eac3,pcm_s16le,pcm_f32le,rawvideo
  --enable-encoder=mpeg2video,aac,wrapped_avframe,pcm_s16le
  --enable-bsf=aac_adtstoasc,extract_extradata,h264_mp4toannexb,hevc_mp4toannexb
  --enable-indev=lavfi --enable-filter=abuffer,abuffersink,buffer,buffersink,testsrc2,sine,anull,null,aformat,aresample,format,scale
  --extra-cflags="-O2 -ffile-prefix-map=$work=ffmpeg-source"
  --extra-ldflags=-static
)
./configure "${flags[@]}"
make -j2 ffmpeg.exe
cp ffmpeg.exe "$package/ffmpeg.exe"
cp COPYING.LGPLv2.1 LICENSE.md "$package/licenses/"
cp ffbuild/config.log "$package/config.log"
{
  echo 'FFmpeg 7.1.5 - MT001 Windows x64 build'
  echo "Upstream commit: $commit"
  echo "Source URL: https://github.com/FFmpeg/FFmpeg/tree/$commit"
  echo 'No external codec libraries. GPL, nonfree and network components are disabled.'
  printf 'Configure:'; printf ' %q' "${flags[@]}"; printf '\n'
  x86_64-w64-mingw32-gcc --version | head -n1
  nasm -v
  echo "Built (UTC): $(date -u +%FT%TZ)"
  sha256sum "$package/ffmpeg.exe" "$package/ffmpeg-source.tar.gz"
} > "$package/build-info.txt"
