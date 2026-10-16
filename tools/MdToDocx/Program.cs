// MdToDocx: минимальный конвертер Markdown -> DOCX (заголовки, таблицы, списки,
// жирный, код, цитаты, колонтитул "Стр. X из Y"). Использование:
//   MdToDocx <input.md> <output.docx>
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

static class MdToDocx
{
    public static int Main(string[] args)
    {
        if (args.Length < 2) { Console.WriteLine("Usage: MdToDocx <input.md> <output.docx>"); return 1; }
        string mdPath = args[0], docxPath = args[1];
        if (!File.Exists(mdPath)) { Console.WriteLine("Input not found: " + mdPath); return 2; }

        var lines = File.ReadAllLines(mdPath);

        using var doc = WordprocessingDocument.Create(docxPath, WordprocessingDocumentType.Document);
        var main = doc.AddMainDocumentPart();
        main.Document = new Document(new Body());
        var body = main.Document.Body!;
        AddStyles(doc);
        AddNumbering(doc);
        AddFooter(doc, "Стр. ", " из ");

        var paras = new List<OpenXmlElement>();
        int i = 0;
        while (i < lines.Length)
        {
            string line = lines[i].TrimEnd();
            string t = line.Trim();
            if (t.Length == 0) { i++; continue; }

            // Таблица: строка-заголовок + строка-разделитель
            if (t.StartsWith("|") && i + 1 < lines.Length && IsTableDelim(lines[i + 1]))
            {
                var rows = new List<string[]> { SplitRow(t) };
                i += 2;
                while (i < lines.Length && lines[i].Trim().StartsWith("|"))
                {
                    rows.Add(SplitRow(lines[i].Trim()));
                    i++;
                }
                paras.Add(BuildTable(rows));
                continue;
            }
            // Заголовки
            var hm = Regex.Match(t, @"^(#{1,4})\s+(.*)$");
            if (hm.Success)
            {
                int level = hm.Groups[1].Value.Length;
                string style = level == 1 ? "Title" : "Heading" + (level - 1);
                paras.Add(BuildParagraph(ParseInline(hm.Groups[2].Value), style,
                    level == 1 ? JustificationValues.Center : JustificationValues.Left));
                i++;
                continue;
            }
            // Разделитель
            if (Regex.IsMatch(t, @"^(-{3,}|\*{3,})$"))
            {
                paras.Add(BuildParagraph(new List<RunSpec> { new RunSpec("", false, false) }, "Normal",
                    JustificationValues.Left, withBottomBorder: true));
                i++;
                continue;
            }
            // Цитата
            if (t.StartsWith(">"))
            {
                paras.Add(BuildParagraph(ParseInline(t.TrimStart('>').Trim()), "Normal",
                    JustificationValues.Left, indentLeft: 720, italic: true));
                i++;
                continue;
            }
            // Маркированный список
            var bm = Regex.Match(line, @"^(\s*)[-*]\s+(.*)$");
            if (bm.Success)
            {
                int lvl = Math.Min(bm.Groups[1].Value.Length / 2, 1);
                paras.Add(BuildListParagraph(StripMarker(bm.Groups[2].Value), numId: 1, level: lvl));
                i++;
                continue;
            }
            // Нумерованный список ("N. текст", но не "9.1.1." — остаток не с цифры)
            var nm = Regex.Match(line, @"^(\s*)(\d{1,3})([.)])\s+(\D.*)$");
            if (nm.Success)
            {
                int lvl = Math.Min(nm.Groups[1].Value.Length / 2, 1);
                paras.Add(BuildListParagraph(nm.Groups[4].Value, numId: 2, level: lvl));
                i++;
                continue;
            }
            // Обычный абзац
            paras.Add(BuildParagraph(ParseInline(t), "Normal", JustificationValues.Both));
            i++;
        }

        foreach (var p in paras) body.Append(p);
        main.Document.Save();
        Console.WriteLine($"OK: {docxPath} ({paras.Count} blocks)");
        return 0;
    }

    static bool IsTableDelim(string line)
    {
        string t = line.Trim();
        return t.StartsWith("|") && t.Trim('|', ' ', '-', ':').Length == 0 && t.Contains("-");
    }

    static string[] SplitRow(string line)
    {
        string t = line.Trim();
        if (t.StartsWith("|")) t = t[1..];
        if (t.EndsWith("|")) t = t[..^1];
        return t.Split('|').Select(c => c.Trim()).ToArray();
    }

    static string StripMarker(string s)
    {
        var m = Regex.Match(s, @"^\d{1,3}[.)]\s+(.*)$");
        return m.Success ? m.Groups[1].Value : s;
    }

    static List<RunSpec> ParseInline(string s)
    {
        var list = new List<RunSpec>();
        s = Regex.Replace(s, @"\[(.+?)\]\(.+?\)", "$1"); // ссылки [t](u) -> t
        var parts = Regex.Split(s, @"(\*\*.+?\*\*|`[^`]+`)");
        foreach (var p in parts)
        {
            if (p.Length == 0) continue;
            if (p.StartsWith("**") && p.EndsWith("**") && p.Length > 4)
                list.Add(new RunSpec(p[2..^2], true, false));
            else if (p.StartsWith("`") && p.EndsWith("`") && p.Length > 2)
                list.Add(new RunSpec(p[1..^1], false, true));
            else
                list.Add(new RunSpec(p, false, false));
        }
        return list;
    }

