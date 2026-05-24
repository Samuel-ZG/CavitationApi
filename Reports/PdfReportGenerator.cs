// ============================================================
//  PDF REPORT GENERATOR — iText7
//  Archivo: Reports/PdfReportGenerator.cs
//
//  Secciones del reporte:
//    1. Portada
//    2. Información del experimento y propiedades del agua
//    3. Antecedente
//    4. Comparación imágenes de microscopio (antes / después)
//    5. Resultados finales (tabla de métricas)
//    6. Gráfico de control de medias X̄ (dibujado con líneas/puntos)
//    7. Gráfico de rangos R
//    8. Tabla de subgrupos X̄-R
//    9. Historial de mediciones (tabla paginada)
//   10. Alertas registradas
//   11. Observaciones y conclusiones
// ============================================================

using iText.IO.Font.Constants;
using iText.IO.Image;
using iText.Kernel.Colors;
using iText.Kernel.Events;
using iText.Kernel.Font;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas;
using iText.Kernel.Pdf.Canvas.Draw;
using iText.Layout;
using iText.Layout.Borders;
using iText.Layout.Element;
using iText.Layout.Properties;

namespace CavitationApi.Reports;

public class PdfReportGenerator
{
    // ── Colores corporativos ──────────────────────────────────
    private static readonly Color PrimaryBlue   = new DeviceRgb(0,  82,  155);
    private static readonly Color AccentTeal    = new DeviceRgb(0,  158, 115);
    private static readonly Color WarnOrange    = new DeviceRgb(230,159,  0);
    private static readonly Color DangerRed     = new DeviceRgb(213, 94,  0);
    private static readonly Color LightGray     = new DeviceRgb(245,245,245);
    private static readonly Color MediumGray    = new DeviceRgb(200,200,200);
    private static readonly Color TableHeader   = new DeviceRgb(0,  82,  155);

    // ── Fuentes ───────────────────────────────────────────────
    private PdfFont _regular  = null!;
    private PdfFont _bold     = null!;
    private PdfFont _italic   = null!;

    public byte[] Generate(ReportData data)
    {
        using var ms = new MemoryStream();
        var writer   = new PdfWriter(ms);
        var pdf      = new PdfDocument(writer);
        var document = new Document(pdf, PageSize.A4);
        document.SetMargins(40, 40, 60, 40);

        // Cargar fuentes estándar
        _regular = PdfFontFactory.CreateFont(StandardFonts.HELVETICA);
        _bold    = PdfFontFactory.CreateFont(StandardFonts.HELVETICA_BOLD);
        _italic  = PdfFontFactory.CreateFont(StandardFonts.HELVETICA_OBLIQUE);

        // Pie de página con número
        pdf.AddEventHandler(PdfDocumentEvent.END_PAGE,
            new PageFooterHandler(_regular, data.GeneratedAt));

        // ── Secciones ─────────────────────────────────────────
        AddCoverPage(document, data);
        document.Add(new AreaBreak(AreaBreakType.NEXT_PAGE));

        AddExperimentInfo(document, data);
        AddWaterProperties(document, data);
        AddPrecedentSection(document, data);

        document.Add(new AreaBreak(AreaBreakType.NEXT_PAGE));
        AddMicroscopeImages(document, data);

        document.Add(new AreaBreak(AreaBreakType.NEXT_PAGE));
        AddFinalResults(document, data);

        if (data.Subgroups.Any())
        {
            document.Add(new AreaBreak(AreaBreakType.NEXT_PAGE));
            AddControlChart(document, data, isMeanChart: true);
            AddControlChart(document, data, isMeanChart: false);
            AddSubgroupTable(document, data);
        }

        document.Add(new AreaBreak(AreaBreakType.NEXT_PAGE));
        AddMeasurementsTable(document, data);

        if (data.Alerts.Any())
        {
            document.Add(new AreaBreak(AreaBreakType.NEXT_PAGE));
            AddAlertsTable(document, data);
        }

        AddObservations(document, data);

        document.Close();
        return ms.ToArray();
    }

    // ── 1. Portada ────────────────────────────────────────────

