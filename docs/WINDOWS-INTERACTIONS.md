# Windows interaction study

Baseera retains its WPF layout, supplied fonts, navigation order and native window
caption. Its interaction implementation is original code, informed by an authorized
observation of the user's Windows Settings and Microsoft's public design guidance.
No private Windows source, icons, binaries or account screenshots are redistributed.

## Evidence and boundaries

REA inspected PE resources in the installed Windows Settings artifacts:

| Artifact | SHA-256 | REA Evidence ID |
| --- | --- | --- |
| SystemSettings.exe, 165,856 bytes | `c514b73f3b9cc38be1dc817c177d07ecf12ce4ce9e21df5d54128e7359198bba` | `ev_4b45a996fb1df1b96e8486636b01a83fd32f9970dabf2b6dd29b5622fc8c1759` |
| SystemSettings.dll, 5,900,800 bytes | `129f124fe001c75855e7f3ed5038f98f0bc0662362496c6b2724469dd65f4a16` | `ev_5b5da8004e5965d61bff5b7741e3365009a5c217eebacc017c67f3bed822ae7f` |

These resource trees establish artifact identity and resource inventory. They do
not recover private XAML or animation timings. The connected REA native UI tracing
route supports macOS, so Windows runtime observation used Computer Use instead.
Observed Home/Personalization states included a rounded selected row, narrow
accent indicator, subtle hover fill, stable text and icons, and card navigation.
Desktop interference limited repeated observations. Exact Windows frame timing,
easing and internal implementation were not measured or recovered.

Public references: [Microsoft motion guidance](https://learn.microsoft.com/en-us/windows/apps/design/signature-experiences/motion)
and [WinUI button visual states](https://github.com/microsoft/microsoft-ui-xaml/blob/main/controls/dev/CommonStyles/Button_themeresources.xaml).
The latter is a reference for distinct interaction states, not copied WPF markup.

## Baseera implementation

Hover feedback changes a surface overlay, preserving text opacity. Press feedback
uses a small content scale and stronger overlay. Navigation selection moves between
rows; pages enter with a short fade/translation. Rapid navigation starts from the
current visual position. The sidebar expands from its actual current width.
Switch thumbs slide, dropdown chevrons rotate and flyouts enter briefly. Scroll
thumbs widen on interaction. Keyboard focus indicators remain visible.

Baseera chooses its own durations: press 70 ms, hover 100 ms, release 160 ms,
switches 140 ms and navigation/sidebar 180 ms. These are not claims about Windows
Settings internals. Motion is skipped when Windows client-area animation is off
or high contrast is enabled. The application does not alter those OS settings.

Regression checks exercise routed hover/resting feedback, real navigation button
handlers, interrupted navigation, visible flyouts and switch state in Arabic RTL,
English, French and Spanish. They do not claim physical keyboard coverage or a
live test that changes the user's reduced-motion preference.