    static Paragraph BuildParagraph(List<RunSpec> runs, string style,
        JustificationValues align, int indentLeft = 0, bool italic = false, bool withBottomBorder = false)
    {
        var pp = new ParagraphProperties(new ParagraphStyleId { Val = style });
        pp.Append(new Justification { Val = align });
        pp.Append(new SpacingBetweenLines { After = "160", Line = "276", LineRule = LineSpacingRuleValues.Auto });
        if (indentLeft > 0) pp.Append(new Indentation { Left = indentLeft.ToString() });
        if (withBottomBorder)
            pp.Append(new ParagraphBorders(new BottomBorder
            { Val = BorderValues.Single, Size = 6, Space = 4, Color = "999999" }));
        var p = new Paragraph(pp);
        foreach (var r in runs) p.Append(BuildRun(r, italic));
        return p;
    }

    static Paragraph BuildListParagraph(string text, int numId, int level)
    {
        var pp = new ParagraphProperties(new ParagraphStyleId { Val = "Normal" });
        pp.Append(new NumberingProperties(
            new NumberingLevelReference { Val = level },
            new NumberingId { Val = numId }));
        pp.Append(new SpacingBetweenLines { After = "80", Line = "276", LineRule = LineSpacingRuleValues.Auto });
        var p = new Paragraph(pp);
        foreach (var r in ParseInline(text)) p.Append(BuildRun(r, false));
        return p;
    }

    static Run BuildRun(RunSpec r, bool italic)
    {
        var rp = new RunProperties();
        if (r.Bold) rp.Append(new Bold());
        if (italic) rp.Append(new Italic());
        if (r.Code)
        {
            rp.Append(new RunFonts { Ascii = "Consolas", HighAnsi = "Consolas" });
            rp.Append(new FontSize { Val = "21" });
        }
        var run = new Run(rp);
        run.Append(new Text(r.Text) { Space = SpaceProcessingModeValues.Preserve });
        return run;
    }

