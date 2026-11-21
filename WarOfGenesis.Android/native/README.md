# native — 파트1 엔딩 영상 해독기

- `pl_mpeg.h` — Dominic Szablewski 의 pl_mpeg(https://github.com/phoboslab/pl_mpeg), **MIT 라이선스**(파일 머리에 적혀 있다). 고치지 않고 그대로 둔다.
- `wogmpeg.c` — C# 이 부르는 얇은 겉(`Platform/MpegVideo.cs`).
- 굽기: CI(`.github/workflows/android.yml`)가 NDK clang 으로 ABI 마다 `libs/<abi>/libwogmpeg.so` 를 만든다. 시험기는 `devtools/sim/build.py` 가 gcc 로.
