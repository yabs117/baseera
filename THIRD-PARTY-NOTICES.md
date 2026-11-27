# Third-party assets

## PdfPig PDF text extraction

The desktop app uses unmodified [PdfPig](https://github.com/UglyToad/PdfPig)
version `0.1.16` to extract text from PDF documents for a local search index.
The package is licensed under Apache License 2.0. The complete upstream license
and notices, from its pinned source commit
`a7bb35662bbbf405efddad50aedc9bcdcf515afc`, are at
`src/Baseera.App/Assets/Licenses/PdfPig-APACHE-2.0.txt` and
`src/Baseera.App/Assets/Licenses/PdfPig-NOTICES.txt`. Both files are embedded in
the standalone executable and included in the Windows ZIP.


The unmodified `Elgharib-Hayyakum-Allah-Taweel-Medium.ttf` supplied for Baseera is
distributed under the SIL Open Font License (OFL), according to its embedded license metadata.

The font's embedded copyright notices are:

> Copyright 2026 Ministry of Media. All rights reserved.
> Copyright © 2026 By Ahmed Alaa Elgharib. All Rights Reserved.

The complete font license is in `src/Baseera.App/Assets/Fonts/OFL.txt`,
`extension/assets/OFL.txt`, and `OFL.txt` in the Windows distribution.
The font remains under its own license; the repository's MIT license applies to the application code.

The unmodified Noto Serif fonts supplied by the user are also distributed under
the SIL Open Font License, Version 1.1.

> Copyright 2022 The Noto Project Authors (https://github.com/notofonts/latin-greek-cyrillic)

The desktop app uses the supplied static Regular, Medium, SemiBold, Bold and Italic
faces. The browser extension uses the supplied upright and italic variable fonts.
Their license is in `src/Baseera.App/Assets/Fonts/Noto-OFL.txt`,
`extension/assets/Noto-OFL.txt`, and `Noto-OFL.txt` in the Windows ZIP. Both font
licenses are also embedded in the standalone Windows executable.

The unmodified ChatGPT logo in `src/Baseera.App/Assets/Brand/chatgpt-logo-white.svg`
is an OpenAI brand asset used on the official sign-in button. OpenAI and ChatGPT
marks belong to OpenAI and are not relicensed under the application’s MIT license.
Asset source: https://developers.openai.com/assets/siwc/chatgpt-logo-white.svg
Usage guidance: https://developers.openai.com/siwc/ui-ux-guidelines

## Provider identification marks

The twenty unmodified monochrome provider marks in `src/Baseera.App/Assets/Brand/Providers/`
come from [Lobe Icons](https://github.com/lobehub/lobe-icons), pinned at commit
`7d7d0379f18b114a4009dc5cb85a7c375284f31b`. Source mappings are recorded in
`Providers/SOURCE.md`. The accompanying MIT license is embedded in the Windows app
and reproduced below. All brand marks belong to their respective owners; their
presence identifies the selected service and does not imply endorsement.

MIT License

Copyright (c) 2023 LobeHub

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