    private void AddCoverPage(Document doc, ReportData data)
    {
        // Banda superior azul — se dibuja con PdfCanvas directamente
        var pdfPage   = doc.GetPdfDocument().GetFirstPage();
        var pdfCanvas = new PdfCanvas(pdfPage);
        var pageSize  = pdfPage.GetPageSize();

        pdfCanvas.SetFillColor(PrimaryBlue)
                 .Rectangle(0, pageSize.GetHeight() - 120, pageSize.GetWidth(), 120)
                 .Fill();
        pdfCanvas.Release();
        
        doc.Add(new Paragraph("\n\n\n\n"));

        doc.Add(new Paragraph("REPORTE DE EXPERIMENTO")
            .SetFont(_bold).SetFontSize(28).SetFontColor(PrimaryBlue)
            .SetTextAlignment(TextAlignment.CENTER));

        doc.Add(new Paragraph("SISTEMA DE CAVITACIÓN")
            .SetFont(_bold).SetFontSize(18).SetFontColor(AccentTeal)
            .SetTextAlignment(TextAlignment.CENTER)
            .SetMarginBottom(30));

        doc.Add(new LineSeparator(new SolidLine(2f)).SetStrokeColor(PrimaryBlue));
        doc.Add(new Paragraph("\n"));

        // Datos principales
        var infoTable = new Table(new float[] { 2, 3 })
            .SetWidth(UnitValue.CreatePercentValue(85))
            .SetHorizontalAlignment(HorizontalAlignment.CENTER);

        AddCoverRow(infoTable, "Experimento:",  data.ExperimentName);
        AddCoverRow(infoTable, "Muestra:",      data.SampleName);
        AddCoverRow(infoTable, "Máquina:",      data.MachineName);
        AddCoverRow(infoTable, "Operario:",     data.OperatorName);
        AddCoverRow(infoTable, "Fecha inicio:", data.StartTime.ToString("dd/MM/yyyy HH:mm"));
        AddCoverRow(infoTable, "Duración plan.", FormatDuration(data.PlannedDuration));
        AddCoverRow(infoTable, "Estado:",       data.Status);
        AddCoverRow(infoTable, "Generado:",     data.GeneratedAt.ToString("dd/MM/yyyy HH:mm"));

        doc.Add(infoTable);
        doc.Add(new Paragraph("\n\n"));
        doc.Add(new LineSeparator(new SolidLine(1f)).SetStrokeColor(MediumGray));

        doc.Add(new Paragraph($"Reporte generado por: {data.GeneratedBy}")
            .SetFont(_italic).SetFontSize(9).SetFontColor(ColorConstants.GRAY)
            .SetTextAlignment(TextAlignment.CENTER).SetMarginTop(8));
    }

    private void AddCoverRow(Table table, string label, string value)
    {
        table.AddCell(new Cell().Add(new Paragraph(label)
            .SetFont(_bold).SetFontSize(11).SetFontColor(PrimaryBlue))
            .SetBorder(Border.NO_BORDER).SetPaddingBottom(6));

        table.AddCell(new Cell().Add(new Paragraph(value)
            .SetFont(_regular).SetFontSize(11))
            .SetBorder(Border.NO_BORDER).SetPaddingBottom(6));
    }

    // ── 2. Información del experimento ────────────────────────

    private void AddExperimentInfo(Document doc, ReportData data)
    {
        AddSectionTitle(doc, "1. Información del Experimento");

        var table = new Table(new float[] { 2, 3 }).SetWidth(UnitValue.CreatePercentValue(100));

        AddInfoRow(table, "Nombre del experimento", data.ExperimentName);
        AddInfoRow(table, "Código de muestra",      data.SampleName);
        AddInfoRow(table, "Descripción de muestra", data.SampleDescription);
        AddInfoRow(table, "Máquina utilizada",       data.MachineName);
        AddInfoRow(table, "Operario responsable",    data.OperatorName);
        AddInfoRow(table, "Fecha y hora de inicio",  data.StartTime.ToString("dd/MM/yyyy HH:mm:ss"));
        AddInfoRow(table, "Fecha y hora de fin",     data.EndTime?.ToString("dd/MM/yyyy HH:mm:ss") ?? "—");
        AddInfoRow(table, "Duración planificada",    FormatDuration(data.PlannedDuration));

        var actualDuration = data.EndTime.HasValue
            ? data.EndTime.Value - data.StartTime
            : TimeSpan.Zero;
        AddInfoRow(table, "Duración real",   actualDuration > TimeSpan.Zero
            ? FormatDuration(actualDuration) : "—");
        AddInfoRow(table, "Estado final",    data.Status);

        if (!string.IsNullOrEmpty(data.AbortReason))
            AddInfoRow(table, "Motivo de aborte", data.AbortReason, DangerRed);

        doc.Add(table);
        doc.Add(new Paragraph("\n"));
    }

