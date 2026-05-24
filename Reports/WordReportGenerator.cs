// ============================================================
//  WORD REPORT GENERATOR — DocX (Xceed)
//  Archivo: Reports/WordReportGenerator.cs
//
//  Secciones del reporte (mismo orden que el PDF):
//    1. Portada con estilos
//    2. Información del experimento y propiedades del agua
//    3. Antecedente
//    4. Imágenes de microscopio (antes / después)
//    5. Resultados finales (tabla)
//    6. Gráfico de control X̄-R (tabla de valores)
//    7. Tabla completa de mediciones (sin límite de filas)
//    8. Alertas registradas
//    9. Observaciones
// ============================================================

using Xceed.Document.NET;
using Xceed.Words.NET;
using System.Drawing;

namespace CavitationApi.Reports;

public class WordReportGenerator
{
    // ── Colores ───────────────────────────────────────────────
    private static readonly Color PrimaryBlue  = Color.FromArgb(0,   82, 155);
    private static readonly Color AccentTeal   = Color.FromArgb(0,  158, 115);
    private static readonly Color WarnOrange   = Color.FromArgb(230, 159,   0);
    private static readonly Color DangerRed    = Color.FromArgb(213,  94,   0);
    private static readonly Color LightGray    = Color.FromArgb(245, 245, 245);
    private static readonly Color MediumGray   = Color.FromArgb(180, 180, 180);
    private static readonly Color HeaderBg     = Color.FromArgb(0,   82, 155);

    public byte[] Generate(ReportData data)
    {
        using var ms  = new MemoryStream();
        using var doc = DocX.Create(ms);

        // Márgenes (en puntos; 720 = 2.54 cm aprox.)
        doc.MarginLeft   = 720;
        doc.MarginRight  = 720;
        doc.MarginTop    = 720;
        doc.MarginBottom = 720;

        AddCoverPage(doc, data);
        AddPageBreak(doc);

        AddSectionTitle(doc, "1. Información del Experimento");
        AddExperimentInfo(doc, data);

        AddSectionTitle(doc, "2. Propiedades de la Muestra de Agua");
        AddWaterProperties(doc, data);

        AddSectionTitle(doc, "3. Antecedente del Proceso");
        AddPrecedentSection(doc, data);

        AddPageBreak(doc);
        AddSectionTitle(doc, "4. Comparación Microscópica — Antes / Después");
        AddMicroscopeImages(doc, data);

        AddPageBreak(doc);
        AddSectionTitle(doc, "5. Resultados Finales");
        AddFinalResults(doc, data);

        if (data.Subgroups.Any())
        {
            AddPageBreak(doc);
            AddSectionTitle(doc, "6. Gráfico de Control de Medias (X̄) y Rangos (R)");
            AddControlChartSummary(doc, data);
            AddControlChartTable(doc, data);
        }

        AddPageBreak(doc);
        AddSectionTitle(doc, "7. Historial Completo de Mediciones");
        AddMeasurementsTable(doc, data);

        if (data.Alerts.Any())
        {
            AddPageBreak(doc);
            AddSectionTitle(doc, "8. Alertas Registradas");
            AddAlertsTable(doc, data);
        }

        AddSectionTitle(doc, "9. Observaciones y Conclusiones");
        AddObservations(doc, data);

        AddFooter(doc, data);

        doc.Save();
        return ms.ToArray();
    }

    // ── 1. Portada ────────────────────────────────────────────

