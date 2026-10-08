using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

internal sealed class Result
{
    public DateTime Timestamp;
    public double Download, Upload, Ping, Jitter, Loss;
    public string Server = "", Isp = "", Url = "", Error = "";
    public bool Success { get { return String.IsNullOrEmpty(Error); } }
}

internal sealed class TrendPanel : Panel
{
    public List<Result> Entries = new List<Result>();

    public TrendPanel()
    {
        BackColor = Color.White;
        BorderStyle = BorderStyle.FixedSingle;
        ResizeRedraw = true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var graphics = e.Graphics;
        var valid = new List<Result>();
        foreach (var entry in Entries) if (entry.Success) valid.Add(entry);
        if (valid.Count < 3)
        {
            TextRenderer.DrawText(graphics, "Trend wird nach 3 erfolgreichen Messungen angezeigt.", Font, ClientRectangle,
                Color.DimGray, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }

        int bandHeight = Math.Max(34, (Height - 8) / 3);
        DrawSeries(graphics, valid, "Download", Color.FromArgb(37, 99, 235), 4, bandHeight - 7);
        DrawSeries(graphics, valid, "Upload", Color.FromArgb(22, 163, 74), 4 + bandHeight, bandHeight - 7);
        DrawSeries(graphics, valid, "Ping", Color.FromArgb(234, 88, 12), 4 + bandHeight * 2, bandHeight - 7);
    }

    private void DrawSeries(Graphics graphics, List<Result> items, string metric, Color colour, int top, int height)
    {
        var values = new List<double>();
        foreach (var item in items) values.Add(metric == "Download" ? item.Download : metric == "Upload" ? item.Upload : item.Ping);
        double min = values[0], max = values[0]; foreach (double value in values) { min = Math.Min(min, value); max = Math.Max(max, value); }
        double padding = Math.Max((max - min) * .15, metric == "Ping" ? .5 : 5); min = Math.Max(0, min - padding); max += padding; if (max <= min) max = min + 1;
        int left = 56, right = 18; var plot = new Rectangle(left, top + 16, Math.Max(20, Width - left - right), Math.Max(10, height - 18));
        using (var grid = new Pen(Color.FromArgb(232, 238, 246)))
        using (var line = new Pen(colour, 2f))
        using (var brush = new SolidBrush(colour))
        {
            graphics.DrawLine(grid, plot.Left, plot.Top, plot.Right, plot.Top); graphics.DrawLine(grid, plot.Left, plot.Bottom, plot.Right, plot.Bottom);
            PointF[] points = new PointF[values.Count];
            for (int index = 0; index < values.Count; index++) points[index] = new PointF(plot.Left + plot.Width * index / (values.Count - 1f), plot.Top + (float)((max - values[index]) / (max - min) * plot.Height));
            graphics.DrawLines(line, points); foreach (PointF point in points) graphics.FillEllipse(brush, point.X - 2, point.Y - 2, 4, 4);
        }
        string unit = metric == "Ping" ? "ms" : "Mbps";
        TextRenderer.DrawText(graphics, metric + " · " + values[values.Count - 1].ToString("0.00") + " " + unit, new Font(Font, FontStyle.Bold), new Rectangle(left, top, plot.Width, 16), colour);
        TextRenderer.DrawText(graphics, max.ToString("0"), Font, new Rectangle(0, plot.Top - 8, left - 8, 16), Color.DimGray, TextFormatFlags.Right);
        TextRenderer.DrawText(graphics, min.ToString("0"), Font, new Rectangle(0, plot.Bottom - 8, left - 8, 16), Color.DimGray, TextFormatFlags.Right);
    }
}

internal sealed class MainForm : Form
{
    private readonly TextBox server = new TextBox { Text = "auto" };
    private readonly NumericUpDown count = new NumericUpDown { Minimum = 1, Maximum = 999, Value = 1 };
    private readonly NumericUpDown interval = new NumericUpDown { Minimum = 1, Maximum = 1440, Value = 5 };
    private readonly NumericUpDown duration = new NumericUpDown { Minimum = 0, Maximum = 10080, Value = 0 };
    private readonly Button start = new Button { Text = "Test starten" };
    private readonly Button stop = new Button { Text = "Stoppen", Enabled = false };
    private readonly Label status = new Label { Text = "Bereit", AutoSize = false };
    private readonly TrendPanel trend = new TrendPanel();
    private readonly DataGridView history = new DataGridView();
    private readonly Dictionary<string, Label> resultLabels = new Dictionary<string, Label>();
    private readonly List<Result> entries = new List<Result>();
    private bool stopRequested;
    private readonly string historyPath;