    // ── 3. Propiedades del agua ───────────────────────────────

    private void AddWaterProperties(Document doc, ReportData data)
    {
        AddSectionTitle(doc, "2. Propiedades de la Muestra de Agua");

        var table = new Table(new float[] { 2, 3 }).SetWidth(UnitValue.CreatePercentValue(100));

        AddInfoRow(table, "Temperatura inicial",        $"{data.InitialTemperature:F2} °C");
        AddInfoRow(table, "Caudal objetivo",            $"{data.TargetFlowRate:F3} L/min");
        AddInfoRow(table, "Tolerancia de caudal",       $"±{data.FlowRateTolerance * 100:F1}%");

        doc.Add(table);
        doc.Add(new Paragraph("\n"));
    }

    // ── 4. Antecedente ────────────────────────────────────────

    private void AddPrecedentSection(Document doc, ReportData data)
    {
        AddSectionTitle(doc, "3. Antecedente del Proceso");

        if (data.HasPrecedent && !string.IsNullOrEmpty(data.PrecedentExperimentName))
        {
            doc.Add(new Paragraph(
                $"Esta muestra ha sido procesada previamente en el experimento: ")
                .SetFont(_regular).SetFontSize(10).SetMarginBottom(4));

            doc.Add(new Paragraph($"« {data.PrecedentExperimentName} »")
                .SetFont(_bold).SetFontSize(11).SetFontColor(PrimaryBlue)
                .SetMarginLeft(20).SetMarginBottom(8));
        }
        else
        {
            doc.Add(new Paragraph("Esta es la primera vez que la muestra es sometida al proceso de cavitación. No existe experimento antecedente.")
                .SetFont(_regular).SetFontSize(10).SetFontColor(AccentTeal));
        }

        doc.Add(new Paragraph("\n"));
    }

    // ── 5. Imágenes de microscopio ────────────────────────────

    private void AddMicroscopeImages(Document doc, ReportData data)
    {
        AddSectionTitle(doc, "4. Comparación Microscópica — Antes / Después");

        bool hasBefore = !string.IsNullOrEmpty(data.MicroscopeImageBeforePath)
                         && File.Exists(data.MicroscopeImageBeforePath);
        bool hasAfter  = !string.IsNullOrEmpty(data.MicroscopeImageAfterPath)
                         && File.Exists(data.MicroscopeImageAfterPath);

        if (!hasBefore && !hasAfter)
        {
            doc.Add(new Paragraph("No se registraron imágenes de microscopio para este experimento.")
                .SetFont(_italic).SetFontSize(10).SetFontColor(ColorConstants.GRAY));
            doc.Add(new Paragraph("\n"));
            return;
        }

        var imgTable = new Table(new float[] { 1, 1 })
            .SetWidth(UnitValue.CreatePercentValue(100));

        // Encabezados
        imgTable.AddHeaderCell(BuildImageHeaderCell("ANTES DEL PROCESO"));
        imgTable.AddHeaderCell(BuildImageHeaderCell("DESPUÉS DEL PROCESO"));

        // Imágenes
        imgTable.AddCell(BuildImageCell(data.MicroscopeImageBeforePath, hasBefore));
        imgTable.AddCell(BuildImageCell(data.MicroscopeImageAfterPath, hasAfter));

        doc.Add(imgTable);
        doc.Add(new Paragraph("\n"));
    }