    private void AddCoverPage(DocX doc, ReportData data)
    {
        // Título principal
        var title = doc.InsertParagraph("REPORTE DE EXPERIMENTO");
        title.StyleName = "Heading1";
        title.Alignment = Alignment.center;
        title.Color(PrimaryBlue);
        title.FontSize(28);
        title.Bold();
        title.SpacingAfter(6);

        var subtitle = doc.InsertParagraph("SISTEMA DE CAVITACIÓN");
        subtitle.Alignment = Alignment.center;
        subtitle.Color(AccentTeal);
        subtitle.FontSize(16);
        subtitle.Bold();
        subtitle.SpacingAfter(20);

        // Línea horizontal
        var separator = doc.InsertParagraph(new string('─', 60));
        separator.Alignment = Alignment.center;
        separator.Color(PrimaryBlue);
        separator.SpacingAfter(20);

        // Tabla de datos del experimento
        var table = doc.AddTable(8, 2);
        table.Design = TableDesign.None;
        table.Alignment = Alignment.center;

        var rows = new[]
        {
            ("Experimento:",   data.ExperimentName),
            ("Muestra:",       data.SampleName),
            ("Máquina:",       data.MachineName),
            ("Operario:",      data.OperatorName),
            ("Fecha inicio:",  data.StartTime.ToString("dd/MM/yyyy HH:mm")),
            ("Duración plan.", FormatDuration(data.PlannedDuration)),
            ("Estado:",        data.Status),
            ("Generado:",      data.GeneratedAt.ToString("dd/MM/yyyy HH:mm"))
        };

        for (int i = 0; i < rows.Length; i++)
        {
            SetCell(table.Rows[i].Cells[0], rows[i].Item1, bold: true, color: PrimaryBlue, fontSize: 11);
            SetCell(table.Rows[i].Cells[1], rows[i].Item2, fontSize: 11);
            table.Rows[i].Cells[0].Width = 200;
            table.Rows[i].Cells[1].Width = 320;
        }

        doc.InsertTable(table);

        doc.InsertParagraph("\n");
        var footNote = doc.InsertParagraph(
            $"Reporte generado por: {data.GeneratedBy}  |  {data.GeneratedAt:dd/MM/yyyy HH:mm}");
        footNote.Alignment = Alignment.center;
        footNote.Color(Color.Gray);
        footNote.FontSize(9);
        footNote.Italic();
    }

    // ── 2. Información del experimento ────────────────────────

    private void AddExperimentInfo(DocX doc, ReportData data)
    {
        var table = doc.AddTable(11, 2);
        table.Design = TableDesign.None;

        var actualDuration = data.EndTime.HasValue
            ? data.EndTime.Value - data.StartTime
            : TimeSpan.Zero;

        var rows = new[]
        {
            ("Nombre del experimento",   data.ExperimentName),
            ("Código de muestra",        data.SampleName),
            ("Descripción de muestra",   data.SampleDescription),
            ("Máquina utilizada",        data.MachineName),
            ("Operario responsable",     data.OperatorName),
            ("Fecha y hora de inicio",   data.StartTime.ToString("dd/MM/yyyy HH:mm:ss")),
            ("Fecha y hora de fin",      data.EndTime?.ToString("dd/MM/yyyy HH:mm:ss") ?? "—"),
            ("Duración planificada",     FormatDuration(data.PlannedDuration)),
            ("Duración real",            actualDuration > TimeSpan.Zero ? FormatDuration(actualDuration) : "—"),
            ("Estado final",             data.Status),
            ("Motivo de aborte",         string.IsNullOrEmpty(data.AbortReason) ? "—" : data.AbortReason),
        };

        for (int i = 0; i < rows.Length; i++)
        {
            SetCell(table.Rows[i].Cells[0], rows[i].Item1, bold: true, bgColor: LightGray);
            SetCell(table.Rows[i].Cells[1], rows[i].Item2,
                color: (i == 10 && !string.IsNullOrEmpty(data.AbortReason)) ? DangerRed : Color.Black);
            table.Rows[i].Cells[0].Width = 210;
            table.Rows[i].Cells[1].Width = 310;
        }

        doc.InsertTable(table);
        doc.InsertParagraph(" ");
    }

    // ── 3. Propiedades del agua ───────────────────────────────

    private void AddWaterProperties(DocX doc, ReportData data)
    {
        var table = doc.AddTable(3, 2);
        table.Design = TableDesign.None;

        var rows = new[]
        {
            ("Temperatura inicial",  $"{data.InitialTemperature:F2} °C"),
            ("Caudal objetivo",      $"{data.TargetFlowRate:F3} L/min"),
            ("Tolerancia de caudal", $"±{data.FlowRateTolerance * 100:F1}%"),
        };

        for (int i = 0; i < rows.Length; i++)
        {
            SetCell(table.Rows[i].Cells[0], rows[i].Item1, bold: true, bgColor: LightGray);
            SetCell(table.Rows[i].Cells[1], rows[i].Item2);
            table.Rows[i].Cells[0].Width = 210;
            table.Rows[i].Cells[1].Width = 310;
        }

        doc.InsertTable(table);
        doc.InsertParagraph(" ");
    }

    // ── 4. Antecedente ────────────────────────────────────────

