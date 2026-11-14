namespace CorsixNet.Formats;

/// <summary>
/// Cyrillic letters for a Theme Hospital bitmap font, made from the font's own Latin letters, so that Russian and
/// Ukrainian text is drawn in the game's lettering rather than with a TrueType font.
/// </summary>
/// <remarks>
/// The game's fonts have no Cyrillic (the Russian release had fonts of its own, which CorsixNet does not read). Most
/// capitals share a Latin shape (А В Е К М Н О Р С Т Х) or are one mirrored (И Я, Э from С); the rest are put together
/// from parts of the font's letters: stems and bars of Н, F, L and Е, the arms of К, the bowl of b, the digits 3 and 6.
/// Small letters are the capitals with rows taken out down to the font's x-height (small capitals, as pixel fonts draw
/// Cyrillic), except where the Latin small letter has the shape (а е о р с у х, і ї ё). Pixels keep their palette
/// indices, so a font's colours, shading and outlines come along; mirrored letters of a slanted font are slanted back.
/// A letter is left out when the font lacks what it is made of (fonts of digits only).
/// </remarks>
public static class CyrillicGlyphs
{
    private const byte Clear = Palette.TransparentIndex;

    /// <summary>The Russian alphabet, all of which a font must get to get any.</summary>
    public const string Russian = "АБВГДЕЁЖЗИЙКЛМНОПРСТУФХЦЧШЩЪЫЬЭЮЯабвгдеёжзийклмнопрстуфхцчшщъыьэюя";

    /// <summary>The Cyrillic letters (U+0400 to U+045F, Ґ and ґ) that could be made for the font, by code point.</summary>
    public static IReadOnlyDictionary<int, IndexedImage> Build(BitmapFont font)
    {
        ArgumentNullException.ThrowIfNull(font);
        var result = new Dictionary<int, IndexedImage>();
        if (Metrics.Find(font) is not { } metrics)
        {
            return result;
        }

        var maker = new Maker(font, metrics);
        foreach (var (code, glyph) in maker.All())
        {
            if (glyph is { } made && made.Bounds().L >= 0)
            {
                result[code] = made.Fitted(metrics.Bearing, metrics.Padding).ToImage();
            }
            else if (Russian.Contains((char)code))
            {
                // A font whose letters cannot all be made (serif, hand-drawn and outlined fonts, whose stems and bars
                // are not plain runs of pixels) gets none, so that a line is never drawn in two fonts.
                return new Dictionary<int, IndexedImage>();
            }
        }

        return result;
    }

    /// <summary>Where a font's letters stand: the rows of its capitals and small letters, its stems and spacing.</summary>
    private sealed record Metrics(int CapitalTop, int SmallTop, int Baseline, int Stem, int Bearing, int Padding, double Slant, bool CapitalsOnly)
    {
        public static Metrics? Find(BitmapFont font)
        {
            if (Source(font, 'H') is not { } h)
            {
                return null;
            }

            var (left, right, top, bottom) = h.Bounds();
            var x = Source(font, 'x');
            var a = Source(font, 'a');
            var capitalsOnly = x is null || (a is not null && Source(font, 'A') is { } capitalA && a.SameAs(capitalA));
            var smallTop = !capitalsOnly && x is not null ? x.Bounds().T : top;

            // The stem: the first run of ink in H's lower half.
            var row = bottom - ((bottom - top) / 4);
            var stem = 0;
            while (left + stem <= right && h.Ink(left + stem, row))
            {
                stem++;
            }

            // The slant: how far the stem of I (else l) moves across from its foot to its top, per row.
            double slant = 0;
            if ((Source(font, 'I') ?? Source(font, 'l')) is { } i)
            {
                var (_, _, iTop, iBottom) = i.Bounds();
                var shift = i.FirstInk(iTop) - i.FirstInk(iBottom);
                slant = iBottom > iTop && Math.Abs(shift) >= 2 ? (double)shift / (iBottom - iTop) : 0;
            }

            return new Metrics(top, smallTop, bottom + 1, Math.Max(stem, 1), left, h.Width - 1 - right, slant, capitalsOnly);
        }
    }

    /// <summary>The font's letter as a glyph to work on; null if it has none or it is blank.</summary>
    private static Glyph? Source(BitmapFont font, char character)
    {
        if (!font.TryGetGlyph(new System.Text.Rune(character), out var sprite))
        {
            return null;
        }

        var image = font.Sheet[sprite];
        var glyph = Glyph.From(image);
        return image.Width > 1 && glyph.Bounds().L >= 0 ? glyph : null;
    }