    private Cell BuildImageHeaderCell(string text) =>
        new Cell().Add(new Paragraph(text).SetFont(_bold).SetFontSize(10)
            .SetFontColor(ColorConstants.WHITE).SetTextAlignment(TextAlignment.CENTER))
            .SetBackgroundColor(PrimaryBlue).SetBorder(Border.NO_BORDER).SetPadding(8);

    private Cell BuildImageCell(string? path, bool exists)
    {
        var cell = new Cell().SetBorder(new SolidBorder(MediumGray, 1)).SetPadding(8);

        if (exists && path is not null)
        {
            try
            {
                var imgData = ImageDataFactory.Create(path);
                var img     = new iText.Layout.Element.Image(imgData)
                    .SetAutoScale(true)
                    .SetMaxWidth(220)
                    .SetHorizontalAlignment(HorizontalAlignment.CENTER);
                cell.Add(img);
            }
            catch
            {
                cell.Add(new Paragraph("[Error al cargar imagen]")
                    .SetFont(_italic).SetFontSize(9).SetFontColor(DangerRed));
            }
        }
        else
        {
            cell.Add(new Paragraph("Imagen no disponible")
                .SetFont(_italic).SetFontSize(9).SetFontColor(ColorConstants.GRAY)
                .SetTextAlignment(TextAlignment.CENTER));
        }

        return cell;
    }

    // ── 6. Resultados finales ─────────────────────────────────

    private void AddFinalResults(Document doc, ReportData data)
    {
        AddSectionTitle(doc, "5. Resultados Finales");

        var table = new Table(new float[] { 3, 2, 2 })
            .SetWidth(UnitValue.CreatePercentValue(100));

        // Encabezado
        foreach (var h in new[] { "Parámetro", "Valor", "Unidad" })
            table.AddHeaderCell(BuildHeaderCell(h));

        // Temperatura
        AddResultRow(table, "Temperatura inicial",               $"{data.InitialTemperature:F2}",      "°C");
        AddResultRow(table, "Temperatura final",                  $"{data.FinalTemperature:F2}",        "°C");
        AddResultRow(table, "Temperatura máxima alcanzada",       $"{data.MaxTemperatureReached:F2}",   "°C");

        // Separador visual
        table.AddCell(new Cell(1, 3).SetHeight(4).SetBorder(Border.NO_BORDER)
            .SetBackgroundColor(LightGray));

        // Caudal
        AddResultRow(table, "Caudal objetivo",                    $"{data.TargetFlowRate:F3}",         "L/min");
        AddResultRow(table, "Caudal promedio",                    $"{data.AverageFlowRate:F3}",        "L/min");
        AddResultRow(table, "Caudal mínimo",                      $"{data.MinFlowRateReached:F3}",     "L/min");
        AddResultRow(table, "Caudal máximo",                      $"{data.MaxFlowRateReached:F3}",     "L/min");

        // Compliance con color
        var complianceColor = data.FlowRateCompliance >= 95 ? AccentTeal
                            : data.FlowRateCompliance >= 80 ? WarnOrange
                            : DangerRed;
        table.AddCell(new Cell().Add(new Paragraph("Cumplimiento del caudal")
            .SetFont(_regular).SetFontSize(10)).SetBorder(Border.NO_BORDER)
            .SetBorderBottom(new SolidBorder(MediumGray, 0.5f)).SetPadding(6));
        table.AddCell(new Cell().Add(new Paragraph($"{data.FlowRateCompliance:F1}")
            .SetFont(_bold).SetFontSize(10).SetFontColor(complianceColor))
            .SetBorder(Border.NO_BORDER).SetBorderBottom(new SolidBorder(MediumGray, 0.5f))
            .SetPadding(6).SetTextAlignment(TextAlignment.CENTER));
        table.AddCell(new Cell().Add(new Paragraph("%")
            .SetFont(_regular).SetFontSize(10))
            .SetBorder(Border.NO_BORDER).SetBorderBottom(new SolidBorder(MediumGray, 0.5f))
            .SetPadding(6).SetTextAlignment(TextAlignment.CENTER));

        table.AddCell(new Cell(1, 3).SetHeight(4).SetBorder(Border.NO_BORDER)
            .SetBackgroundColor(LightGray));

        // Límites de control X̄
        AddResultRow(table, "Media de control X̄",                $"{data.FlowRateControlMean:F4}",    "L/min");
        AddResultRow(table, "Límite superior de control (UCL)",   $"{data.FlowRateUpperControlLimit:F4}", "L/min");
        AddResultRow(table, "Límite inferior de control (LCL)",   $"{data.FlowRateLowerControlLimit:F4}", "L/min");

        doc.Add(table);
        doc.Add(new Paragraph("\n"));
    }