    public MainForm()
    {
        Text = "SpeedtestPortable";
        ClientSize = new Size(940, 690);
        MinimumSize = new Size(940, 690);
        MaximumSize = new Size(940, 690);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(245, 247, 250);
        Font = new Font("Segoe UI", 9f);
        historyPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "speedtest-history.csv");
        BuildLayout();
        LoadHistory();
        RefreshHistory();
    }

    private static Label LabelFor(string text, int x, int y, int width, Font font, Color color)
    {
        return new Label { Text = text, Location = new Point(x, y), Size = new Size(width, 22), Font = font, ForeColor = color, BackColor = Color.Transparent };
    }

    private void BuildLayout()
    {
        var header = new Panel { Location = new Point(0, 0), Size = new Size(940, 70), BackColor = Color.FromArgb(31, 78, 121) };
        var title = LabelFor("SpeedtestPortable", 22, 7, 380, new Font("Segoe UI", 18, FontStyle.Bold), Color.White);
        title.Size = new Size(380, 34);
        var subtitle = LabelFor("Für ConnectWise ScreenConnect Backstage", 24, 44, 380, new Font("Segoe UI", 9), Color.FromArgb(220, 235, 250));
        subtitle.Size = new Size(380, 20);
        header.Controls.Add(title); header.Controls.Add(subtitle);
        Controls.Add(header);

        var schedule = new GroupBox { Location = new Point(18, 82), Size = new Size(904, 88), BackColor = Color.White };
        schedule.Controls.Add(LabelFor("Test planen", 16, 7, 160, new Font("Segoe UI", 10, FontStyle.Bold), Color.FromArgb(15, 23, 42)));
        AddScheduleField(schedule, "Server-ID", server, 18, 46, 130);
        AddScheduleField(schedule, "Wiederholungen", count, 180, 46, 105);
        AddScheduleField(schedule, "Intervall (Min.)", interval, 315, 46, 105);
        AddScheduleField(schedule, "Dauer (Min., 0 = Count)", duration, 450, 46, 135);
        start.Location = new Point(620, 47); start.Size = new Size(120, 28); start.BackColor = Color.FromArgb(37, 99, 235); start.ForeColor = Color.White; start.FlatStyle = FlatStyle.Flat;
        stop.Location = new Point(752, 47); stop.Size = new Size(120, 28);
        start.Click += async (sender, args) => await RunSchedule();
        stop.Click += (sender, args) => { stopRequested = true; status.Text = "Stoppe nach aktueller Messung …"; };
        schedule.Controls.Add(start); schedule.Controls.Add(stop); Controls.Add(schedule);

        var latest = new GroupBox { Text = "Letzte Messung", Location = new Point(18, 180), Size = new Size(904, 138), BackColor = Color.White };
        AddResult(latest, "Download", "download", "Mbps", 18, 25);
        AddResult(latest, "Upload", "upload", "Mbps", 240, 25);
        AddResult(latest, "Ping", "ping", "ms", 462, 25);
        AddResult(latest, "Jitter", "jitter", "ms", 684, 25);
        AddResult(latest, "Server", "server", "", 18, 82, 420);
        AddResult(latest, "ISP", "isp", "", 462, 82, 400);
        Controls.Add(latest);

        var historyBox = new GroupBox { Text = "Verlauf", Location = new Point(18, 328), Size = new Size(904, 350), BackColor = Color.White };
        status.Location = new Point(16, 25); status.Size = new Size(500, 22); status.ForeColor = Color.FromArgb(71, 85, 105); status.BackColor = Color.White;
        var export = new Button { Text = "CSV exportieren", Location = new Point(770, 22), Size = new Size(118, 25) };
        export.Click += (sender, args) => ExportCsv();
        trend.Location = new Point(16, 55); trend.Size = new Size(872, 164);
        SetupHistoryGrid(); history.Location = new Point(16, 228); history.Size = new Size(872, 105);
        historyBox.Controls.Add(status); historyBox.Controls.Add(export); historyBox.Controls.Add(trend); historyBox.Controls.Add(history); Controls.Add(historyBox);
    }

    private void AddScheduleField(Control parent, string caption, Control control, int x, int y, int width)
    {
        parent.Controls.Add(LabelFor(caption, x, y - 16, width, new Font("Segoe UI", 8), Color.DimGray));
        control.Location = new Point(x, y); control.Size = new Size(width, 24); parent.Controls.Add(control);
    }