    /// <summary>The recipes, each null when a part is missing.</summary>
    private sealed class Maker(BitmapFont font, Metrics m)
    {
        private Glyph? S(char character) => Source(font, character);

        public IEnumerable<(int Code, Glyph? Glyph)> All()
        {
            var capitals = new Dictionary<char, Glyph?>
            {
                ['А'] = S('A'),
                ['Б'] = Be(),
                ['В'] = S('B'),
                ['Г'] = Ge(),
                ['Д'] = De(),
                ['Е'] = S('E'),
                ['Ж'] = Zhe(),
                ['З'] = Ze(),
                ['И'] = Mirrored(S('N')),
                ['К'] = S('K'),
                ['Л'] = El(),
                ['М'] = S('M'),
                ['Н'] = S('H'),
                ['О'] = S('O'),
                ['П'] = Pe(),
                ['Р'] = S('P'),
                ['С'] = S('C'),
                ['Т'] = S('T'),
                ['У'] = S('Y'),
                ['Ф'] = Ef(),
                ['Х'] = S('X'),
                ['Ц'] = Tse(),
                ['Ч'] = Che(),
                ['Ш'] = Sha(),
                ['Щ'] = Tail(Sha()),
                ['Ъ'] = Hard(),
                ['Ы'] = Yery(),
                ['Ь'] = Soft(),
                ['Э'] = E(mirror: true),
                ['Ю'] = Yu(),
                ['Я'] = Mirrored(S('R')),
                ['Є'] = E(mirror: false),
                ['І'] = S('I'),
            };
            capitals['Й'] = Breve(capitals['И']);
            capitals['Ё'] = Dots(capitals['Е']);
            capitals['Ї'] = Dots(capitals['І']);
            capitals['Ґ'] = Upturn(capitals['Г']);

            foreach (var (letter, glyph) in capitals)
            {
                yield return (letter, glyph);
            }

            foreach (var (letter, capital) in capitals)
            {
                var small = char.ToLowerInvariant(letter);
                yield return (small, m.CapitalsOnly ? capital : Small(small, capital));
            }
        }

        private Glyph? Small(char letter, Glyph? capital) => letter switch
        {
            'а' => S('a'),
            'е' => S('e'),
            'о' => S('o'),
            'р' => S('p'),
            'с' => S('c'),
            'у' => S('y'),
            'х' => S('x'),
            'і' => S('i'),
            'ї' => S('ï'),
            'ё' => S('ë') ?? Dots(S('e')),
            'б' => S('6'),
            'ф' => SmallEf() ?? Height(capital, m.SmallTop),
            'й' => Breve(Height(Mirrored(S('N')), m.SmallTop)),
            'ю' => SmallYu() ?? Height(capital, m.SmallTop),
            _ => Height(capital, m.SmallTop),
        };

        /// <summary>Mirrored, and slanted back the way the font leans.</summary>
        private Glyph? Mirrored(Glyph? glyph) => glyph is null ? null : Lean(glyph.Mirror(), 2 * m.Slant);

        private Glyph Lean(Glyph glyph, double slant) => slant == 0 ? glyph : glyph.Shear(slant, m.Baseline - 1);

        private static Glyph? Height(Glyph? glyph, int top) => glyph?.ToTop(top);

        /// <summary>The rows of a letter's bar beside its stem (F's top bar, L's foot, E's middle).</summary>
        private List<int> BarRows(Glyph glyph, Func<int, bool> rowFilter)
        {
            var (left, right, top, bottom) = glyph.Bounds();
            var rows = new List<int>();
            for (var y = top; y <= bottom; y++)
            {
                if (rowFilter(y) && glyph.LastInk(y) > left + m.Stem && glyph.RunFrom(left + m.Stem, y) > 1)
                {
                    rows.Add(y);
                }
            }

            _ = right;
            return rows;
        }

        private List<int> TopBar(Glyph f)
        {
            var rows = BarRows(f, _ => true);
            return Consecutive(rows, first: true);
        }

        private List<int> FootBar(Glyph l)
        {
            var rows = BarRows(l, _ => true);
            return Consecutive(rows, first: false);
        }