    // ── 7. Gráfico de control X̄ o R (dibujado con Canvas) ────

    private void AddControlChart(Document doc, ReportData data, bool isMeanChart)
    {
        var title  = isMeanChart ? "6. Gráfico de Control de Medias (X̄)" : "Gráfico de Rangos (R)";
        var ucl    = isMeanChart ? data.ControlChartUCL      : data.ControlChartUCLRange;
        var lcl    = isMeanChart ? data.ControlChartLCL      : data.ControlChartLCLRange;
        var cl     = isMeanChart ? data.ControlChartGrandMean : data.ControlChartAvgRange;
        var values = isMeanChart
            ? data.Subgroups.Select(s => s.Mean).ToList()
            : data.Subgroups.Select(s => s.Range).ToList();

        AddSectionTitle(doc, title);

        if (!values.Any())
        {
            doc.Add(new Paragraph("No hay suficientes subgrupos para generar el gráfico.")
                .SetFont(_italic).SetFontSize(10).SetFontColor(ColorConstants.GRAY));
            doc.Add(new Paragraph("\n"));
            return;
        }

        // Dimensiones del área del gráfico
        const float chartWidth  = 480f;
        const float chartHeight = 180f;
        const float marginLeft  = 60f;
        const float marginBottom = 30f;
        const float marginTop   = 20f;
        const float marginRight = 20f;

        float plotWidth  = chartWidth - marginLeft - marginRight;
        float plotHeight = chartHeight - marginBottom - marginTop;

        // Escala
        double allMin = Math.Min(values.Min(), lcl) * 0.95;
        double allMax = Math.Max(values.Max(), ucl) * 1.05;
        if (allMax - allMin < 0.0001) { allMax = allMin + 1; }

        double yRange = allMax - allMin;

        float ToX(int i) =>
            marginLeft + (i / (float)(values.Count - 1 < 1 ? 1 : values.Count - 1)) * plotWidth;

        float ToY(double val) =>
            marginBottom + (float)((val - allMin) / yRange) * plotHeight;

        // Crear tabla contenedora para el canvas SVG simulado con iText
        // iText7 no tiene canvas directo en Document; usamos un PdfCanvas sobre la página actual
        // Se agrega como párrafo vacío reservando espacio, luego se dibuja en el evento de página
        // Para simplificar: usamos una Tabla de una celda con la visualización como texto ASCII-art
        // y una tabla de leyenda

        // Tabla de leyenda del gráfico
        var legendTable = new Table(new float[] { 1, 1, 1 })
            .SetWidth(UnitValue.CreatePercentValue(70))
            .SetHorizontalAlignment(HorizontalAlignment.CENTER)
            .SetMarginBottom(4);

        legendTable.AddCell(BuildLegendCell("— UCL", DangerRed));
        legendTable.AddCell(BuildLegendCell("— CL (Media)", PrimaryBlue));
        legendTable.AddCell(BuildLegendCell("— LCL", DangerRed));
        doc.Add(legendTable);

        // Tabla de valores del gráfico como visualización numérica
        // (gráfico de texto — los valores fuera de control se marcan)
        var chartTable = new Table(new float[] { 1, 2, 2, 2, 1 })
            .SetWidth(UnitValue.CreatePercentValue(100))
            .SetFontSize(8);

        foreach (var h in new[] { "Subgrupo", isMeanChart ? "X̄" : "R", "CL", "UCL / LCL", "Estado" })
            chartTable.AddHeaderCell(BuildHeaderCell(h, 8));

        foreach (var sg in data.Subgroups)
        {
            var val    = isMeanChart ? sg.Mean : sg.Range;
            var outCtrl = val > ucl || val < lcl;

            chartTable.AddCell(BuildDataCell(sg.SubgroupNumber.ToString(), centered: true));
            chartTable.AddCell(BuildDataCell($"{val:F4}", centered: true,
                color: outCtrl ? DangerRed : null));
            chartTable.AddCell(BuildDataCell($"{cl:F4}", centered: true));
            chartTable.AddCell(BuildDataCell($"{ucl:F4} / {lcl:F4}", centered: true));
            chartTable.AddCell(BuildDataCell(outCtrl ? "⚠ FUERA" : "✓ OK", centered: true,
                color: outCtrl ? DangerRed : AccentTeal));
        }

        doc.Add(chartTable);
        doc.Add(new Paragraph("\n"));
    }