    private void AddResult(Control parent, string caption, string key, string unit, int x, int y, int valueWidth = 150)
    {
        parent.Controls.Add(LabelFor(caption.ToUpperInvariant(), x, y, 190, new Font("Segoe UI", 8), Color.DimGray));
        var value = LabelFor("—", x, y + 18, valueWidth, new Font("Segoe UI", 14, FontStyle.Bold), Color.FromArgb(15, 23, 42));
        parent.Controls.Add(value); resultLabels[key] = value;
        if (!String.IsNullOrEmpty(unit)) parent.Controls.Add(LabelFor(unit, x + 154, y + 23, 45, new Font("Segoe UI", 8), Color.DimGray));
    }

    private void SetupHistoryGrid()
    {
        history.ReadOnly = true; history.AllowUserToAddRows = false; history.AllowUserToDeleteRows = false; history.AllowUserToResizeRows = false;
        history.RowHeadersVisible = false; history.BackgroundColor = Color.White; history.BorderStyle = BorderStyle.FixedSingle;
        history.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(241, 245, 249); history.EnableHeadersVisualStyles = false;
        history.Columns.Add("time", "Zeitpunkt"); history.Columns.Add("download", "Download"); history.Columns.Add("upload", "Upload"); history.Columns.Add("ping", "Ping"); history.Columns.Add("server", "Server");
        history.Columns[0].Width = 150; history.Columns[1].Width = 110; history.Columns[2].Width = 110; history.Columns[3].Width = 80; history.Columns[4].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
    }

    private async Task RunSchedule()
    {
        start.Enabled = false; stop.Enabled = true; stopRequested = false;
        int total = duration.Value > 0 ? (int)Math.Floor((double)duration.Value / (double)interval.Value) + 1 : (int)count.Value;
        for (int number = 1; number <= total && !stopRequested; number++)
        {
            status.Text = "Messung " + number + " von " + total + " läuft …";
            Result result = await Task.Run(() => RunSpeedtest(server.Text.Trim()));
            entries.Add(result); SaveHistory(); ShowResult(result); RefreshHistory();
            if (number < total && !stopRequested) await WaitForInterval((int)interval.Value * 60);
        }
        status.Text = stopRequested ? "Zeitplan gestoppt" : "Zeitplan abgeschlossen";
        start.Enabled = true; stop.Enabled = false;
    }

    private async Task WaitForInterval(int seconds)
    {
        for (int left = seconds; left > 0 && !stopRequested; left--)
        {
            status.Text = "Nächste Messung in " + left + " Sekunden …";
            await Task.Delay(1000);
        }
    }

    private Result RunSpeedtest(string serverId)
    {
        try
        {
            string executable = ExtractSpeedtest();
            string arguments = "--accept-license --accept-gdpr --format=json" + (String.IsNullOrWhiteSpace(serverId) || serverId == "auto" ? "" : " --server-id " + serverId);
            var info = new ProcessStartInfo(executable, arguments) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            using (var process = Process.Start(info))
            {
                string output = process.StandardOutput.ReadToEnd(); string error = process.StandardError.ReadToEnd(); process.WaitForExit();
                if (process.ExitCode != 0) throw new InvalidOperationException(String.IsNullOrWhiteSpace(error) ? output : error);
                return ParseResult(output);
            }
        }
        catch (Exception error) { return new Result { Timestamp = DateTime.Now, Error = error.Message }; }
    }

    private static string ExtractSpeedtest()
    {
        string folder = Path.Combine(Path.GetTempPath(), "SpeedtestBackstage"); Directory.CreateDirectory(folder);
        string destination = Path.Combine(folder, "speedtest.exe");
        if (!File.Exists(destination))
        using (Stream input = Assembly.GetExecutingAssembly().GetManifestResourceStream("speedtest.exe"))
        using (FileStream output = File.Create(destination)) input.CopyTo(output);
        return destination;
    }

    private static Result ParseResult(string text)
    {
        var root = new JavaScriptSerializer().DeserializeObject(text) as Dictionary<string, object>;
        var serverData = root["server"] as Dictionary<string, object>; var pingData = root["ping"] as Dictionary<string, object>;
        var download = root["download"] as Dictionary<string, object>; var upload = root["upload"] as Dictionary<string, object>;
        var resultData = root["result"] as Dictionary<string, object>;
        return new Result { Timestamp = DateTime.Now, Download = Number(download, "bandwidth") * 8 / 1000000, Upload = Number(upload, "bandwidth") * 8 / 1000000,
            Ping = Number(pingData, "latency"), Jitter = Number(pingData, "jitter"), Loss = Number(root, "packetLoss"),
            Server = ValueText(serverData, "name") + ", " + ValueText(serverData, "location"), Isp = ValueText(root, "isp"), Url = ValueText(resultData, "url") };
    }