        private static List<int> Consecutive(List<int> rows, bool first)
        {
            if (rows.Count == 0)
            {
                return rows;
            }

            var run = new List<int>();
            if (first)
            {
                run.Add(rows[0]);
                for (var i = 1; i < rows.Count && rows[i] == rows[i - 1] + 1; i++)
                {
                    run.Add(rows[i]);
                }
            }
            else
            {
                run.Add(rows[^1]);
                for (var i = rows.Count - 2; i >= 0 && rows[i] == rows[i + 1] - 1; i--)
                {
                    run.Insert(0, rows[i]);
                }
            }

            return run;
        }

        /// <summary>The colour of a bar's row: the middle of its run beside the stem.</summary>
        private byte BarColour(Glyph glyph, int y)
        {
            var start = glyph.Bounds().L + m.Stem;
            while (start <= glyph.LastInk(y) && !glyph.Ink(start, y))
            {
                start++;
            }

            var run = glyph.RunFrom(start, y);
            return glyph[start + (run / 2), y];
        }

        /// <summary>Paints the source's bar rows, moved to stand on the same baseline, across x0..x1.</summary>
        private void PaintBar(Glyph target, Glyph source, List<int> rows, int x0, int x1)
        {
            foreach (var y in rows)
            {
                var colour = BarColour(source, y);
                for (var x = x0; x <= x1; x++)
                {
                    target[x, y] = colour;
                }
            }
        }

        /// <summary>H's two stems: their columns and H without its cross bar.</summary>
        private (Glyph Stems, int LeftEnd, int RightStart, List<int> Cross)? Stems()
        {
            if (S('H') is not { } h)
            {
                return null;
            }

            var (left, right, top, bottom) = h.Bounds();
            var leftEnd = left + m.Stem - 1;
            var row = bottom - ((bottom - top) / 4);
            var rightStart = right;
            while (rightStart > leftEnd + 1 && h.Ink(rightStart - 1, row))
            {
                rightStart--;
            }

            var cross = new List<int>();
            var stems = h.Clone();
            for (var y = top; y <= bottom; y++)
            {
                var crossed = false;
                for (var x = leftEnd + 1; x < rightStart; x++)
                {
                    crossed |= h.Ink(x, y);
                    stems[x, y] = Clear;
                }

                if (crossed)
                {
                    cross.Add(y);
                }
            }

            return (stems, leftEnd, rightStart, cross);
        }

        private Glyph? Ge()
        {
            if (S('F') is not { } f)
            {
                return null;
            }

            var bar = TopBar(f);
            if (bar.Count == 0)
            {
                return null;
            }

            var g = f.Clone();
            var (left, right, top, bottom) = f.Bounds();
            for (var y = bar[^1] + 1; y <= bottom; y++)
            {
                for (var x = left + m.Stem; x <= right; x++)
                {
                    g[x, y] = Clear;
                }
            }

            _ = top;
            return g;
        }

        private Glyph? Pe()
        {
            if (Stems() is not { } stems || S('F') is not { } f)
            {
                return null;
            }

            var pe = stems.Stems;
            PaintBar(pe, f, TopBar(f), stems.LeftEnd + 1, stems.RightStart - 1);
            return pe;
        }

        private Glyph? El()
        {
            if (Pe() is not { } pe)
            {
                return null;
            }

            var el = pe.Pad(1, 0);
            var (left, _, top, bottom) = el.Bounds();
            var foot = Math.Max(1, (bottom - top + 1) / 4);
            for (var y = bottom - foot + 1; y <= bottom; y++)
            {
                for (var x = left; x < left + m.Stem; x++)
                {
                    el[x - 1, y] = el[x, y];
                }

                el[left + m.Stem - 1, y] = Clear;
            }

            return el;
        }

        private Glyph? De()
        {
            if (Pe() is not { } pe || S('L') is not { } l)
            {
                return null;
            }

            var foot = FootBar(l);
            if (foot.Count == 0)
            {
                return null;
            }

            var de = pe.Pad(1, 1);
            var (left, right, _, bottom) = de.Bounds();
            var colour = BarColour(l, foot[^1]);
            for (var i = 0; i < foot.Count; i++)
            {
                var y = bottom - foot.Count + 1 + i;
                for (var x = left - 1; x <= right + 1; x++)
                {
                    de[x, y] = BarColour(l, foot[i]);
                }
            }

            // The legs below the baseline, where the font has room.
            if (bottom + 1 < de.Height)
            {
                for (var x = 0; x < Math.Min(m.Stem, 2); x++)
                {
                    de[left - 1 + x, bottom + 1] = colour;
                    de[right + 1 - x, bottom + 1] = colour;
                }
            }

            return de;
        }

