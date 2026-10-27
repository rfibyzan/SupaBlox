using Avalonia.Media;
using Avalonia.Media.Fonts;
using Avalonia.Platform;

namespace SupaBlox.Core;

/// <summary>
/// Builder Sans (font Roblox) TIDAK dibundel karena lisensinya hanya untuk konten Roblox.
/// SupaBlox memuatnya dari instalasi Roblox Player lokal milik user:
/// %LocalAppData%\Roblox\Versions\&lt;versi&gt;\content\fonts\BuilderSans-*.otf
/// Kalau tidak ada, UI memakai Inter.
/// </summary>
public static class BuilderFont
{
    public const string FamilyName = "Builder Sans";
    public static readonly Uri Key = new("fonts:BuilderSans");

    public static List<string> FindFiles()
    {
        var dirs = new List<string>();
        var dev = Environment.GetEnvironmentVariable("SUPABLOX_FONT_DIR");
        if (!string.IsNullOrEmpty(dev)) dirs.Add(dev);

        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        foreach (var root in new[] { Path.Combine(local, "Roblox", "Versions"),
                                     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Roblox", "Versions") })
        {
            if (!Directory.Exists(root)) continue;
            try
            {
                dirs.AddRange(Directory.GetDirectories(root)
                    .OrderByDescending(Directory.GetLastWriteTimeUtc)
                    .Select(d => Path.Combine(d, "content", "fonts")));
            }
            catch { }
        }

        foreach (var d in dirs)
        {
            try
            {
                if (!Directory.Exists(d)) continue;
                var files = Directory.GetFiles(d, "BuilderSans*.otf").Concat(Directory.GetFiles(d, "BuilderSans*.ttf")).ToList();
                if (files.Count > 0) return files;
            }
            catch { }
        }
        return new List<string>();
    }

    /// <summary>Daftarkan koleksi font. True kalau Builder Sans berhasil dimuat.</summary>
    public static bool TryRegister()
    {
        try
        {
            var files = FindFiles();
            if (files.Count == 0) return false;
            var collection = new LocalFontCollection(Key, FamilyName, files);
            FontManager.Current.AddFontCollection(collection);
            return collection.Count > 0;
        }
        catch (Exception ex)
        {
            Storage.Log("BuilderFont", ex);
            return false;
        }
    }

    private sealed class LocalFontCollection : FontCollectionBase
    {
        private readonly Uri _key;
        private readonly string _family;
        private readonly List<string> _files;
        private readonly List<IGlyphTypeface> _faces = new();
        private readonly List<FontFamily> _families = new();

        public LocalFontCollection(Uri key, string family, List<string> files)
        {
            _key = key;
            _family = family;
            _files = files;
        }

        public override Uri Key => _key;
        public override int Count => _families.Count;
        public override FontFamily this[int index] => _families[index];

        public override void Initialize(IFontManagerImpl fontManager)
        {
            foreach (var f in _files)
            {
                try
                {
                    var ms = new MemoryStream(File.ReadAllBytes(f));
                    if (CreateFromStream(fontManager, ms) is { } gt) _faces.Add(gt);
                }
                catch (Exception ex) { Storage.Log("font " + f, ex); }
            }
            if (_faces.Count > 0) _families.Add(new FontFamily(_key, _family));
        }

        public override bool TryGetGlyphTypeface(string familyName, FontStyle style, FontWeight weight, FontStretch stretch,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out IGlyphTypeface? glyphTypeface)
        {
            glyphTypeface = null;
            if (_faces.Count == 0 || !familyName.Equals(_family, StringComparison.OrdinalIgnoreCase)) return false;
            var candidates = _faces.Where(f => (f.Style == FontStyle.Italic) == (style == FontStyle.Italic)).ToList();
            if (candidates.Count == 0) candidates = _faces;
            glyphTypeface = candidates.OrderBy(f => Math.Abs((int)f.Weight - (int)weight)).First();
            return true;
        }

        // IFontManagerImpl.TryCreateGlyphTypeface(Stream, ...) disembunyikan dari reference assembly Avalonia,
        // jadi dipanggil lewat reflection. Gagal = fallback ke Inter.
        private static IGlyphTypeface? CreateFromStream(IFontManagerImpl impl, Stream stream)
        {
            var m = typeof(IFontManagerImpl).GetMethod("TryCreateGlyphTypeface",
                new[] { typeof(Stream), typeof(FontSimulations), typeof(IGlyphTypeface).MakeByRefType() });
            if (m == null) return null;
            var args = new object?[] { stream, FontSimulations.None, null };
            return m.Invoke(impl, args) is true ? args[2] as IGlyphTypeface : null;
        }

        public override IEnumerator<FontFamily> GetEnumerator() => _families.GetEnumerator();
    }
}