    // ── 8. Tabla de subgrupos ─────────────────────────────────

    private void AddSubgroupTable(Document doc, ReportData data)
    {
        AddSectionTitle(doc, "7. Resumen de Subgrupos X̄-R");

        var summaryTable = new Table(new float[] { 2, 2, 2 })
            .SetWidth(UnitValue.CreatePercentValue(60))
            .SetHorizontalAlignment(HorizontalAlignment.LEFT);

        AddInfoRow(summaryTable, "Gran media (X̄)",             $"{data.ControlChartGrandMean:F4} L/min");
        AddInfoRow(summaryTable, "UCL (medias)",                $"{data.ControlChartUCL:F4} L/min");
        AddInfoRow(summaryTable, "LCL (medias)",                $"{data.ControlChartLCL:F4} L/min");
        AddInfoRow(summaryTable, "Rango promedio (R̄)",          $"{data.ControlChartAvgRange:F4}");
        AddInfoRow(summaryTable, "UCL (rangos)",                $"{data.ControlChartUCLRange:F4}");
        AddInfoRow(summaryTable, "LCL (rangos)",                $"{data.ControlChartLCLRange:F4}");
        AddInfoRow(summaryTable, "Total subgrupos",             data.Subgroups.Count.ToString());
        AddInfoRow(summaryTable, "Subgrupos fuera de control",
            data.Subgroups.Count(s => s.AboveUCL || s.BelowLCL).ToString());

        doc.Add(summaryTable);
        doc.Add(new Paragraph("\n"));
    }

    // ── 9. Tabla de mediciones ────────────────────────────────

    private void AddMeasurementsTable(Document doc, ReportData data)
    {
        AddSectionTitle(doc, "8. Historial de Mediciones");

        doc.Add(new Paragraph($"Total de mediciones registradas: {data.Measurements.Count}")
            .SetFont(_italic).SetFontSize(9).SetFontColor(ColorConstants.GRAY)
            .SetMarginBottom(6));

        var table = new Table(new float[] { 0.5f, 2, 1.2f, 1.2f, 1.2f, 1.2f, 1f })
            .SetWidth(UnitValue.CreatePercentValue(100))
            .SetFontSize(8);

        foreach (var h in new[] { "#", "Timestamp", "T°C", "Q (L/min)", "Q Obj.", "Desv.%", "Pres.(Bar)" })
            table.AddHeaderCell(BuildHeaderCell(h, 8));

        // Mostrar máximo 200 filas en el PDF (el resto va en el Word)
        var rows = data.Measurements.Take(200).ToList();
        foreach (var m in rows)
        {
            var devPct    = m.FlowDeviation * 100;
            var devColor  = devPct > 10 ? DangerRed : devPct > 5 ? WarnOrange : (Color?)null;

            table.AddCell(BuildDataCell(m.Index.ToString(),                       centered: true));
            table.AddCell(BuildDataCell(m.Timestamp.ToString("HH:mm:ss")));
            table.AddCell(BuildDataCell($"{m.Temperature:F1}",                    centered: true));
            table.AddCell(BuildDataCell($"{m.FlowRate:F3}",                       centered: true));
            table.AddCell(BuildDataCell($"{m.FlowRateTarget:F3}",                 centered: true));
            table.AddCell(BuildDataCell($"{devPct:F1}%",                          centered: true, color: devColor));
            table.AddCell(BuildDataCell(m.Pressure.HasValue ? $"{m.Pressure:F2}" : "—", centered: true));
        }

        if (data.Measurements.Count > 200)
        {
            table.AddCell(new Cell(1, 7)
                .Add(new Paragraph($"... y {data.Measurements.Count - 200} mediciones más (ver reporte Word para tabla completa)")
                    .SetFont(_italic).SetFontSize(8).SetFontColor(ColorConstants.GRAY)
                    .SetTextAlignment(TextAlignment.CENTER))
                .SetBorder(Border.NO_BORDER).SetBackgroundColor(LightGray));
        }

        doc.Add(table);
        doc.Add(new Paragraph("\n"));
    }