    private void AddPrecedentSection(DocX doc, ReportData data)
    {
        if (data.HasPrecedent && !string.IsNullOrEmpty(data.PrecedentExperimentName))
        {
            var p = doc.InsertParagraph(
                $"Esta muestra ha sido procesada previamente en el experimento: « {data.PrecedentExperimentName} »");
            p.Color(PrimaryBlue);
            p.FontSize(10);
        }
        else
        {
            var p = doc.InsertParagraph(
                "Esta es la primera vez que la muestra es sometida al proceso de cavitación. No existe experimento antecedente.");
            p.Color(AccentTeal);
            p.FontSize(10);
        }

        doc.InsertParagraph(" ");
    }

    // ── 5. Imágenes de microscopio ────────────────────────────

    private void AddMicroscopeImages(DocX doc, ReportData data)
    {
        bool hasBefore = !string.IsNullOrEmpty(data.MicroscopeImageBeforePath)
                         && File.Exists(data.MicroscopeImageBeforePath);
        bool hasAfter  = !string.IsNullOrEmpty(data.MicroscopeImageAfterPath)
                         && File.Exists(data.MicroscopeImageAfterPath);

        if (!hasBefore && !hasAfter)
        {
            var noImg = doc.InsertParagraph("No se registraron imágenes de microscopio para este experimento.");
            noImg.Color(Color.Gray);
            noImg.Italic();
            doc.InsertParagraph(" ");
            return;
        }

        // Tabla de 2 columnas para las imágenes
        var imgTable = doc.AddTable(2, 2);
        imgTable.Design = TableDesign.None;

        // Encabezados
        SetCell(imgTable.Rows[0].Cells[0], "ANTES DEL PROCESO",
            bold: true, centered: true, bgColor: HeaderBg, color: Color.White);
        SetCell(imgTable.Rows[0].Cells[1], "DESPUÉS DEL PROCESO",
            bold: true, centered: true, bgColor: HeaderBg, color: Color.White);

        // Imágenes
        InsertImageInCell(doc, imgTable.Rows[1].Cells[0],
            data.MicroscopeImageBeforePath, hasBefore);
        InsertImageInCell(doc, imgTable.Rows[1].Cells[1],
            data.MicroscopeImageAfterPath, hasAfter);

        imgTable.Rows[0].Cells[0].Width = 260;
        imgTable.Rows[0].Cells[1].Width = 260;
        imgTable.Rows[1].Cells[0].Width = 260;
        imgTable.Rows[1].Cells[1].Width = 260;

        doc.InsertTable(imgTable);
        doc.InsertParagraph(" ");
    }

    private void InsertImageInCell(DocX doc, Cell cell, string? path, bool exists)
    {
        if (exists && path is not null)
        {
            try
            {
                var picture = doc.AddImage(path).CreatePicture(180, 130);
                var p       = cell.Paragraphs[0];
                p.Alignment = Alignment.center;
                p.AppendPicture(picture);
            }
            catch
            {
                SetCell(null!, cell, "[Error al cargar imagen]", color: DangerRed, italic: true);
            }
        }
        else
        {
            SetCell(null!, cell, "Imagen no disponible", color: Color.Gray, italic: true, centered: true);
        }
    }

    // ── 6. Resultados finales ─────────────────────────────────

