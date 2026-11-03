# Componente terte

## DeepRTTY (modelul neural)
`src/RttyDecoder.Core/Models/model_rtty_2026-10.onnx` si algoritmul de decodare in flux
(`DeepRtty/*`) provin din [e04/DeepRTTY](https://github.com/e04/DeepRTTY), licenta MIT.

## ONNX Runtime
[microsoft/onnxruntime](https://github.com/microsoft/onnxruntime), licenta MIT. Pachetul NuGet
`Microsoft.ML.OnnxRuntime`.

## NAudio
[naudio/NAudio](https://github.com/naudio/NAudio), licenta MIT. Pachetele `NAudio.Core` si
`NAudio.Wasapi`.

## Microsoft Visual C++ Redistributable
Arhiva pentru Windows include `msvcp140*.dll` si `vcruntime140*.dll` (necesare ONNX Runtime),
redistribuite conform licentei Microsoft Visual C++ Redistributable.

## ATC
Varianta "optimal ATC" a lui Kok Chen, W7AY, asa cum e descrisa si folosita in fldigi.
