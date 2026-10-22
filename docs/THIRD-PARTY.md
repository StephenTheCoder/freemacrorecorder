# Included components

- .NET / WPF 10 runtime: Microsoft; the self-contained distribution includes its license and third-party notices.
- OpenCvSharp4 and the Windows x64 slim native runtime, version 4.13.0.20260627: [shimat/opencvsharp](https://github.com/shimat/opencvsharp), Apache 2.0. See `licenses/OpenCvSharp-LICENSE.txt`.
- OpenCV 4.13 and native image codecs included by that runtime: [opencv/opencv](https://github.com/opencv/opencv), Apache 2.0 and the corresponding upstream third-party notices. See `licenses/OpenCV-LICENSE.txt` and upstream `3rdparty` license files.
- Windows OCR, capture, input and Credential Manager are operating-system facilities. Windows OCR languages must be installed separately through Windows language features.

Native OpenCV may require the Microsoft Visual C++ 2015–2022 x64 runtime on a clean Windows installation. It was available on the verified machine. If OpenCvSharpExtern cannot load, install Microsoft's supported x64 Visual C++ redistributable and retry. No native runtime prerequisite installer is run automatically.