        private Glyph? Tse() => Stems() is { } stems ? Tail(Foot(stems.Stems, stems.LeftEnd + 1, stems.RightStart - 1)) : null;

        /// <summary>L's foot painted across the bottom of the glyph between the given columns.</summary>
        private Glyph? Foot(Glyph glyph, int x0, int x1)
        {
            if (S('L') is not { } l)
            {
                return null;
            }

            var foot = FootBar(l);
            if (foot.Count == 0 || foot[^1] != glyph.Bounds().B)
            {
                return null;
            }

            PaintBar(glyph, l, foot, x0, x1);
            return glyph;
        }

        /// <summary>Ц's and Щ's tail: the foot carried a pixel on and down below the baseline at the right.</summary>
        private Glyph? Tail(Glyph? glyph)
        {
            if (glyph is null)
            {
                return null;
            }

            var tail = glyph.Pad(0, 1);
            var (_, right, _, bottom) = glyph.Bounds();
            var colour = tail[right, bottom];
            for (var y = bottom; y > bottom - Math.Max(1, m.Stem) && y >= 0; y--)
            {
                tail[right + 1, y] = tail[right, y];
            }

            if (bottom + 1 < tail.Height)
            {
                tail[right + 1, bottom + 1] = colour;
            }

            return tail;
        }

        private Glyph? Sha()
        {
            if (Stems() is not { } stems)
            {
                return null;
            }

            var source = stems.Stems;
            var (left, _, _, _) = source.Bounds();
            var inner = stems.RightStart - stems.LeftEnd - 1;
            var gap = Math.Max(2, ((inner * 2) + 2) / 3);
            var width = left + (3 * m.Stem) + (2 * gap) + 1;
            var sha = new Glyph(width, source.Height);
            for (var i = 0; i < 3; i++)
            {
                var x0 = left + (i * (m.Stem + gap));
                for (var y = 0; y < source.Height; y++)
                {
                    for (var x = 0; x < m.Stem; x++)
                    {
                        sha[x0 + x, y] = source[left + x, y];
                    }
                }
            }

            return Foot(sha, left + m.Stem, left + (3 * m.Stem) + (2 * gap) - m.Stem - 1);
        }

        private Glyph? Che()
        {
            if (Stems() is not { } stems || S('H') is not { } h || stems.Cross.Count == 0)
            {
                return null;
            }

            var che = h.Clone();
            var (left, _, _, bottom) = h.Bounds();
            for (var y = stems.Cross[^1] + 1; y <= bottom; y++)
            {
                for (var x = left; x <= stems.LeftEnd; x++)
                {
                    che[x, y] = Clear;
                }
            }

            return che;
        }

        private Glyph? Zhe()
        {
            if (S('K') is not { } k)
            {
                return null;
            }

            var (left, _, _, _) = k.Bounds();
            var stem = k.Columns(left, left + m.Stem - 1);
            var arms = k.Clone();
            for (var x = left; x < left + m.Stem; x++)
            {
                for (var y = 0; y < k.Height; y++)
                {
                    arms[x, y] = Clear;
                }
            }

            if (arms.Bounds().L < 0 || Mirrored(arms) is not { } mirrored)
            {
                return null;
            }

            return Glyph.Join(Glyph.Join(mirrored, stem, 0), arms, 0);
        }

        /// <summary>З: the digit 3, else B without its stem.</summary>
        private Glyph? Ze()
        {
            if (Height(S('3'), m.CapitalTop) is { } three)
            {
                return three;
            }

            if (S('B') is not { } b)
            {
                return null;
            }

            var left = b.Bounds().L;
            var ze = b.Clone();
            for (var y = 0; y < b.Height; y++)
            {
                for (var x = left; x < left + m.Stem; x++)
                {
                    ze[x, y] = Clear;
                }
            }

            return ze.Bounds().L < 0 ? null : ze;
        }

