# PDF text and OCR components

- [PdfPig 0.1.16](https://github.com/UglyToad/PdfPig): Apache-2.0, `PdfPig-LICENSE.txt`.
- [Tesseract .NET wrapper 5.2.0](https://github.com/charlesw/tesseract): Apache-2.0, `Tesseract-wrapper-LICENSE.txt`; includes x64 Tesseract 5.2 and Leptonica 1.82 native libraries.
- [Tesseract OCR 5.2.0](https://github.com/tesseract-ocr/tesseract/tree/5.2.0): Apache-2.0, `Tesseract-LICENSE.txt`.
- [Leptonica 1.82.0](https://github.com/DanBloomberg/leptonica/tree/1.82.0): BSD-style license, `Leptonica-LICENSE.txt`.
- [tessdata_best](https://github.com/tesseract-ocr/tessdata_best/tree/e12c65a915945e4c28e237a9b52bc4a8f39a0cec): Apache-2.0, `Tesseract-models-LICENSE.txt`. Vietnamese and English models pinned to commit `e12c65a915945e4c28e237a9b52bc4a8f39a0cec`.

SHA-256:

```
eng.traineddata 8280AED0782FE27257A68EA10FE7EF324CA0F8D85BD2FD145D1C2B560BCB66BA
vie.traineddata B6B49293D95D0B6DBD8780174627E82C75BE957B6F4ED9862155540D6B00BB45
```

Models are downloaded from the official repository and verified by `scripts/prepare-ocr.ps1`. All recognition runs locally. Native binaries retain their original names in the `x64` folder. OCR needs the Microsoft Visual C++ x64 runtime; it is not bundled in this ZIP.