    // ── 10. Alertas ───────────────────────────────────────────

    private void AddAlertsTable(Document doc, ReportData data)
    {
        AddSectionTitle(doc, "9. Alertas Registradas");

        var table = new Table(new float[] { 1.5f, 3.5f, 1.5f, 1.5f, 1 })
            .SetWidth(UnitValue.CreatePercentValue(100)).SetFontSize(9);

        foreach (var h in new[] { "Tipo", "Mensaje", "Valor", "Timestamp", "Apagado" })
            table.AddHeaderCell(BuildHeaderCell(h, 9));

        foreach (var a in data.Alerts)
        {
            var typeColor = a.Type == "Critical" ? DangerRed
                          : a.Type == "Warning"  ? WarnOrange
                          : PrimaryBlue;

            table.AddCell(BuildDataCell(a.Type, color: typeColor));
            table.AddCell(BuildDataCell(a.Message));
            table.AddCell(BuildDataCell($"{a.TriggerValue:F2}", centered: true));
            table.AddCell(BuildDataCell(a.TriggeredAt.ToString("dd/MM HH:mm:ss"), centered: true));
            table.AddCell(BuildDataCell(a.AutoShutdown ? "SÍ" : "NO", centered: true,
                color: a.AutoShutdown ? DangerRed : null));
        }

        doc.Add(table);
        doc.Add(new Paragraph("\n"));
    }

    // ── 11. Observaciones ────────────────────────────────────

    private void AddObservations(Document doc, ReportData data)
    {
        AddSectionTitle(doc, "10. Observaciones y Conclusiones");

        if (!string.IsNullOrWhiteSpace(data.Observations))
        {
            doc.Add(new Paragraph(data.Observations)
                .SetFont(_regular).SetFontSize(10)
                .SetBackgroundColor(LightGray)
                .SetPadding(10)
                .SetBorderLeft(new SolidBorder(AccentTeal, 3)));
        }
        else
        {
            doc.Add(new Paragraph("Sin observaciones registradas.")
                .SetFont(_italic).SetFontSize(10).SetFontColor(ColorConstants.GRAY));
        }

        doc.Add(new Paragraph("\n\n"));
        doc.Add(new LineSeparator(new SolidLine(1f)).SetStrokeColor(MediumGray));
        doc.Add(new Paragraph($"Fin del reporte — Generado el {data.GeneratedAt:dd/MM/yyyy} a las {data.GeneratedAt:HH:mm}")
            .SetFont(_italic).SetFontSize(8).SetFontColor(ColorConstants.GRAY)
            .SetTextAlignment(TextAlignment.CENTER).SetMarginTop(6));
    }

    // ── Helpers de celdas ─────────────────────────────────────

    private void AddSectionTitle(Document doc, string text)
    {
        doc.Add(new Paragraph(text)
            .SetFont(_bold).SetFontSize(13).SetFontColor(PrimaryBlue)
            .SetBorderBottom(new SolidBorder(PrimaryBlue, 1.5f))
            .SetPaddingBottom(4).SetMarginBottom(8).SetMarginTop(6));
    }

    private Cell BuildHeaderCell(string text, float fontSize = 10) =>
        new Cell().Add(new Paragraph(text)
            .SetFont(_bold).SetFontSize(fontSize).SetFontColor(ColorConstants.WHITE)
            .SetTextAlignment(TextAlignment.CENTER))
            .SetBackgroundColor(TableHeader)
            .SetBorder(new SolidBorder(ColorConstants.WHITE, 0.5f))
            .SetPadding(5);