        /// <summary>Ь: B without its upper bowl (the rows above its middle bar), else b with its stem as tall as the capitals.</summary>
        private Glyph? Soft()
        {
            if (S('B') is { } b)
            {
                var (left, right, top, bottom) = b.Bounds();
                var height = bottom - top + 1;

                // The middle bar: the row between the top and the foot reaching furthest across beside the stem.
                var middle = Enumerable.Range(top + 1, Math.Max(1, height - 2))
                    .OrderByDescending(y => b.RunFrom(left + m.Stem, y))
                    .ThenBy(y => Math.Abs(y - (top + (height / 2))))
                    .First();
                if (b.RunFrom(left + m.Stem, middle) > 1)
                {
                    var soft = b.Clone();
                    for (var y = top; y < middle; y++)
                    {
                        for (var x = left + m.Stem; x <= right; x++)
                        {
                            soft[x, y] = Clear;
                        }
                    }

                    return soft;
                }
            }

            return Height(S('b'), m.CapitalTop);
        }

        private Glyph? Be()
        {
            if (Soft() is not { } soft || S('F') is not { } f)
            {
                return null;
            }

            var bar = TopBar(f);
            var (left, right, _, _) = soft.Bounds();
            var length = f.LastInk(bar.Count > 0 ? bar[0] : f.Bounds().T) - f.Bounds().L;
            var be = soft.Pad(0, Math.Max(0, left + length - right));
            PaintBar(be, f, bar, left + m.Stem, Math.Max(right, left + length));
            return be;
        }

        private Glyph? Hard()
        {
            if (Soft() is not { } soft || S('F') is not { } f)
            {
                return null;
            }

            var bar = TopBar(f);
            var tick = Math.Max(2, m.Stem + 1);
            var hard = soft.Pad(tick, 0);
            var left = hard.Bounds().L;
            PaintBar(hard, f, bar, left - tick, left - 1);
            return hard;
        }

        private Glyph? Yery() => Soft() is { } soft && S('I') is { } i ? Glyph.Join(soft, i, 1) : null;

        /// <summary>Э (С mirrored) or Є (С) with E's middle bar reaching in from the open side.</summary>
        private Glyph? E(bool mirror)
        {
            if (S('C') is not { } c || S('E') is not { } e)
            {
                return null;
            }

            var top = TopBar(e);
            var foot = FootBar(e);
            var middle = BarRows(e, y => !top.Contains(y) && !foot.Contains(y));
            if (middle.Count == 0)
            {
                return null;
            }

            var glyph = mirror ? Mirrored(c)! : c.Clone();
            var length = Math.Max(2, ((e.LastInk(middle[0]) - e.Bounds().L - m.Stem + 1) * 2) / 3);
            foreach (var y in middle)
            {
                var colour = BarColour(e, y);
                if (mirror)
                {
                    // From the inside of the right-hand curve leftwards.
                    var x = glyph.LastInk(y);
                    while (x > 0 && glyph.Ink(x, y))
                    {
                        x--;
                    }

                    for (var i = 0; i < length; i++)
                    {
                        glyph[x - i, y] = colour;
                    }
                }
                else
                {
                    var x = glyph.FirstInk(y);
                    while (x < glyph.Width - 1 && glyph.Ink(x, y))
                    {
                        x++;
                    }

                    for (var i = 0; i < length; i++)
                    {
                        glyph[x + i, y] = colour;
                    }
                }
            }

            return glyph;
        }

        private Glyph? Yu()
        {
            if (S('I') is not { } i || S('O') is not { } o || Stems() is not { } stems || S('H') is not { } h || stems.Cross.Count == 0)
            {
                return null;
            }

            var width = Math.Max(1, m.Stem);
            var join = new Glyph(width, h.Height);
            foreach (var y in stems.Cross)
            {
                var colour = h[(stems.LeftEnd + stems.RightStart) / 2, y];
                for (var x = 0; x < width; x++)
                {
                    join[x, y] = colour;
                }
            }

            return Glyph.Join(Glyph.Join(i, join, 0), o, 0);
        }

        /// <summary>ю: a small stem joined to o at its middle.</summary>
        private Glyph? SmallYu()
        {
            if (Height(S('I'), m.SmallTop) is not { } i || S('o') is not { } o || Stems() is not { } stems || S('H') is not { } h || stems.Cross.Count == 0)
            {
                return null;
            }

            var (_, _, top, bottom) = o.Bounds();
            var middle = (top + bottom) / 2;
            var join = new Glyph(Math.Max(1, m.Stem), o.Height);
            for (var k = 0; k < stems.Cross.Count; k++)
            {
                var y = middle - (stems.Cross.Count / 2) + k;
                var colour = h[(stems.LeftEnd + stems.RightStart) / 2, stems.Cross[k]];
                for (var x = 0; x < join.Width; x++)
                {
                    join[x, y] = colour;
                }
            }

            return Glyph.Join(Glyph.Join(i, join, 0), o, 0);
        }