    private void AddFinalResults(DocX doc, ReportData data)
    {
        var table = doc.AddTable(14, 3);
        table.Design = TableDesign.None;

        // Encabezados
        SetCell(table.Rows[0].Cells[0], "Parámetro",  bold: true, bgColor: HeaderBg, color: Color.White);
        SetCell(table.Rows[0].Cells[1], "Valor",       bold: true, bgColor: HeaderBg, color: Color.White, centered: true);
        SetCell(table.Rows[0].Cells[2], "Unidad",      bold: true, bgColor: HeaderBg, color: Color.White, centered: true);
        table.Rows[0].Cells[0].Width = 270;
        table.Rows[0].Cells[1].Width = 120;
        table.Rows[0].Cells[2].Width = 130;

        var complianceColor = data.FlowRateCompliance >= 95 ? AccentTeal
                            : data.FlowRateCompliance >= 80 ? WarnOrange
                            : DangerRed;

        var resultRows = new[]
        {
            ("Temperatura inicial",              $"{data.InitialTemperature:F2}",           "°C",    (Color?)null),
            ("Temperatura final",                $"{data.FinalTemperature:F2}",             "°C",    null),
            ("Temperatura máxima alcanzada",     $"{data.MaxTemperatureReached:F2}",        "°C",    null),
            ("— — —",                            "— — —",                                   "— —",   null),
            ("Caudal objetivo",                  $"{data.TargetFlowRate:F3}",               "L/min", null),
            ("Caudal promedio",                  $"{data.AverageFlowRate:F3}",              "L/min", null),
            ("Caudal mínimo",                    $"{data.MinFlowRateReached:F3}",           "L/min", null),
            ("Caudal máximo",                    $"{data.MaxFlowRateReached:F3}",           "L/min", null),
            ("Cumplimiento del caudal",          $"{data.FlowRateCompliance:F1}",           "%",     complianceColor),
            ("— — —",                            "— — —",                                   "— —",   null),
            ("Media de control X̄",              $"{data.FlowRateControlMean:F4}",          "L/min", null),
            ("Límite superior de control (UCL)", $"{data.FlowRateUpperControlLimit:F4}",   "L/min", null),
            ("Límite inferior de control (LCL)", $"{data.FlowRateLowerControlLimit:F4}",   "L/min", null),
        };

        for (int i = 0; i < resultRows.Length; i++)
        {
            var row   = table.Rows[i + 1];
            var (param, val, unit, clr) = resultRows[i];
            var isSep = param.StartsWith("— — —");

            SetCell(row.Cells[0], param, bold: !isSep,
                bgColor: isSep ? MediumGray : LightGray,
                color: isSep ? Color.Gray : Color.Black);
            SetCell(row.Cells[1], val, centered: true, bold: !isSep,
                color: clr ?? (isSep ? Color.Gray : Color.Black),
                bgColor: isSep ? MediumGray : Color.White);
            SetCell(row.Cells[2], unit, centered: true, italic: !isSep,
                color: isSep ? Color.Gray : Color.Gray,
                bgColor: isSep ? MediumGray : Color.White);

            row.Cells[0].Width = 270;
            row.Cells[1].Width = 120;
            row.Cells[2].Width = 130;
        }

        doc.InsertTable(table);
        doc.InsertParagraph(" ");
    }

    // ── 7. Resumen y tabla de control X̄-R ───────────────────

    private void AddControlChartSummary(DocX doc, ReportData data)
    {
        var summaryRows = new[]
        {
            ("Gran media (X̄)",             $"{data.ControlChartGrandMean:F4} L/min"),
            ("UCL — Medias",               $"{data.ControlChartUCL:F4} L/min"),
            ("LCL — Medias",               $"{data.ControlChartLCL:F4} L/min"),
            ("Rango promedio (R̄)",          $"{data.ControlChartAvgRange:F4}"),
            ("UCL — Rangos",               $"{data.ControlChartUCLRange:F4}"),
            ("LCL — Rangos",               $"{data.ControlChartLCLRange:F4}"),
            ("Total subgrupos",            data.Subgroups.Count.ToString()),
            ("Subgrupos fuera de control", data.Subgroups.Count(s => s.AboveUCL || s.BelowLCL).ToString()),
        };

        var table = doc.AddTable(summaryRows.Length, 2);
        table.Design = TableDesign.None;

        for (int i = 0; i < summaryRows.Length; i++)
        {
            SetCell(table.Rows[i].Cells[0], summaryRows[i].Item1, bold: true, bgColor: LightGray);
            SetCell(table.Rows[i].Cells[1], summaryRows[i].Item2);
            table.Rows[i].Cells[0].Width = 240;
            table.Rows[i].Cells[1].Width = 200;
        }

        doc.InsertTable(table);
        doc.InsertParagraph(" ");
    }

    private void AddControlChartTable(DocX doc, ReportData data)
    {
        var subLabel = doc.InsertParagraph("Valores de subgrupos X̄-R:");
        subLabel.Bold();
        subLabel.FontSize(10);
        subLabel.SpacingAfter(4);

        // Encabezados
        var headers = new[] { "Subgrupo", "X̄ (Media)", "R (Rango)", "Timestamp", "Estado X̄", "Estado R" };
        var table   = doc.AddTable(data.Subgroups.Count + 1, headers.Length);
        table.Design = TableDesign.None;

        for (int h = 0; h < headers.Length; h++)
        {
            SetCell(table.Rows[0].Cells[h], headers[h],
                bold: true, bgColor: HeaderBg, color: Color.White, centered: true, fontSize: 9);
            table.Rows[0].Cells[h].Width = h == 3 ? 120 : 80;
        }

        for (int i = 0; i < data.Subgroups.Count; i++)
        {
            var sg     = data.Subgroups[i];
            var outCtrl = sg.AboveUCL || sg.BelowLCL;
            var ucl    = data.ControlChartUCL;
            var lclR   = data.ControlChartLCLRange;
            var uclR   = data.ControlChartUCLRange;
            var outR   = sg.Range > uclR || sg.Range < lclR;

            var row = table.Rows[i + 1];
            SetCell(row.Cells[0], sg.SubgroupNumber.ToString(),  centered: true, fontSize: 9);
            SetCell(row.Cells[1], $"{sg.Mean:F4}",               centered: true, fontSize: 9,
                color: outCtrl ? DangerRed : Color.Black);
            SetCell(row.Cells[2], $"{sg.Range:F4}",              centered: true, fontSize: 9,
                color: outR ? DangerRed : Color.Black);
            SetCell(row.Cells[3], sg.Timestamp.ToString("HH:mm:ss"), centered: true, fontSize: 9);
            SetCell(row.Cells[4], outCtrl ? "⚠ FUERA" : "✓ OK", centered: true, fontSize: 9,
                color: outCtrl ? DangerRed : AccentTeal, bold: outCtrl);
            SetCell(row.Cells[5], outR ? "⚠ FUERA" : "✓ OK",    centered: true, fontSize: 9,
                color: outR ? DangerRed : AccentTeal, bold: outR);

            for (int c = 0; c < headers.Length; c++)
                row.Cells[c].Width = c == 3 ? 120 : 80;
        }

        doc.InsertTable(table);
        doc.InsertParagraph(" ");
    }