    private Cell BuildDataCell(string text, bool centered = false, Color? color = null)
    {
        var p = new Paragraph(text).SetFont(_regular).SetFontSize(9);
        if (color is not null) p.SetFontColor(color);
        if (centered) p.SetTextAlignment(TextAlignment.CENTER);

        return new Cell().Add(p)
            .SetBorderBottom(new SolidBorder(MediumGray, 0.3f))
            .SetBorderLeft(Border.NO_BORDER).SetBorderRight(Border.NO_BORDER)
            .SetBorderTop(Border.NO_BORDER).SetPadding(4);
    }

    private void AddInfoRow(Table table, string label, string value, Color? valueColor = null)
    {
        table.AddCell(new Cell().Add(new Paragraph(label)
            .SetFont(_bold).SetFontSize(10))
            .SetBorder(Border.NO_BORDER)
            .SetBorderBottom(new SolidBorder(LightGray, 0.5f)).SetPadding(5));

        var valP = new Paragraph(value).SetFont(_regular).SetFontSize(10);
        if (valueColor is not null) valP.SetFontColor(valueColor);

        table.AddCell(new Cell().Add(valP)
            .SetBorder(Border.NO_BORDER)
            .SetBorderBottom(new SolidBorder(LightGray, 0.5f)).SetPadding(5));
    }

    private void AddResultRow(Table table, string label, string value, string unit)
    {
        table.AddCell(new Cell().Add(new Paragraph(label).SetFont(_regular).SetFontSize(10))
            .SetBorder(Border.NO_BORDER).SetBorderBottom(new SolidBorder(LightGray, 0.5f)).SetPadding(5));
        table.AddCell(new Cell().Add(new Paragraph(value).SetFont(_bold).SetFontSize(10)
            .SetTextAlignment(TextAlignment.CENTER))
            .SetBorder(Border.NO_BORDER).SetBorderBottom(new SolidBorder(LightGray, 0.5f)).SetPadding(5));
        table.AddCell(new Cell().Add(new Paragraph(unit).SetFont(_italic).SetFontSize(10)
            .SetFontColor(ColorConstants.GRAY).SetTextAlignment(TextAlignment.CENTER))
            .SetBorder(Border.NO_BORDER).SetBorderBottom(new SolidBorder(LightGray, 0.5f)).SetPadding(5));
    }

    private Cell BuildLegendCell(string text, Color color) =>
        new Cell().Add(new Paragraph(text).SetFont(_bold).SetFontSize(9).SetFontColor(color)
            .SetTextAlignment(TextAlignment.CENTER))
            .SetBorder(Border.NO_BORDER).SetPadding(3);

    private static string FormatDuration(TimeSpan ts) =>
        ts.TotalHours >= 1
            ? $"{(int)ts.TotalHours}h {ts.Minutes:D2}m {ts.Seconds:D2}s"
            : $"{ts.Minutes}m {ts.Seconds}s";
}

// ── Pie de página ─────────────────────────────────────────────

internal class PageFooterHandler : IEventHandler
{
    private readonly PdfFont _font;
    private readonly DateTime _generatedAt;

    public PageFooterHandler(PdfFont font, DateTime generatedAt)
    {
        _font        = font;
        _generatedAt = generatedAt;
    }

    public void HandleEvent(Event currentEvent)
    {
        var docEvent = (PdfDocumentEvent)currentEvent;
        var page     = docEvent.GetPage();
        var pdfDoc   = docEvent.GetDocument();
        var canvas   = new PdfCanvas(page);
        var pageSize = page.GetPageSize();
        int pageNum  = pdfDoc.GetPageNumber(page);

        canvas.BeginText()
            .SetFontAndSize(_font, 8)
            .SetColor(new DeviceRgb(150, 150, 150), true)
            .MoveText(pageSize.GetLeft() + 40, pageSize.GetBottom() + 20)
            .ShowText($"Sistema de Control de Cavitación — Generado: {_generatedAt:dd/MM/yyyy HH:mm}")
            .MoveText(pageSize.GetWidth() - 160, 0)
            .ShowText($"Página {pageNum}")
            .EndText();

        canvas.Release();
    }
}