        /// <summary>Ф: O with I's stem through its middle.</summary>
        private Glyph? Ef()
        {
            if (S('O') is not { } o || S('I') is not { } i)
            {
                return null;
            }

            return Through(o, i, i.Bounds().T, i.Bounds().B);
        }

        /// <summary>ф: o with l's stem through it, carried below the baseline as p's is.</summary>
        private Glyph? SmallEf()
        {
            if (S('o') is not { } o || S('l') is not { } l || S('p') is not { } p)
            {
                return null;
            }

            var ef = Through(o, l, l.Bounds().T, l.Bounds().B);
            var (pLeft, _, _, pBottom) = p.Bounds();
            var center = ((o.Bounds().L + o.Bounds().R) / 2) - (m.Stem / 2);
            for (var y = m.Baseline; y <= pBottom; y++)
            {
                for (var x = 0; x < m.Stem; x++)
                {
                    ef[center + x, y] = p[pLeft + x, y];
                }
            }

            return ef;
        }

        private Glyph Through(Glyph round, Glyph stem, int top, int bottom)
        {
            var glyph = round.Clone();
            var (left, right, _, _) = round.Bounds();
            var center = ((left + right) / 2) - (m.Stem / 2);
            var middle = (top + bottom) / 2;
            var stemLeft = stem.FirstInk(middle);
            var run = Math.Max(1, Math.Min(m.Stem, stem.RunFrom(stemLeft, middle)));
            for (var y = top; y <= bottom; y++)
            {
                var from = stem.FirstInk(y) >= 0 && Math.Abs(stem.FirstInk(y) - stemLeft) <= 2 ? stem.FirstInk(y) : stemLeft;
                for (var x = 0; x < run; x++)
                {
                    if (stem.Ink(from + x, y))
                    {
                        glyph[center + x, y] = stem[from + x, y];
                    }
                }
            }

            return glyph;
        }

        /// <summary>Two dots over the letter, ë's, rows taken out of the letter if there is no room above it.</summary>
        private Glyph? Dots(Glyph? glyph)
        {
            if (glyph is null || S('ë') is not { } withDots || S('e') is not { } e)
            {
                return glyph;
            }

            var dots = withDots.Rows(0, e.Bounds().T - 1);
            if (dots.Bounds().L < 0)
            {
                return glyph;
            }

            return Above(glyph, dots);
        }

        /// <summary>A breve over the letter, drawn in the colour its top uses most.</summary>
        private Glyph? Breve(Glyph? glyph)
        {
            if (glyph is null)
            {
                return null;
            }

            var (left, right, top, _) = glyph.Bounds();
            var colour = glyph.MostUsed(top);
            var width = Math.Max(3, (right - left + 1) / 2);
            var breve = new Glyph(width, 2);
            breve[0, 0] = colour;
            breve[width - 1, 0] = colour;
            for (var x = 1; x < width - 1; x++)
            {
                breve[x, 1] = colour;
            }

            return Above(glyph, breve);
        }

        /// <summary>Ґ: Г with its bar's end turned up, where there is a row above it.</summary>
        private static Glyph? Upturn(Glyph? ge)
        {
            if (ge is null)
            {
                return null;
            }

            var (_, _, top, _) = ge.Bounds();
            if (top == 0)
            {
                return ge;
            }

            var upturn = ge.Clone();
            var end = ge.LastInk(top);
            upturn[end, top - 1] = ge[end, top];
            return upturn;
        }

        /// <summary>The mark centred a row above the letter, rows taken out of the letter to make room.</summary>
        private static Glyph Above(Glyph glyph, Glyph mark)
        {
            var (markLeft, markRight, markTop, markBottom) = mark.Bounds();
            var height = markBottom - markTop + 1;
            var (_, _, top, _) = glyph.Bounds();
            var target = glyph;
            if (top < height + 1)
            {
                target = glyph.ToTop(height + 1);
            }

            var (left, right, newTop, _) = target.Bounds();
            var x0 = ((left + right + 1) / 2) - ((markRight - markLeft + 1) / 2);
            var result = target.Clone();
            for (var y = 0; y < height; y++)
            {
                for (var x = markLeft; x <= markRight; x++)
                {
                    if (mark.Ink(x, markTop + y))
                    {
                        result[x0 + x - markLeft, newTop - 1 - height + y] = mark[x, markTop + y];
                    }
                }
            }

            return result;
        }
    }