    // ── 8. Tabla completa de mediciones ──────────────────────

    private void AddMeasurementsTable(DocX doc, ReportData data)
    {
        var note = doc.InsertParagraph(
            $"Total de mediciones registradas: {data.Measurements.Count}");
        note.Color(Color.Gray);
        note.Italic();
        note.FontSize(9);
        note.SpacingAfter(6);

        var headers = new[] { "#", "Timestamp", "Temp (°C)", "Q (L/min)", "Q Obj.", "Desv.%", "Presión" };
        var table   = doc.AddTable(data.Measurements.Count + 1, headers.Length);
        table.Design = TableDesign.None;

        for (int h = 0; h < headers.Length; h++)
        {
            SetCell(table.Rows[0].Cells[h], headers[h],
                bold: true, bgColor: HeaderBg, color: Color.White, centered: true, fontSize: 9);
        }

        SetColumnWidths(table.Rows[0], 35, 100, 70, 70, 70, 60, 70);

        for (int i = 0; i < data.Measurements.Count; i++)
        {
            var m      = data.Measurements[i];
            var devPct = m.FlowDeviation * 100;
            var devClr = devPct > 10 ? DangerRed : devPct > 5 ? WarnOrange : Color.Black;
            var row    = table.Rows[i + 1];
            var bgClr  = i % 2 == 0 ? Color.White : LightGray;

            SetCell(row.Cells[0], m.Index.ToString(),                    centered: true, fontSize: 8, bgColor: bgClr);
            SetCell(row.Cells[1], m.Timestamp.ToString("HH:mm:ss"),      centered: true, fontSize: 8, bgColor: bgClr);
            SetCell(row.Cells[2], $"{m.Temperature:F1}",                 centered: true, fontSize: 8, bgColor: bgClr);
            SetCell(row.Cells[3], $"{m.FlowRate:F3}",                    centered: true, fontSize: 8, bgColor: bgClr);
            SetCell(row.Cells[4], $"{m.FlowRateTarget:F3}",              centered: true, fontSize: 8, bgColor: bgClr);
            SetCell(row.Cells[5], $"{devPct:F1}%",                       centered: true, fontSize: 8, color: devClr, bgColor: bgClr);
            SetCell(row.Cells[6], m.Pressure.HasValue ? $"{m.Pressure:F2}" : "—",
                centered: true, fontSize: 8, bgColor: bgClr);

            SetColumnWidths(row, 35, 100, 70, 70, 70, 60, 70);
        }

        doc.InsertTable(table);
        doc.InsertParagraph(" ");
    }

    // ── 9. Tabla de alertas ───────────────────────────────────