    static Table BuildTable(List<string[]> rows)
    {
        int cols = rows.Max(r => r.Length);
        var tbl = new Table();
        tbl.Append(new TableProperties(
            new TableStyle { Val = "TableGrid" },
            new TableWidth { Width = "0", Type = TableWidthUnitValues.Auto },
            new TableBorders(
                new TopBorder { Val = BorderValues.Single, Size = 4, Color = "000000" },
                new LeftBorder { Val = BorderValues.Single, Size = 4, Color = "000000" },
                new BottomBorder { Val = BorderValues.Single, Size = 4, Color = "000000" },
                new RightBorder { Val = BorderValues.Single, Size = 4, Color = "000000" },
                new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4, Color = "000000" },
                new InsideVerticalBorder { Val = BorderValues.Single, Size = 4, Color = "000000" })));
        var grid = new TableGrid();
        for (int c = 0; c < cols; c++) grid.Append(new GridColumn { Width = (9638 / cols).ToString() });
        tbl.Append(grid);
        for (int ri = 0; ri < rows.Count; ri++)
        {
            var tr = new TableRow();
            for (int c = 0; c < cols; c++)
            {
                string cell = c < rows[ri].Length ? rows[ri][c] : "";
                var tc = new TableCell();
                var tcp = new TableCellProperties(new TableCellWidth { Width = "0", Type = TableWidthUnitValues.Auto });
                if (ri == 0) tcp.Append(new Shading
                { Val = ShadingPatternValues.Clear, Fill = "D9D9D9" });
                tc.Append(tcp);
                var cp = new Paragraph(new ParagraphProperties(
                    new ParagraphStyleId { Val = "Normal" },
                    new SpacingBetweenLines { After = "40", Line = "240", LineRule = LineSpacingRuleValues.Auto }));
                bool any = false;
                foreach (var r in ParseInline(cell))
                {
                    var rp = new RunProperties();
                    if (ri == 0 || r.Bold) rp.Append(new Bold());
                    if (r.Code)
                    {
                        rp.Append(new RunFonts { Ascii = "Consolas", HighAnsi = "Consolas" });
                        rp.Append(new FontSize { Val = "20" });
                    }
                    var run = new Run(rp);
                    run.Append(new Text(r.Text) { Space = SpaceProcessingModeValues.Preserve });
                    cp.Append(run);
                    any = true;
                }
                if (!any) cp.Append(new Run(new Text("")));
                tc.Append(cp);
                tr.Append(tc);
            }
            tbl.Append(tr);
        }
        return tbl;
    }

    static void AddStyles(WordprocessingDocument doc)
    {
        var sp = doc.MainDocumentPart!.AddNewPart<StyleDefinitionsPart>();
        sp.Styles = new Styles(
            NewTextStyle("Normal", "Обычный", "24", false, false),
            NewTextStyle("Title", "Заголовок", "36", true, false, JustificationValues.Center),
            NewTextStyle("Heading1", "Заголовок 1", "32", true, false),
            NewTextStyle("Heading2", "Заголовок 2", "28", true, false),
            NewTextStyle("Heading3", "Заголовок 3", "26", true, true));
        sp.Styles.Save();
        // A4 + поля 2 см
        doc.MainDocumentPart.Document.Body!.PrependChild(new SectionProperties(
            new PageSize { Width = 11906, Height = 16838 },
            new PageMargin { Top = 1134, Bottom = 1134, Left = 1134, Right = 1134, Header = 708, Footer = 708 }));
    }

    static Style NewTextStyle(string id, string name, string sizeHalfPt, bool bold, bool italic,
        JustificationValues? align = null)
    {
        var st = new Style(
            new StyleName { Val = name },
            new StyleRunProperties(
                new RunFonts { Ascii = "Times New Roman", HighAnsi = "Times New Roman" },
                new FontSize { Val = sizeHalfPt },
                new Languages { Val = "ru-RU" }))
        {
            Type = StyleValues.Paragraph,
            StyleId = id,
            CustomStyle = true,
        };
        if (bold) st.StyleRunProperties!.Append(new Bold());
        if (italic) st.StyleRunProperties!.Append(new Italic());
        st.Append(new StyleParagraphProperties(new Justification { Val = align ?? JustificationValues.Left }));
        return st;
    }

    static void AddNumbering(WordprocessingDocument doc)
    {
        var np = doc.MainDocumentPart!.AddNewPart<NumberingDefinitionsPart>();
        var anBullet = new AbstractNum(
            new Level(
                new StartNumberingValue { Val = 1 },
                new NumberingFormat { Val = NumberFormatValues.Bullet },
                new LevelText { Val = "•" },
                new ParagraphProperties(new Indentation { Left = "720", Hanging = "360" })) { LevelIndex = 0 },
            new Level(
                new StartNumberingValue { Val = 1 },
                new NumberingFormat { Val = NumberFormatValues.Bullet },
                new LevelText { Val = "–" },
                new ParagraphProperties(new Indentation { Left = "1440", Hanging = "360" })) { LevelIndex = 1 })
        { AbstractNumberId = 1 };
        var anDec = new AbstractNum(
            new Level(
                new StartNumberingValue { Val = 1 },
                new NumberingFormat { Val = NumberFormatValues.Decimal },
                new LevelText { Val = "%1." },
                new ParagraphProperties(new Indentation { Left = "720", Hanging = "360" })) { LevelIndex = 0 },
            new Level(
                new StartNumberingValue { Val = 1 },
                new NumberingFormat { Val = NumberFormatValues.LowerLetter },
                new LevelText { Val = "%2)" },
                new ParagraphProperties(new Indentation { Left = "1440", Hanging = "360" })) { LevelIndex = 1 })
        { AbstractNumberId = 2 };
        np.Numbering = new Numbering(anBullet, anDec,
            new NumberingInstance(new AbstractNumId { Val = 1 }) { NumberID = 1 },
            new NumberingInstance(new AbstractNumId { Val = 2 }) { NumberID = 2 });
        np.Numbering.Save();
    }

    static void AddFooter(WordprocessingDocument doc, string before, string middle)
    {
        var fp = doc.MainDocumentPart!.AddNewPart<FooterPart>();
        var p = new Paragraph(new ParagraphProperties(
            new ParagraphStyleId { Val = "Normal" },
            new Justification { Val = JustificationValues.Center }));
        p.Append(new Run(new Text(before) { Space = SpaceProcessingModeValues.Preserve }));
        p.Append(FieldRun("PAGE"));
        p.Append(new Run(new Text(middle) { Space = SpaceProcessingModeValues.Preserve }));
        p.Append(FieldRun("NUMPAGES"));
        fp.Footer = new Footer(p);
        fp.Footer.Save();
        var sect = doc.MainDocumentPart.Document.Body!.Elements<SectionProperties>().First();
        sect.PrependChild(new FooterReference { Type = HeaderFooterValues.Default, Id = doc.MainDocumentPart.GetIdOfPart(fp) });
    }

    static Run FieldRun(string instr)
    {
        var r = new Run();
        r.Append(new FieldChar { FieldCharType = FieldCharValues.Begin });
        r.Append(new FieldCode(instr) { Space = SpaceProcessingModeValues.Preserve });
        r.Append(new FieldChar { FieldCharType = FieldCharValues.Separate });
        r.Append(new Text("1") { Space = SpaceProcessingModeValues.Preserve });
        r.Append(new FieldChar { FieldCharType = FieldCharValues.End });
        return r;
    }

    sealed class RunSpec
    {
        public readonly string Text;
        public readonly bool Bold;
        public readonly bool Code;
        public RunSpec(string text, bool bold, bool code) { Text = text; Bold = bold; Code = code; }
    }
}