    private static double Number(Dictionary<string, object> map, string key) { object value; return map != null && map.TryGetValue(key, out value) ? Convert.ToDouble(value, CultureInfo.InvariantCulture) : 0; }
    private static string ValueText(Dictionary<string, object> map, string key) { object value; return map != null && map.TryGetValue(key, out value) ? Convert.ToString(value) : ""; }
    private void ShowResult(Result result)
    {
        if (!result.Success) { status.Text = "Fehler: " + result.Error; return; }
        resultLabels["download"].Text = result.Download.ToString("0.00"); resultLabels["upload"].Text = result.Upload.ToString("0.00");
        resultLabels["ping"].Text = result.Ping.ToString("0.00"); resultLabels["jitter"].Text = result.Jitter.ToString("0.00");
        resultLabels["server"].Text = result.Server; resultLabels["isp"].Text = result.Isp;
    }

    private void LoadHistory()
    {
        if (!File.Exists(historyPath)) return;
        foreach (string line in File.ReadAllLines(historyPath))
        {
            string[] fields = line.Split('\t'); if (fields.Length < 7) continue;
            DateTime timestamp; if (!DateTime.TryParse(fields[0], out timestamp)) continue;
            double down, up, ping, jitter, loss;
            if (Double.TryParse(fields[1], NumberStyles.Any, CultureInfo.InvariantCulture, out down) && Double.TryParse(fields[2], NumberStyles.Any, CultureInfo.InvariantCulture, out up) && Double.TryParse(fields[3], NumberStyles.Any, CultureInfo.InvariantCulture, out ping) && Double.TryParse(fields[4], NumberStyles.Any, CultureInfo.InvariantCulture, out jitter) && Double.TryParse(fields[5], NumberStyles.Any, CultureInfo.InvariantCulture, out loss))
                entries.Add(new Result { Timestamp = timestamp, Download = down, Upload = up, Ping = ping, Jitter = jitter, Loss = loss, Server = fields[6].Replace(" ", " "), Isp = fields.Length > 7 ? fields[7] : "" });
        }
    }

    private void SaveHistory()
    {
        var lines = new List<string>();
        foreach (Result entry in entries)
            if (entry.Success) lines.Add(String.Join("\t", entry.Timestamp.ToString("s"), entry.Download.ToString(CultureInfo.InvariantCulture), entry.Upload.ToString(CultureInfo.InvariantCulture), entry.Ping.ToString(CultureInfo.InvariantCulture), entry.Jitter.ToString(CultureInfo.InvariantCulture), entry.Loss.ToString(CultureInfo.InvariantCulture), entry.Server.Replace("\t", " "), entry.Isp.Replace("\t", " ")));
        File.WriteAllLines(historyPath, lines.ToArray());
    }

    private void RefreshHistory()
    {
        trend.Entries = entries; trend.Invalidate(); history.Rows.Clear();
        int first = Math.Max(0, entries.Count - 3);
        for (int index = entries.Count - 1; index >= first; index--)
        {
            Result entry = entries[index];
            history.Rows.Add(entry.Timestamp.ToString("yyyy-MM-dd HH:mm"), entry.Download.ToString("0.00") + " Mbps", entry.Upload.ToString("0.00") + " Mbps", entry.Ping.ToString("0.00") + " ms", entry.Server);
        }
    }

    private void ExportCsv()
    {
        using (var dialog = new SaveFileDialog { Filter = "CSV-Datei|*.csv", FileName = "speedtest-history.csv" })
        if (dialog.ShowDialog() == DialogResult.OK)
        {
            var rows = new List<string> { "Timestamp,Download Mbps,Upload Mbps,Ping ms,Jitter ms,Packet loss,Server,ISP" };
            foreach (Result entry in entries) rows.Add(String.Join(",", entry.Timestamp.ToString("s"), entry.Download.ToString(CultureInfo.InvariantCulture), entry.Upload.ToString(CultureInfo.InvariantCulture), entry.Ping.ToString(CultureInfo.InvariantCulture), entry.Jitter.ToString(CultureInfo.InvariantCulture), entry.Loss.ToString(CultureInfo.InvariantCulture), '"' + entry.Server.Replace("\"", "\"\"") + '"', '"' + entry.Isp.Replace("\"", "\"\"") + '"'));
            File.WriteAllLines(dialog.FileName, rows.ToArray(), Encoding.UTF8); status.Text = "CSV exportiert";
        }
    }
}

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false); Application.Run(new MainForm());
    }
}