    /// <summary>A glyph being made: palette indices, row by row, <see cref="Clear"/> where transparent.</summary>
    private sealed class Glyph(int width, int height)
    {
        private readonly byte[] _pixels = Enumerable.Repeat(Clear, width * height).ToArray();

        public int Width { get; } = width;

        public int Height { get; } = height;

        public byte this[int x, int y]
        {
            get => x >= 0 && y >= 0 && x < Width && y < Height ? _pixels[(y * Width) + x] : Clear;
            set
            {
                if (x >= 0 && y >= 0 && x < Width && y < Height)
                {
                    _pixels[(y * Width) + x] = value;
                }
            }
        }

        public static Glyph From(IndexedImage image)
        {
            var glyph = new Glyph(image.Width, image.Height);
            image.Pixels.CopyTo(glyph._pixels, 0);
            return glyph;
        }

        /// <summary>The second glyph's ink placed <paramref name="gap"/> columns after the first's.</summary>
        public static Glyph Join(Glyph first, Glyph second, int gap)
        {
            var (_, firstRight, _, _) = first.Bounds();
            var (secondLeft, secondRight, _, _) = second.Bounds();
            var offset = firstRight + 1 + gap - secondLeft;
            var joined = new Glyph(Math.Max(firstRight + 1, offset + second.Width), Math.Max(first.Height, second.Height));
            for (var y = 0; y < joined.Height; y++)
            {
                for (var x = 0; x <= firstRight; x++)
                {
                    joined[x, y] = first[x, y];
                }

                for (var x = secondLeft; x <= secondRight; x++)
                {
                    if (second.Ink(x, y))
                    {
                        joined[offset + x, y] = second[x, y];
                    }
                }
            }

            return joined;
        }

        public bool Ink(int x, int y) => this[x, y] != Clear;

        public Glyph Clone()
        {
            var clone = new Glyph(Width, Height);
            _pixels.CopyTo(clone._pixels, 0);
            return clone;
        }

        public bool SameAs(Glyph other) => Width == other.Width && Height == other.Height && _pixels.AsSpan().SequenceEqual(other._pixels);

        /// <summary>The inked area (right and bottom included); L is -1 for a blank glyph.</summary>
        public (int L, int R, int T, int B) Bounds()
        {
            int left = Width, right = -1, top = Height, bottom = -1;
            for (var y = 0; y < Height; y++)
            {
                for (var x = 0; x < Width; x++)
                {
                    if (Ink(x, y))
                    {
                        left = Math.Min(left, x);
                        right = Math.Max(right, x);
                        top = Math.Min(top, y);
                        bottom = y;
                    }
                }
            }

            return right < 0 ? (-1, -1, -1, -1) : (left, right, top, bottom);
        }

        public int FirstInk(int y)
        {
            for (var x = 0; x < Width; x++)
            {
                if (Ink(x, y))
                {
                    return x;
                }
            }

            return -1;
        }

        public int LastInk(int y)
        {
            for (var x = Width - 1; x >= 0; x--)
            {
                if (Ink(x, y))
                {
                    return x;
                }
            }

            return -1;
        }

        /// <summary>How many inked pixels follow on from (x, y), it included.</summary>
        public int RunFrom(int x, int y)
        {
            var run = 0;
            while (Ink(x + run, y))
            {
                run++;
            }

            return run;
        }

        public byte MostUsed(int y)
        {
            var counts = new Dictionary<byte, int>();
            for (var x = 0; x < Width; x++)
            {
                if (Ink(x, y))
                {
                    counts[this[x, y]] = counts.GetValueOrDefault(this[x, y]) + 1;
                }
            }

            return counts.Count == 0 ? Clear : counts.MaxBy(pair => pair.Value).Key;
        }

        /// <summary>Mirrored left to right within its ink, the spacing either side kept.</summary>
        public Glyph Mirror()
        {
            var (left, right, _, _) = Bounds();
            var mirror = new Glyph(Width, Height);
            for (var y = 0; y < Height; y++)
            {
                for (var x = left; x <= right; x++)
                {
                    mirror[left + right - x, y] = this[x, y];
                }
            }

            return mirror;
        }

