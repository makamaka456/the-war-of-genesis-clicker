// 파트1 엔딩 영상(MPEG-1 · MP2 소리)을 푸는 아주 얇은 겉 — pl_mpeg(MIT, phoboslab/pl_mpeg, native/pl_mpeg.h)를 그대로 쓴다.
// C# 쪽은 Platform/MpegVideo.cs. 안드로이드는 CI 가 NDK 로 ABI 마다 libwogmpeg.so 를 굽고, 시험기는 build.py 가 gcc 로 굽는다.
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <stdint.h>
#define PL_MPEG_IMPLEMENTATION
#include "pl_mpeg.h"

#if defined(_WIN32)
#define EXPORT __declspec(dllexport)
#else
#define EXPORT __attribute__((visibility("default")))
#endif

// 연다 — 못 열거나 영상 갈래가 없으면 NULL.
EXPORT void *wog_open(const char *path, int *width, int *height, double *fps, int *samplerate, double *duration)
{
    plm_t *plm = plm_create_with_filename(path);
    if (!plm) return NULL;
    if (!plm_probe(plm, 5000 * 1024) || plm_get_num_video_streams(plm) == 0) { plm_destroy(plm); return NULL; }
    *width = plm_get_width(plm);
    *height = plm_get_height(plm);
    *fps = plm_get_framerate(plm);
    *samplerate = plm_get_num_audio_streams(plm) > 0 ? plm_get_samplerate(plm) : 0;
    *duration = plm_get_duration(plm);
    plm_set_loop(plm, 0);
    return plm;
}

// 다음 그림을 ARGB(0xAARRGGBB, 너비 × 높이)로 — 끝이면 0.
EXPORT int wog_video(void *p, uint32_t *argb, double *time)
{
    plm_frame_t *f = plm_decode_video((plm_t *)p);
    if (!f) return 0;
    int n = f->width * f->height;
    for (int i = 0; i < n; i++) argb[i] = 0xFF000000u;   // bgra 는 알파를 안 쓴다
    plm_frame_to_bgra(f, (uint8_t *)argb, f->width * 4);   // 바이트 B G R A = 작은 끝 0xAARRGGBB
    *time = f->time;
    return 1;
}

// 다음 소리 덩이(1152 표본, 늘 스테레오)를 16비트로 — 표본 수, 끝이면 0.
EXPORT int wog_audio(void *p, int16_t *pcm, double *time)
{
    plm_samples_t *s = plm_decode_audio((plm_t *)p);
    if (!s) return 0;
    for (unsigned i = 0; i < s->count * 2; i++)
    {
        float v = s->interleaved[i];
        pcm[i] = (int16_t)(v > 1.0f ? 32767 : v < -1.0f ? -32768 : v * 32767.0f);
    }
    *time = s->time;
    return (int)s->count;
}

EXPORT void wog_close(void *p) { if (p) plm_destroy((plm_t *)p); }