    private void AddAlertsTable(DocX doc, ReportData data)
    {
        var headers = new[] { "Tipo", "Mensaje", "Valor", "Timestamp", "Apagado" };
        var table   = doc.AddTable(data.Alerts.Count + 1, headers.Length);
        table.Design = TableDesign.None;

        for (int h = 0; h < headers.Length; h++)
            SetCell(table.Rows[0].Cells[h], headers[h],
                bold: true, bgColor: HeaderBg, color: Color.White, centered: true, fontSize: 9);

        SetColumnWidths(table.Rows[0], 80, 240, 70, 100, 70);

        for (int i = 0; i < data.Alerts.Count; i++)
        {
            var a      = data.Alerts[i];
            var typeClr = a.Type == "Critical" ? DangerRed
                        : a.Type == "Warning"  ? WarnOrange
                        : PrimaryBlue;
            var row = table.Rows[i + 1];

            SetCell(row.Cells[0], a.Type,                              bold: true, color: typeClr, fontSize: 9);
            SetCell(row.Cells[1], a.Message,                           fontSize: 9);
            SetCell(row.Cells[2], $"{a.TriggerValue:F2}",              centered: true, fontSize: 9);
            SetCell(row.Cells[3], a.TriggeredAt.ToString("dd/MM HH:mm:ss"), centered: true, fontSize: 9);
            SetCell(row.Cells[4], a.AutoShutdown ? "SÍ" : "NO",        centered: true, fontSize: 9,
                color: a.AutoShutdown ? DangerRed : Color.Black, bold: a.AutoShutdown);

            SetColumnWidths(row, 80, 240, 70, 100, 70);
        }

        doc.InsertTable(table);
        doc.InsertParagraph(" ");
    }

    // ── 10. Observaciones ────────────────────────────────────

    private void AddObservations(DocX doc, ReportData data)
    {
        if (!string.IsNullOrWhiteSpace(data.Observations))
        {
            var p = doc.InsertParagraph(data.Observations);
            p.FontSize(10);
            p.SpacingAfter(12);
        }
        else
        {
            var p = doc.InsertParagraph("Sin observaciones registradas.");
            p.Color(Color.Gray);
            p.Italic();
            p.FontSize(10);
        }

        doc.InsertParagraph(" ");
        var closing = doc.InsertParagraph(
            $"Fin del reporte — Generado el {data.GeneratedAt:dd/MM/yyyy} a las {data.GeneratedAt:HH:mm}");
        closing.Alignment = Alignment.center;
        closing.Color(Color.Gray);
        closing.Italic();
        closing.FontSize(8);
    }

    // ── Footer ────────────────────────────────────────────────

    private void AddFooter(DocX doc, ReportData data)
    {
        doc.AddFooters();
        doc.Footers.Odd.InsertParagraph(
            $"Sistema de Control de Cavitación  |  {data.GeneratedAt:dd/MM/yyyy HH:mm}  |  {data.OperatorName}")
            .FontSize(8).Color(Color.Gray).Alignment = Alignment.center;
    }

    // ── Helpers ───────────────────────────────────────────────

    private void AddSectionTitle(DocX doc, string text)
    {
        var p = doc.InsertParagraph(text);
        p.StyleName   = "Heading2";
        p.Bold();
        p.FontSize(13);
        p.Color(PrimaryBlue);
        p.SpacingBefore(12);
        p.SpacingAfter(6);
    }

    private void AddPageBreak(DocX doc)
    {
        doc.InsertParagraph().InsertPageBreakAfterSelf();
    }

    // SetCell con DocX - sobrecarga para Cell directa
    private void SetCell(
        Row row, Cell cell, string text,
        bool bold = false, bool italic = false, bool centered = false,
        Color? color = null, Color? bgColor = null, double fontSize = 10)
        => SetCell(cell, text, bold, italic, centered, color, bgColor, fontSize);

    private void SetCell(
        Cell cell, string text,
        bool bold = false, bool italic = false, bool centered = false,
        Color? color = null, Color? bgColor = null, double fontSize = 10)
    {
        var p = cell.Paragraphs.Count > 0
            ? cell.Paragraphs[0]
            : cell.InsertParagraph();

        p.RemoveText(0);
        p.Append(text);
        p.FontSize(fontSize);

        if (bold)   p.Bold();
        if (italic) p.Italic();
        if (centered) p.Alignment = Alignment.center;
        if (color.HasValue)   p.Color(color.Value);
        if (bgColor.HasValue) cell.FillColor = bgColor.Value;

        cell.Paragraphs[0].SpacingBefore(2);
        cell.Paragraphs[0].SpacingAfter(2);
    }

    private void SetColumnWidths(Row row, params double[] widths)
    {
        for (int i = 0; i < Math.Min(widths.Length, row.Cells.Count); i++)
            row.Cells[i].Width = widths[i];
    }

    private static string FormatDuration(TimeSpan ts) =>
        ts.TotalHours >= 1
            ? $"{(int)ts.TotalHours}h {ts.Minutes:D2}m {ts.Seconds:D2}s"
            : $"{ts.Minutes}m {ts.Seconds}s";
}