        /// <summary>Each row moved right by <paramref name="slant"/> pixels for each row above <paramref name="baseRow"/>.</summary>
        public Glyph Shear(double slant, int baseRow)
        {
            var shifts = Enumerable.Range(0, Height).Select(y => (int)Math.Round(slant * (baseRow - y))).ToArray();
            var least = shifts.Min();
            var sheared = new Glyph(Width + shifts.Max() - least, Height);
            for (var y = 0; y < Height; y++)
            {
                for (var x = 0; x < Width; x++)
                {
                    sheared[x + shifts[y] - least, y] = this[x, y];
                }
            }

            return sheared;
        }

        /// <summary>The columns from x0 to x1, the rest cleared.</summary>
        public Glyph Columns(int x0, int x1)
        {
            var columns = new Glyph(Width, Height);
            for (var y = 0; y < Height; y++)
            {
                for (var x = x0; x <= x1; x++)
                {
                    columns[x, y] = this[x, y];
                }
            }

            return columns;
        }

        /// <summary>The rows from y0 to y1, the rest cleared.</summary>
        public Glyph Rows(int y0, int y1)
        {
            var rows = new Glyph(Width, Height);
            for (var y = Math.Max(0, y0); y <= Math.Min(Height - 1, y1); y++)
            {
                for (var x = 0; x < Width; x++)
                {
                    rows[x, y] = this[x, y];
                }
            }

            return rows;
        }

        /// <summary>Wider by so many clear columns on the left and the right.</summary>
        public Glyph Pad(int left, int right)
        {
            var padded = new Glyph(Width + left + right, Height);
            for (var y = 0; y < Height; y++)
            {
                for (var x = 0; x < Width; x++)
                {
                    padded[x + left, y] = this[x, y];
                }
            }

            return padded;
        }

        /// <summary>Its ink with so many clear columns before and after it, as the font's letters are spaced.</summary>
        public Glyph Fitted(int bearing, int padding)
        {
            var (left, right, _, _) = Bounds();
            var fitted = new Glyph(bearing + (right - left + 1) + padding, Height);
            for (var y = 0; y < Height; y++)
            {
                for (var x = left; x <= right; x++)
                {
                    fitted[bearing + x - left, y] = this[x, y];
                }
            }

            return fitted;
        }

        /// <summary>
        /// The glyph with its top on row <paramref name="top"/>, its foot staying where it is: rows taken out where it is
        /// lower (rows the same as the one above first, nearest the middle), or rows doubled where it is higher.
        /// </summary>
        public Glyph ToTop(int top)
        {
            var rows = Enumerable.Range(0, Height).Select(y => _pixels.AsSpan(y * Width, Width).ToArray()).ToList();
            var (_, _, current, bottom) = Bounds();
            while (current < top && bottom - current > 1)
            {
                // The row most like the one above it goes, nearest the middle of the letter where several are.
                var middle = (current + bottom) / 2.0;
                var pick = Enumerable.Range(current + 1, Math.Max(0, bottom - current - 1))
                    .OrderBy(y => Difference(rows[y], rows[y - 1]))
                    .ThenBy(y => Math.Abs(y - middle))
                    .First();
                rows.RemoveAt(pick);
                rows.Insert(0, Enumerable.Repeat(Clear, Width).ToArray());
                current++;
            }

            while (current > top && current > 0)
            {
                // Double the row below the top, a stem's.
                var copy = rows[Math.Min(current + 1, bottom)].ToArray();
                rows.Insert(current + 1, copy);
                rows.RemoveAt(0);
                current--;
            }

            var glyph = new Glyph(Width, Height);
            for (var y = 0; y < Height; y++)
            {
                rows[y].CopyTo(glyph._pixels, y * Width);
            }

            return glyph;
        }

        /// <summary>How many pixels are inked in one row and not the other.</summary>
        private static int Difference(byte[] a, byte[] b)
        {
            var count = 0;
            for (var x = 0; x < a.Length; x++)
            {
                if ((a[x] == Clear) != (b[x] == Clear))
                {
                    count++;
                }
            }

            return count;
        }

        public IndexedImage ToImage() => new(Width, Height, _pixels.ToArray());
    }
}
