namespace Baseera.App;

internal sealed record WorkspaceColors(
    uint Background, uint Surface, uint Ink, uint Muted, uint Line, uint Accent,
    uint Workspace, uint Selected, uint Hover, uint OnAccent, uint Subtle,
    uint Hero, uint HeroInk, uint HeroMuted, uint HeroLine, uint Tint, uint AccentInk)
{
    internal void Apply(ResourceDictionary resources)
    {
        resources["Bg"] = Brush(Background);
        resources["Surface"] = Brush(Surface);
        resources["Ink"] = Brush(Ink);
        resources["Muted"] = Brush(Muted);
        resources["Line"] = Brush(Line);
        resources["Accent"] = Brush(Accent);
        resources["Workspace"] = Brush(Workspace);
        resources["Selected"] = Brush(Selected);
        resources["Hover"] = Brush(Hover);
        resources["OnAccent"] = Brush(OnAccent);
        resources["Subtle"] = Brush(Subtle);
        resources["Hero"] = Brush(Hero);
        resources["HeroInk"] = Brush(HeroInk);
        resources["HeroMuted"] = Brush(HeroMuted);
        resources["HeroLine"] = Brush(HeroLine);
        resources["Tint"] = Brush(Tint);
        resources["AccentInk"] = Brush(AccentInk);
    }

    internal static SolidColorBrush Brush(uint rgb)
    {
        var brush = new SolidColorBrush(Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
        brush.Freeze();
        return brush;
    }
}

internal sealed record WorkspacePalette(string Id, string Label, WorkspaceColors Light, WorkspaceColors Dark, uint SidebarSelected)
{
    internal const string DefaultId = "green";

    internal static IReadOnlyList<WorkspacePalette> Choices { get; } =
    [
        new(DefaultId, "paletteGreen",
            new(0xF3F5F0, 0xFDFEFB, 0x172821, 0x4F655C, 0xCFDAD2, 0x16644F,
                0xE8EEE7, 0xD0E7DA, 0xE2ECE4, 0xFFFFFF, 0xEDF1EB,
                0x15352D, 0xF0FBF4, 0xB5D6C7, 0x456C5E, 0xE2F1E7, 0x165744),
            new(0x101916, 0x192520, 0xECF5EE, 0xABC2B6, 0x35483E, 0x91DFC1,
                0x131F1A, 0x28443A, 0x263830, 0x102720, 0x15211B,
                0x16392F, 0xF0FBF4, 0xB8D9CA, 0x4B7566, 0x243D32, 0xA2E9CA),
            0x384B4E),
        new("graphite", "paletteGraphite",
            new(0xF1F4F6, 0xFCFDFE, 0x202B35, 0x4D5F6F, 0xCED9E2, 0x33596F,
                0xE8EDF1, 0xD5E2EB, 0xE0E9EF, 0xFFFFFF, 0xEBF0F3,
                0x263A4A, 0xF3F8FC, 0xC1D3E2, 0x5E788B, 0xE3ECF3, 0x2D526B),
            new(0x12171D, 0x1B222A, 0xEDF2F6, 0xB7C5D0, 0x404E5B, 0xABC5D9,
                0x151B22, 0x293846, 0x26313B, 0x19252F, 0x171F27,
                0x223340, 0xF3F8FC, 0xBBCFDE, 0x53708A, 0x273744, 0xC2DBED),
            0x3D4954),
        new("red", "paletteRed",
            new(0xF7F2F3, 0xFFFDFD, 0x33242C, 0x68525C, 0xE3D2D8, 0x8A394E,
                0xF1E8EB, 0xEAD5DF, 0xF2E0E7, 0xFFFFFF, 0xF6EDF0,
                0x4C2B39, 0xFFF4F7, 0xE8C4D0, 0x956578, 0xF3E0E8, 0x7D364B),
            new(0x1A1518, 0x261D22, 0xF9EFF2, 0xCEB5BE, 0x524049, 0xF0ABB9,
                0x20181C, 0x462D36, 0x38272E, 0x321D25, 0x24191E,
                0x462834, 0xFFF3F6, 0xE6BFCA, 0x835A6B, 0x402A34, 0xF4BDC8),
            0x513A43),
        new("blue", "paletteBlue",
            new(0xF1F5FA, 0xFBFDFF, 0x192A3F, 0x485F77, 0xCBD9E7, 0x285782,
                0xE7EEF6, 0xD3E2F0, 0xDFE9F5, 0xFFFFFF, 0xEDF3F8,
                0x243E59, 0xF0F7FF, 0xBFD4EB, 0x6786A6, 0xDFEAF6, 0x264E78),
            new(0x111822, 0x1B2632, 0xEDF4FC, 0xB2C5DB, 0x3A4D62, 0x9EBFF0,
                0x141E2A, 0x283F59, 0x25364A, 0x15263C, 0x182330,
                0x203A55, 0xF0F7FF, 0xBDD4EC, 0x567FA7, 0x283D53, 0xBBD5FA),
            0x364B61)
    ];

    internal static WorkspacePalette Resolve(string? id) =>
        Choices.FirstOrDefault(palette => palette.Id == id) ?? Choices[0];

    internal WorkspaceColors Colors(bool dark) => dark ? Dark : Light;
}
